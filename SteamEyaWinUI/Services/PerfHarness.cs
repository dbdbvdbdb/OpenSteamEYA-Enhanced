#if PERF_HARNESS
using System.Diagnostics;
using System.Globalization;
using System.Runtime;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SteamEyaWinUI.Pages;

namespace SteamEyaWinUI.Services;

/// <summary>
/// 性能测量脚手架（仅在 EnablePerfHarness=true 时编译进来）。
/// 用法：SteamEyaWinUI.exe --perf-run [--perf-mode=standard|scale|soak|heights] [--perf-rounds=3] [--perf-accounts=100]
///       [--perf-dir=&lt;目录&gt;] [--perf-label=&lt;标签&gt;]
/// mode=scale 只跑 V1 前置测量：账号数缩放（进页面耗时）与离页释放实验，不跑 S1–S9。
/// mode=soak 反复进出账号管理页，测 30 次进入的私有内存斜率（memory.csv 增列 soak_cycle）。
/// mode=heights 注入 4 类状态变体，测列表/卡片两视图的卡片高度一致性与每屏实建卡片数（heights.csv）。
/// 输出：&lt;dir&gt;\measure.csv、memory.csv、environment.json、summary.txt、log.txt（heights 另出 heights.csv）
/// </summary>
internal static class PerfHarness
{
    internal static bool Enabled { get; private set; }
    private static int _rounds = 3;
    private static int _accounts = 100;
    private static string _baseDir = "";
    private static string _label = "";
    private static string _mode = "standard";

    private static readonly List<(string Scenario, int Round, double ActionMs, double? StableMs, int Renders,
        int Cards, int Gc0, int Gc1, int Gc2, double GcPauseMs, double UiBlockMs, string Note)> Rows = [];

    private static readonly List<(string Point, long Private, long WorkingSet, long Managed, int Cycle)> Memory = [];

    // soak / heights 两模式的专用状态：只在对应模式下填充，standard/scale 输出保持原样。
    private static readonly List<double> SoakPrivateMb = [];
    private static readonly List<(int Cycle, double BeforeMb, double AfterMb, string Loh)> SoakGcSamples = [];
    private static int _soakCycles;
    private static readonly List<(string View, int Pass, int Index, string ItemId, int Variant, double CardHeight,
        double ContainerHeight, bool Realized)> Heights = [];
    private static readonly List<(string View, int ItemCount, int Renders, int CardsCreated)> HeightViews = [];

    private static DispatcherQueue? _dispatcher;

    // environment.json 里的启动基线必须来自启动瞬间，不能等到 WriteOutputs 再取（那时已加载 100 张卡片）。
    private static long _startupPrivateBytes;
    private static long _startupWorkingSetBytes;
    private static long _startupManagedBytes;

    // 16ms 探针：记录相邻 tick 的最大间隔（≈ UI 线程最长阻塞），仅在探针运行期间累计。
    private static DispatcherQueueTimer? _probeTimer;
    private static long _probeLastTicks;
    private static double _probeMaxBlockMs;
    private const int ProbeIntervalMs = 16;

    private static readonly Stopwatch RunWatch = new();

    internal static void Configure(string[] args)
    {
        Enabled = args.Any(x => string.Equals(x, "--perf-run", StringComparison.OrdinalIgnoreCase));
        if (!Enabled)
        {
            return;
        }

        _rounds = Math.Max(1, ReadInt(args, "--perf-rounds=", 3));
        _accounts = Math.Max(10, ReadInt(args, "--perf-accounts=", 100));
        var dir = args.FirstOrDefault(x => x.StartsWith("--perf-dir=", StringComparison.OrdinalIgnoreCase));
        _baseDir = dir is null
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SteamEYA", "perf",
                DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture))
            : dir[("--perf-dir=".Length)..];
        _label = ReadText(args, "--perf-label=", "");
        var mode = ReadText(args, "--perf-mode=", "standard").Trim().ToLowerInvariant();
        _mode = mode is "scale" or "soak" or "heights" ? mode : "standard";
        Directory.CreateDirectory(_baseDir);
    }

    private static int ReadInt(string[] args, string prefix, int fallback)
    {
        var value = args.FirstOrDefault(x => x.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        return value is not null && int.TryParse(value[prefix.Length..], out var parsed) ? parsed : fallback;
    }

    private static string ReadText(string[] args, string prefix, string fallback)
    {
        var value = args.FirstOrDefault(x => x.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        return value is null ? fallback : value[prefix.Length..];
    }

    internal static async Task RunAsync(MainWindow window)
    {
        var log = new List<string>();
        void Note(string text)
        {
            log.Add(text);
            Console.WriteLine("[perf] " + text);
        }

        RunWatch.Start();
        try
        {
            Note("start dir=" + _baseDir + " mode=" + _mode + " rounds=" + _rounds + " accounts=" + _accounts +
                 " label=" + (_label.Length == 0 ? "-" : _label));
            CaptureMemory("startup");
            _startupPrivateBytes = Memory[0].Private;
            _startupWorkingSetBytes = Memory[0].WorkingSet;
            _startupManagedBytes = Memory[0].Managed;
            Note("startup private=" + Mb(_startupPrivateBytes) + "MB working_set=" + Mb(_startupWorkingSetBytes) +
                 "MB managed=" + Mb(_startupManagedBytes) + "MB");

            _dispatcher = window.DispatcherQueue;
            StartUiProbe(_dispatcher);
            window.Nav.SelectedItem = window.ManagerNav;
            var page = await WaitForManagerAsync(window, 15_000);
            if (page is null)
            {
                Note("FAIL manager page not found");
                return;
            }

#if MANAGER_UI_CHECKS
            await page.PrepareUiCheckAsync(_accounts);
#else
            Note("seeding skipped: MANAGER_UI_CHECKS not compiled in");
#endif
            await SettleAsync(300);
            var cards = FindFirst<GridView>(page, "Cards")!;
            var search = FindFirst<TextBox>(page, "Search")!;
            Note("seeded cards=" + cards.Items.Count + " accounts=" + _accounts);
            CaptureMemory("after_seed");

            if (_mode == "scale")
            {
                await RunScaleAsync(window, page, cards, log);
                StopUiProbe();
                WriteOutputs(log);
                Note("done dir=" + _baseDir);
                return;
            }

            if (_mode == "soak")
            {
                await RunSoakAsync(window, page, cards, log);
                StopUiProbe();
                WriteOutputs(log);
                Note("done dir=" + _baseDir);
                return;
            }

            if (_mode == "heights")
            {
                await RunHeightsAsync(page, cards, log);
                StopUiProbe();
                WriteOutputs(log);
                Note("done dir=" + _baseDir);
                return;
            }

            // S1 批量变更通知：连续 20 次 QueueReload。度量点＝尾随节流窗口结束且合并后的刷新真正执行完。
            await MeasureAsync(page, "S1_batch_notifications", async () =>
            {
                for (var i = 0; i < 20; i++)
                {
                    page.QueueReload();
                    await Task.Delay(5);
                }
            }, log);

            // S2 搜索输入：逐字符输入 6 个字符（含 200ms 去抖窗口），每轮前先清空以便重复触发。
            await MeasureAsync(page, "S2_search_typing", async () =>
            {
                search.Text = "";
                await Task.Delay(30);
                foreach (var ch in "qa-acc")
                {
                    search.Text += ch;
                    await Task.Delay(40);
                }
                await Task.Delay(260);
            }, log);
            search.Text = "";

            // S3 窗口缩放：1180x780 → 1680x980 → 复原（末尾等待尺寸真正落定，保证下一轮起点一致）。
            await MeasureAsync(page, "S3_resize", async () =>
            {
                window.AppWindow.Resize(new Windows.Graphics.SizeInt32(1180, 780));
                await SettleAsync(60);
                window.AppWindow.Resize(new Windows.Graphics.SizeInt32(1680, 980));
                await SettleAsync(60);
                window.AppWindow.Resize(new Windows.Graphics.SizeInt32(1180, 780));
                await SettleAsync(150);
            }, log);

            // S4 滚动：滚到尾部再回顶。
            await MeasureAsync(page, "S4_scroll", async () =>
            {
                if (cards.Items.Count > 0)
                {
                    cards.ScrollIntoView(cards.Items[^1]);
                    await SettleAsync(60);
                    cards.ScrollIntoView(cards.Items[0]);
                }
            }, log);

            // S5 切换主题：浅 → 深 → 浅。
            await MeasureAsync(page, "S5_theme_switch", async () =>
            {
                window.ApplyTheme(ElementTheme.Light);
                await SettleAsync(60);
                window.ApplyTheme(ElementTheme.Dark);
                await SettleAsync(60);
                window.ApplyTheme(ElementTheme.Light);
            }, log);

            // S6 页面切换：白号管理 → 设置 → 白号管理（往返，含新页面重建）。
            await MeasureAsync(page, "S6_navigation", async () =>
            {
                window.Nav.SelectedItem = window.SettingsNav;
                await SettleAsync(120);
                _dispatcher = window.DispatcherQueue;
                window.Nav.SelectedItem = window.ManagerNav;
                await SettleAsync(120);
            }, log);
            // 页面切回后 Frame 会创建新实例，后续场景必须重新取页，否则量到的是已离页实例。
            page = await WaitForManagerAsync(window, 5_000) ?? page;
            cards = FindFirst<GridView>(page, "Cards") ?? cards;
            Note("re-acquired page after S6 loaded=" + page.IsLoaded);

            // S7 快照加载（纯数据层）。
            await MeasureAsync(page, "S7_snapshot", async () =>
            {
                _ = await ManagedAccounts.SnapshotAsync();
            }, log);

            // S8 冷却 tick：页面级计时器驱动全部卡片。
            Note("S8 skipped: cooldown ticker needs live cooldown data");

            // S9 隐私模式切换（开关会真实落盘并触发刷新）。
            var privacy = FindFirst<Microsoft.UI.Xaml.Controls.Primitives.ToggleButton>(page, "Privacy");
            if (privacy is not null)
            {
                await MeasureAsync(page, "S9_privacy_toggle", async () =>
                {
                    page.TogglePrivacyForCheck(true);
                    await SettleAsync(60);
                    page.TogglePrivacyForCheck(false);
                    await SettleAsync(60);
                    // 隐私处理是 fire-and-forget（async void），给落盘与刷新留出启动时间，
                    // 否则测量窗口会在刷新真正开始之前就关闭（三个构建用同一等待，比较仍公平）。
                    await SettleAsync(150);
                }, log);
                Note("S9 state after measurement privacy=" + page.IsPrivate);
            }
            else
            {
                Note("S9 skipped: privacy toggle not found");
            }

            // S10 固定预算批量刷新：清空后重新载入 10k 账号。
            Note("S10 skipped: fixed-budget 10k reload not implemented");

            CaptureMemory("after_scenarios");
            Note("gc before forced collection: " + GcCounters());

            // 全部场景结束后强制一次完整 GC，用于区分“可达对象持有”与“尚未回收的垃圾”。
            ForceGc();
            await SettleAsync(200);
            CaptureMemory("after_forced_gc");
            Note("gc after forced collection: " + GcCounters());

            StopUiProbe();
            WriteOutputs(log);
            Note("done dir=" + _baseDir);
        }
        catch (Exception ex)
        {
            Note("FAIL " + ex.GetType().Name + ": " + ex.Message);
            try
            {
                StopUiProbe();
                WriteOutputs(log);
            }
            catch
            {
                // 输出失败时保留控制台日志即可。
            }
        }
        finally
        {
            await Task.Delay(300);
            Application.Current.Exit();
        }
    }

    /// <summary>
    /// V1 前置测量（--perf-mode=scale）：
    /// 1) 进页面耗时——从设置页切回白号管理，等到页面刷新与渲染完成（含 UI 最长阻塞、渲染/卡片增量、私有内存）。
    /// 2) 离页释放实验——记录“N 张卡常驻”的私有内存，离页触发 ReleaseCards 后再记一次，强制 GC 后再记一次，
    ///    用来区分“卡片常驻”与“分配器高水位/原生内存未归还”。
    /// </summary>
    private static async Task RunScaleAsync(MainWindow window, AccountManagerPage page, GridView cards, List<string> log)
    {
        void Note(string text)
        {
            log.Add(text);
            Console.WriteLine("[perf] " + text);
        }

        var enterRounds = Math.Max(1, _rounds);
        Note("scale mode: accounts=" + _accounts + " enter_rounds=" + enterRounds);
        Note("after_seed cards=" + cards.Items.Count + " private=" + Mb(Memory[^1].Private) + "MB");

        for (var i = 1; i <= enterRounds; i++)
        {
            await page.WaitForIdleAsync();
            window.Nav.SelectedItem = window.SettingsNav;
            await SettleAsync(200);

            ResetProbe();
            var gc0 = GC.CollectionCount(0);
            var gc1 = GC.CollectionCount(1);
            var gc2 = GC.CollectionCount(2);
            var pauseBefore = GC.GetTotalPauseDuration();
            var (rendersBefore, cardsBefore) = SnapshotCounters();
            CaptureMemory("enter_before_r" + i);

            var watch = Stopwatch.StartNew();
            window.Nav.SelectedItem = window.ManagerNav;
            page = await WaitForManagerAsync(window, 15_000) ?? page;
            await page.WaitForIdleAsync();
            var actionMs = watch.Elapsed.TotalMilliseconds;
            var stableMs = await SettleAsync(0);

            var uiBlockMs = TakeProbeMax();
            var (rendersAfter, cardsAfter) = SnapshotCounters();
            var gcPauseMs = (GC.GetTotalPauseDuration() - pauseBefore).TotalMilliseconds;
            CaptureMemory("enter_after_r" + i);
            Rows.Add(("S0_enter_manager", i, actionMs, stableMs, rendersAfter - rendersBefore, cardsAfter - cardsBefore,
                GC.CollectionCount(0) - gc0, GC.CollectionCount(1) - gc1, GC.CollectionCount(2) - gc2, gcPauseMs,
                uiBlockMs, "accounts=" + _accounts));
            Note("S0_enter_manager #" + i +
                 " action=" + actionMs.ToString("0.0", CultureInfo.InvariantCulture) + "ms" +
                 " stable=" + (stableMs?.ToString("0.0", CultureInfo.InvariantCulture) ?? "-") + "ms" +
                 " renders=" + (rendersAfter - rendersBefore) + " cards=" + (cardsAfter - cardsBefore) +
                 " ui_block=" + uiBlockMs.ToString("0.0", CultureInfo.InvariantCulture) + "ms" +
                 " private=" + Mb(Memory[^1].Private) + "MB");

            cards = FindFirst<GridView>(page, "Cards") ?? cards;
            Note("enter round " + i + " page_cards=" + cards.Items.Count + " is_loaded=" + page.IsLoaded);
        }

        // 离页释放实验：此时页面刚从设置页切回并渲染完 N 张卡。
        var cardsOnPage = cards.Items.Count;
        CaptureMemory("release_before");
        var beforePrivate = Memory[^1].Private;
        var beforeManaged = Memory[^1].Managed;
        Note("release before: cards=" + cardsOnPage + " private=" + Mb(beforePrivate) + "MB managed=" +
             Mb(beforeManaged) + "MB");

        ResetProbe();
        var relGc0 = GC.CollectionCount(0);
        var relGc1 = GC.CollectionCount(1);
        var relGc2 = GC.CollectionCount(2);
        var relPauseBefore = GC.GetTotalPauseDuration();
        var releaseWatch = Stopwatch.StartNew();
        window.Nav.SelectedItem = window.SettingsNav;
        await SettleAsync(250); // 让离页排队的 ReleaseCards 真正执行（低优先级回调）
        var releaseMs = releaseWatch.Elapsed.TotalMilliseconds;
        var releaseUiBlockMs = TakeProbeMax();
        CaptureMemory("release_after_clear");

        ForceGc();
        await SettleAsync(250);
        CaptureMemory("release_after_gc");
        Rows.Add(("R1_release_cards", 1, releaseMs, null, 0, 0,
            GC.CollectionCount(0) - relGc0, GC.CollectionCount(1) - relGc1, GC.CollectionCount(2) - relGc2,
            (GC.GetTotalPauseDuration() - relPauseBefore).TotalMilliseconds, releaseUiBlockMs,
            "cards=" + cardsOnPage + " accounts=" + _accounts));
        Note("release after clear: private=" + Mb(Memory[^2].Private) + "MB managed=" + Mb(Memory[^2].Managed) + "MB");
        Note("release after forced gc: private=" + Mb(Memory[^1].Private) + "MB managed=" + Mb(Memory[^1].Managed) +
             "MB");
        Note("release delta: clear_private=" + Mb(beforePrivate - Memory[^2].Private) + "MB gc_private=" +
             Mb(Memory[^2].Private - Memory[^1].Private) + "MB managed_total=" + Mb(beforeManaged - Memory[^1].Managed) +
             "MB");
    }

    private static void ForceGc()
    {
        GC.Collect(2, GCCollectionMode.Forced, true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, true);
    }

    /// <summary>
    /// soak（--perf-mode=soak）：反复进出账号管理页，观察私有内存斜率是否收敛。
    /// 每轮＝切设置页 → 切回（Frame 重建页面与全部卡片）→ 等刷新/渲染完成 → 采样一行。
    /// 第 10/20/30 轮额外请求一次 LOH 压缩 + 强制 GC 再采样，用来区分“可回收垃圾”与“常驻高水位”；
    /// 短跑（cycles&lt;10）把采样点退化为末轮，否则冒烟拿不到任何 GC 对照数据。
    /// </summary>
    private static async Task RunSoakAsync(MainWindow window, AccountManagerPage page, GridView cards, List<string> log)
    {
        void Note(string text)
        {
            log.Add(text);
            Console.WriteLine("[perf] " + text);
        }

        var cycles = Math.Max(1, _rounds);
        _soakCycles = cycles;
        Note("soak mode: accounts=" + _accounts + " cycles=" + cycles);
        Note("after_seed cards=" + cards.Items.Count + " private=" + Mb(Memory[^1].Private) + "MB");
        if (cycles < 10)
        {
            Note("soak note: cycles<10, GC 采样点退化为末轮 cycle=" + cycles + "（正式口径为 10/20/30）");
        }

        for (var i = 1; i <= cycles; i++)
        {
            await page.WaitForIdleAsync();
            window.Nav.SelectedItem = window.SettingsNav;
            await SettleAsync(200);

            ResetProbe();
            var gc0 = GC.CollectionCount(0);
            var gc1 = GC.CollectionCount(1);
            var gc2 = GC.CollectionCount(2);
            var pauseBefore = GC.GetTotalPauseDuration();
            var (rendersBefore, cardsBefore) = SnapshotCounters();
            var watch = Stopwatch.StartNew();
            window.Nav.SelectedItem = window.ManagerNav;
            page = await WaitForManagerAsync(window, 15_000) ?? page;
            await page.WaitForIdleAsync();
            var actionMs = watch.Elapsed.TotalMilliseconds;
            var stableMs = await SettleAsync(0);

            var uiBlockMs = TakeProbeMax();
            var (rendersAfter, cardsAfter) = SnapshotCounters();
            var gcPauseMs = (GC.GetTotalPauseDuration() - pauseBefore).TotalMilliseconds;
            CaptureMemory("soak_enter_" + i, i);
            var snapshot = Memory[^1];
            var memoryInfo = GC.GetGCMemoryInfo();
            SoakPrivateMb.Add(snapshot.Private / 1024.0 / 1024.0);
            Rows.Add(("S0_enter_manager", i, actionMs, stableMs, rendersAfter - rendersBefore, cardsAfter - cardsBefore,
                GC.CollectionCount(0) - gc0, GC.CollectionCount(1) - gc1, GC.CollectionCount(2) - gc2, gcPauseMs,
                uiBlockMs, "soak cycle=" + i + " accounts=" + _accounts));
            Note("cycle=" + i + " private=" + Mb(snapshot.Private) + "MB ws=" + Mb(snapshot.WorkingSet) + "MB managed=" +
                 Mb(snapshot.Managed) + "MB heap=" + Mb(memoryInfo.HeapSizeBytes) + "MB fragmented=" +
                 Mb(memoryInfo.FragmentedBytes) + "MB committed=" + Mb(memoryInfo.TotalCommittedBytes) + "MB" +
                 " renders=" + (rendersAfter - rendersBefore) + " cards=" + (cardsAfter - cardsBefore) + " enter_ms=" +
                 actionMs.ToString("0.0", CultureInfo.InvariantCulture));

            // 每 5 次进入打一行斜率：最近 5 次的平均单次私有内存增量。
            if (i % 5 == 0 && SoakPrivateMb.Count > 1)
            {
                var span = Math.Min(5, SoakPrivateMb.Count - 1);
                var delta = SoakPrivateMb[^1] - SoakPrivateMb[^(span + 1)];
                Note("slope last" + span + ": avg=" +
                     (delta / span).ToString("+0.00;-0.00", CultureInfo.InvariantCulture) + " MB/cycle (private " +
                     SoakPrivateMb[^(span + 1)].ToString("0.0", CultureInfo.InvariantCulture) + "MB -> " +
                     SoakPrivateMb[^1].ToString("0.0", CultureInfo.InvariantCulture) + "MB)");
            }

            if (i % 10 == 0 || (cycles < 10 && i == cycles))
            {
                var beforeMb = SoakPrivateMb[^1];
                var loh = TryCompactLoh();
                ForceGc();
                await SettleAsync(120);
                CaptureMemory("soak_after_gc_" + i, i);
                var afterMb = Memory[^1].Private / 1024.0 / 1024.0;
                SoakGcSamples.Add((i, beforeMb, afterMb, loh));
                Note("after_gc cycle=" + i + " loh=" + loh + " before=" +
                     beforeMb.ToString("0.0", CultureInfo.InvariantCulture) + "MB after=" +
                     afterMb.ToString("0.0", CultureInfo.InvariantCulture) + "MB drop=" +
                     (beforeMb - afterMb).ToString("0.00", CultureInfo.InvariantCulture) + "MB");
            }

            cards = FindFirst<GridView>(page, "Cards") ?? cards;
        }

        var sequence = new List<string>();
        for (var i = 0; i < SoakPrivateMb.Count; i++)
        {
            sequence.Add((i + 1) + "=" + SoakPrivateMb[i].ToString("0.0", CultureInfo.InvariantCulture));
        }
        Note("soak private sequence: " + string.Join(" ", sequence));
    }

    /// <summary>请求下一次 GC 压缩 LOH。属性不可用（或读回不一致）时如实降级，不伪造效果。</summary>
    private static string TryCompactLoh()
    {
        try
        {
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            return GCSettings.LargeObjectHeapCompactionMode == GCLargeObjectHeapCompactionMode.CompactOnce
                ? "requested-confirmed"
                : "set-but-readback-mismatch";
        }
        catch (Exception ex)
        {
            return "LOH compaction unavailable (" + ex.GetType().Name + ")";
        }
    }

    /// <summary>
    /// heights（--perf-mode=heights）：给账号注入 4 类状态变体（2 行冷却+GC 封禁 / 1 行冷却 / 正常 / 未知），
    /// 然后列表与卡片两视图各滚两遍，逐批（8 个）记录已实现卡片的 ActualHeight 与容器 ActualHeight，
    /// 用来看“同一屏内卡片高度是否一致”以及每屏真实创建了多少张卡片。
    /// </summary>
    private static async Task RunHeightsAsync(AccountManagerPage page, GridView cards, List<string> log)
    {
        void Note(string text)
        {
            log.Add(text);
            Console.WriteLine("[perf] " + text);
        }

        Note("heights mode: accounts=" + _accounts);

#if MANAGER_UI_CHECKS
        var variantById = await InjectStatusFixturesAsync();
        Note("fixtures enabled=" + ManagerUiChecks.Enabled + " injected=" + variantById.Count +
             " variants=1:2line 2:1line 3:normal 4:unknown");

        // 注入后先让页面按新变体重建一次卡片，避免旧卡片（无 fixture）混进第一遍测量。
        page.QueueReload();
        await page.WaitForIdleAsync();
        await SettleAsync(200);

        foreach (var view in new[] { "list", "cards" })
        {
            var (rendersBefore, cardsBefore) = SnapshotCounters();
            await page.SwitchUiCheckAsync(view, false);
            await page.WaitForIdleAsync();
            await SettleAsync(150);
            cards = FindFirst<GridView>(page, "Cards") ?? cards;
            var (rendersAfter, cardsAfter) = SnapshotCounters();
            HeightViews.Add((view, cards.Items.Count, rendersAfter - rendersBefore, cardsAfter - cardsBefore));
            Note("view=" + view + " items=" + cards.Items.Count + " renders_delta=" + (rendersAfter - rendersBefore) +
                 " cards_created_delta=" + (cardsAfter - cardsBefore));
            await MeasureHeightsAsync(cards, view, variantById, log);
        }
#else
        Note("heights skipped: MANAGER_UI_CHECKS not compiled in");
        await Task.CompletedTask;
        _ = page;
        _ = cards;
#endif
    }

#if MANAGER_UI_CHECKS
    /// <summary>
    /// 按下标 i%4 注入变体：0→2 行（冷却 + GC 封禁，最坏）、1→1 行（仅冷却）、2→正常、3→不注入（未知）。
    /// 变体号按 1..4 记录到 heights.csv；键是账号 Id，即使渲染顺序变化也不会错配。
    /// </summary>
    private static async Task<Dictionary<Guid, int>> InjectStatusFixturesAsync()
    {
        var variantById = new Dictionary<Guid, int>();
        var document = await ManagedAccounts.Store.LoadAsync();
        var ordered = document.Accounts.OrderBy(x => x.Order).ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            var account = ordered[i];
            var variant = i % 4 + 1;
            variantById[account.Id] = variant;
            switch (variant)
            {
                case 1:
                    ManagerUiChecks.StatusFixtures[account.Id] = new Models.SteamAccountHistoryItem
                    {
                        CooldownSeconds = 3605, GcVacBanned = true, CsStatusUpdatedAt = DateTimeOffset.Now,
                    };
                    break;
                case 2:
                    ManagerUiChecks.StatusFixtures[account.Id] = new Models.SteamAccountHistoryItem
                    {
                        CooldownSeconds = 3605, GcVacBanned = false, CsStatusUpdatedAt = DateTimeOffset.Now,
                    };
                    break;
                case 3:
                    ManagerUiChecks.StatusFixtures[account.Id] = new Models.SteamAccountHistoryItem
                    {
                        CooldownSeconds = 0, GcVacBanned = false,
                    };
                    break;
                default:
                    ManagerUiChecks.StatusFixtures.Remove(account.Id);
                    break;
            }
        }

        return variantById;
    }
#endif

    private static async Task MeasureHeightsAsync(GridView cards, string view, Dictionary<Guid, int> variantById,
        List<string> log)
    {
        void Note(string text)
        {
            log.Add(text);
            Console.WriteLine("[perf] " + text);
        }

        const int Batch = 8;
        var total = cards.Items.Count;
        for (var pass = 0; pass < 2; pass++)
        {
            if (pass > 0 && total > 0)
            {
                // 第二遍从顶部重新走同一批数据，作为“同一批卡片两遍测量”的一致性对照。
                cards.ScrollIntoView(cards.Items[0]);
                await SettleAsync(150);
            }

            var realized = 0;
            for (var start = 0; start < total; start += Batch)
            {
                var end = Math.Min(start + Batch, total);
                for (var i = start; i < end; i++)
                {
                    cards.ScrollIntoView(cards.Items[i]);
                }

                await SettleAsync(60);
                for (var i = start; i < end; i++)
                {
                    var item = cards.Items[i];
                    var id = AccountIdOf(item);
                    var variant = id != Guid.Empty
                        ? variantById.GetValueOrDefault(id, 0)
                        : i % 4 + 1;
                    var container = cards.ContainerFromItem(item) as FrameworkElement;
                    var cardHeight = double.NaN;
                    var containerHeight = double.NaN;
                    var isRealized = false;
                    if (container is not null)
                    {
                        containerHeight = container.ActualHeight;
                        var card = item as AccountCard ?? FindAll(container).OfType<AccountCard>().FirstOrDefault();
                        if (card is not null && card.ActualHeight > 0)
                        {
                            cardHeight = card.ActualHeight;
                            isRealized = true;
                            realized++;
                        }
                    }

                    Heights.Add((view, pass, i, ShortId(id), variant, cardHeight, containerHeight, isRealized));
                }
            }

            Note("heights view=" + view + " pass=" + pass + " realized=" + realized + "/" + total);
        }
    }

    /// <summary>卡片是 GridView 的 item 本身（当前实现）；将来虚拟化后 item 可能是视图模型，拿不到就记空 Id。</summary>
    private static Guid AccountIdOf(object item) => item is AccountCard card ? card.Account.Id : Guid.Empty;

    private static string ShortId(Guid id) => id == Guid.Empty ? "" : id.ToString("N")[..8];

    /// <summary>先等到上一场景完全静默，再开始计时，避免场景间互相污染。</summary>
    private static async Task MeasureAsync(AccountManagerPage page, string scenario, Func<Task> action, List<string> log)
    {
        var rounds = Math.Max(1, _rounds);
        for (var i = 0; i < rounds + 1; i++)
        {
            await page.WaitForIdleAsync();
            ResetProbe();
            var gc0 = GC.CollectionCount(0);
            var gc1 = GC.CollectionCount(1);
            var gc2 = GC.CollectionCount(2);
            var pauseBefore = GC.GetTotalPauseDuration();
            var (rendersBefore, cardsBefore) = SnapshotCounters();
            var measured = i > 0;
            if (measured)
            {
                CaptureMemory(scenario + "_before");
            }

            var watch = Stopwatch.StartNew();
            await action();
            await page.WaitForIdleAsync();
            var actionMs = watch.Elapsed.TotalMilliseconds;
            var stableMs = await SettleAsync(0);

            var uiBlockMs = TakeProbeMax();
            var (rendersAfter, cardsAfter) = SnapshotCounters();
            var gcPauseMs = (GC.GetTotalPauseDuration() - pauseBefore).TotalMilliseconds;
            if (!measured)
            {
                log.Add("[perf] warmup " + scenario + " " + actionMs.ToString("0.0", CultureInfo.InvariantCulture) + " ms");
                continue;
            }

            CaptureMemory(scenario + "_after");
            Rows.Add((scenario, i, actionMs, stableMs, rendersAfter - rendersBefore, cardsAfter - cardsBefore,
                GC.CollectionCount(0) - gc0, GC.CollectionCount(1) - gc1, GC.CollectionCount(2) - gc2, gcPauseMs,
                uiBlockMs, ""));
            log.Add("[perf] " + scenario + " #" + i +
                    " action=" + actionMs.ToString("0.0", CultureInfo.InvariantCulture) + "ms" +
                    " stable=" + (stableMs?.ToString("0.0", CultureInfo.InvariantCulture) ?? "-") + "ms" +
                    " renders=" + (rendersAfter - rendersBefore) + " cards=" + (cardsAfter - cardsBefore) +
                    " gc=" + (GC.CollectionCount(0) - gc0) + "/" + (GC.CollectionCount(1) - gc1) + "/" +
                    (GC.CollectionCount(2) - gc2) + " gc_pause=" + gcPauseMs.ToString("0.0", CultureInfo.InvariantCulture) +
                    "ms ui_block=" + uiBlockMs.ToString("0.0", CultureInfo.InvariantCulture) + "ms");
        }
    }

    private static (int Renders, int Cards) SnapshotCounters()
    {
#if MANAGER_UI_CHECKS
        return (UiCheckPerf.RenderCount, UiCheckPerf.CardCreations);
#else
        return (0, 0);
#endif
    }

    private static void StartUiProbe(DispatcherQueue dispatcher)
    {
        _probeTimer = dispatcher.CreateTimer();
        _probeTimer.Interval = TimeSpan.FromMilliseconds(ProbeIntervalMs);
        _probeTimer.IsRepeating = true;
        _probeLastTicks = Environment.TickCount64;
        _probeTimer.Tick += (_, _) =>
        {
            var now = Environment.TickCount64;
            var gap = now - _probeLastTicks - ProbeIntervalMs;
            if (gap > _probeMaxBlockMs)
            {
                _probeMaxBlockMs = gap;
            }

            _probeLastTicks = now;
        };
        _probeTimer.Start();
    }

    private static void StopUiProbe()
    {
        _probeTimer?.Stop();
        _probeTimer = null;
    }

    private static void ResetProbe()
    {
        _probeMaxBlockMs = 0;
        _probeLastTicks = Environment.TickCount64;
    }

    private static double TakeProbeMax()
    {
        var value = _probeMaxBlockMs;
        _probeMaxBlockMs = 0;
        _probeLastTicks = Environment.TickCount64;
        return value;
    }

    /// <summary>等待 UI 线程排空：连续两次低优先级回调 + 一次渲染帧。</summary>
    private static async Task<double?> SettleAsync(int extraDelayMs)
    {
        var watch = Stopwatch.StartNew();
        for (var i = 0; i < 2; i++)
        {
            var tcs = new TaskCompletionSource();
            if (!_dispatcher!.TryEnqueue(DispatcherQueuePriority.Low, () => tcs.TrySetResult()))
            {
                return null;
            }

            await tcs.Task;
        }

        if (extraDelayMs > 0)
        {
            await Task.Delay(extraDelayMs);
        }

        return watch.Elapsed.TotalMilliseconds;
    }

    private static async Task<AccountManagerPage?> WaitForManagerAsync(MainWindow window, int timeoutMs)
    {
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < timeoutMs)
        {
            if (window.MainFrame.Content is AccountManagerPage page && page.IsLoaded)
            {
                return page;
            }

            await Task.Delay(50);
        }

        return window.MainFrame.Content as AccountManagerPage;
    }

    private static T? FindFirst<T>(FrameworkElement root, string name) where T : FrameworkElement =>
        FindAll(root).OfType<T>().FirstOrDefault(x => x.Name == name);

    private static IEnumerable<FrameworkElement> FindAll(DependencyObject root)
    {
        var count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement element)
            {
                yield return element;
            }

            foreach (var nested in FindAll(child))
            {
                yield return nested;
            }
        }
    }

    private static void CaptureMemory(string point) => CaptureMemory(point, 0);

    private static void CaptureMemory(string point, int cycle)
    {
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        Memory.Add((point, process.PrivateMemorySize64, process.WorkingSet64, GC.GetTotalMemory(false), cycle));
    }

    private static string GcCounters() =>
        "gc0=" + GC.CollectionCount(0) + " gc1=" + GC.CollectionCount(1) + " gc2=" + GC.CollectionCount(2) +
        " pause_total=" + GC.GetTotalPauseDuration().TotalMilliseconds.ToString("0.0", CultureInfo.InvariantCulture) + "ms";

    private static string Mb(long bytes) => (bytes / 1024.0 / 1024.0).ToString("0.0", CultureInfo.InvariantCulture);

    private static string Px(double value) =>
        double.IsNaN(value) ? "" : value.ToString("0.00", CultureInfo.InvariantCulture);

    private static void WriteOutputs(List<string> log)
    {
        // heights 只关心卡片高度，Rows/Memory 为空，写出来是只有表头的文件：CI 用 measure.csv 判断“跑完了”，
        // 恒写表头既不伪造数据也不会让 heights 运行被判失败。
        {
            var csv = new StringBuilder(
                "scenario,round,action_ms,stable_ms,renders_delta,cards_delta,gc0,gc1,gc2,gc_pause_ms,ui_block_ms,note\n");
            foreach (var row in Rows)
            {
                csv.Append(row.Scenario).Append(',').Append(row.Round).Append(',')
                    .Append(row.ActionMs.ToString("0.0", CultureInfo.InvariantCulture)).Append(',')
                    .Append(row.StableMs?.ToString("0.0", CultureInfo.InvariantCulture) ?? "").Append(',')
                    .Append(row.Renders).Append(',').Append(row.Cards).Append(',')
                    .Append(row.Gc0).Append(',').Append(row.Gc1).Append(',').Append(row.Gc2).Append(',')
                    .Append(row.GcPauseMs.ToString("0.0", CultureInfo.InvariantCulture)).Append(',')
                    .Append(row.UiBlockMs.ToString("0.0", CultureInfo.InvariantCulture)).Append(',')
                    .Append(row.Note).Append('\n');
            }

            // soak 的 memory.csv 增列 soak_cycle，standard/scale 的输出保持原有列不变。
            var soak = _mode == "soak";
            var mem = new StringBuilder(soak
                ? "point,private_mb,working_set_mb,managed_mb,soak_cycle\n"
                : "point,private_mb,working_set_mb,managed_mb\n");
            foreach (var row in Memory)
            {
                mem.Append(row.Point).Append(',')
                    .Append(Mb(row.Private)).Append(',')
                    .Append(Mb(row.WorkingSet)).Append(',')
                    .Append(Mb(row.Managed));
                if (soak)
                {
                    mem.Append(',').Append(row.Cycle > 0 ? row.Cycle.ToString(CultureInfo.InvariantCulture) : "");
                }

                mem.Append('\n');
            }

            File.WriteAllText(Path.Combine(_baseDir, "measure.csv"), csv.ToString());
            File.WriteAllText(Path.Combine(_baseDir, "memory.csv"), mem.ToString());
        }

        if (Heights.Count > 0)
        {
            File.WriteAllText(Path.Combine(_baseDir, "heights.csv"), BuildHeightsCsv());
        }

        File.WriteAllText(Path.Combine(_baseDir, "log.txt"), string.Join(Environment.NewLine, log));
        File.WriteAllText(Path.Combine(_baseDir, "environment.json"), BuildEnvironmentJson());
        File.WriteAllText(Path.Combine(_baseDir, "summary.txt"), BuildSummary());
    }

    private static string BuildHeightsCsv()
    {
        var csv = new StringBuilder("view,pass,index,item_id,variant,card_height,container_height,realized\n");
        foreach (var row in Heights)
        {
            csv.Append(row.View).Append(',').Append(row.Pass).Append(',').Append(row.Index).Append(',')
                .Append(row.ItemId).Append(',').Append(row.Variant).Append(',')
                .Append(Px(row.CardHeight)).Append(',').Append(Px(row.ContainerHeight)).Append(',')
                .Append(row.Realized ? "1" : "0").Append('\n');
        }

        return csv.ToString();
    }

    private static string BuildSummary()
    {
        var builder = new StringBuilder();
        foreach (var group in Rows.GroupBy(x => x.Scenario))
        {
            var ordered = group.Select(x => x.ActionMs).OrderBy(x => x).ToList();
            var median = ordered[ordered.Count / 2];
            var p95 = ordered[Math.Min(ordered.Count - 1, (int)Math.Ceiling(ordered.Count * 0.95) - 1)];
            builder.AppendLine(group.Key + " n=" + ordered.Count +
                               " median=" + median.ToString("0.0", CultureInfo.InvariantCulture) +
                               " p95=" + p95.ToString("0.0", CultureInfo.InvariantCulture));
        }

        if (_mode == "soak")
        {
            AppendSoakSummary(builder);
        }
        else if (_mode == "heights")
        {
            AppendHeightsSummary(builder);
        }

        return builder.ToString();
    }

    /// <summary>
    /// soak 结论：分段平均单次增量（1–10/11–20/21–30）、每次强制 GC 的回落量、以及斜率/基线判定。
    /// 判定口径：斜率逐段递减且 GC 后回落到同一基线 → 高水位封顶；斜率不降且段间基线抬高 → 疑似泄漏。
    /// </summary>
    private static void AppendSoakSummary(StringBuilder builder)
    {
        builder.AppendLine();
        builder.AppendLine("soak: cycles=" + _soakCycles + " accounts=" + _accounts);
        if (SoakPrivateMb.Count > 0)
        {
            var sequence = new List<string>();
            for (var i = 0; i < SoakPrivateMb.Count; i++)
            {
                sequence.Add((i + 1) + "=" + SoakPrivateMb[i].ToString("0.0", CultureInfo.InvariantCulture));
            }

            builder.AppendLine("soak_private_mb: " + string.Join(" ", sequence));
        }

        var means = new List<double>();
        void Segment(int from, int to)
        {
            to = Math.Min(to, SoakPrivateMb.Count);
            if (from > to || to - from < 1)
            {
                return;
            }

            var mean = (SoakPrivateMb[to - 1] - SoakPrivateMb[from - 1]) / (to - from);
            means.Add(mean);
            builder.AppendLine("soak_segment_" + from + "_" + to + ": n=" + (to - from) + " avg=" +
                               mean.ToString("+0.00;-0.00", CultureInfo.InvariantCulture) + " MB/enter");
        }

        Segment(1, 10);
        Segment(11, 20);
        Segment(21, 30);

        foreach (var sample in SoakGcSamples)
        {
            builder.AppendLine("soak_after_gc cycle=" + sample.Cycle + " loh=" + sample.Loh + " before=" +
                               sample.BeforeMb.ToString("0.0", CultureInfo.InvariantCulture) + "MB after=" +
                               sample.AfterMb.ToString("0.0", CultureInfo.InvariantCulture) + "MB drop=" +
                               (sample.BeforeMb - sample.AfterMb).ToString("0.00", CultureInfo.InvariantCulture) +
                               "MB");
        }

        var spread = double.NaN;
        if (SoakGcSamples.Count >= 2)
        {
            spread = SoakGcSamples[^1].AfterMb - SoakGcSamples[0].AfterMb;
            builder.AppendLine("soak_gc_baseline_spread: " +
                               spread.ToString("+0.0;-0.0", CultureInfo.InvariantCulture) + "MB");
        }

        var decreasing = means.Count >= 2 &&
                         means.Zip(means.Skip(1), (a, b) => b <= a - 0.02).All(x => x);
        var baselineSame = SoakGcSamples.Count >= 2 && Math.Abs(spread) <= 3.0;
        string verdict;
        if (means.Count < 2)
        {
            verdict = "证据不足：cycles=" + _soakCycles + " 只够一段（正式口径 ≥30 次，GC 采样点 10/20/30），" +
                      "当前段均值 " +
                      (means.Count == 1 ? means[0].ToString("+0.00;-0.00", CultureInfo.InvariantCulture) + " MB/enter" : "n/a");
        }
        else if (decreasing && (SoakGcSamples.Count < 2 || baselineSame))
        {
            verdict = "高水位封顶：斜率逐段递减" +
                      (SoakGcSamples.Count >= 2 ? "，且强制 GC 后回落到同一基线" : "（GC 采样不足，基线未对照）");
        }
        else if (!decreasing && SoakGcSamples.Count >= 2 && spread > 3.0)
        {
            verdict = "疑似泄漏：斜率未递减且强制 GC 后基线逐段抬高";
        }
        else
        {
            verdict = "混合信号：斜率" + (decreasing ? "递减" : "未递减") + "，GC 后基线" +
                      (SoakGcSamples.Count >= 2 ? (baselineSame ? "一致" : "抬高") : "样本不足");
        }

        builder.AppendLine("soak_verdict: " + verdict);
    }

    /// <summary>heights 结论：每视图每变体的 min/median/max/去重值个数、整视图一致性（容差 0.5px）、每屏实建卡片数。</summary>
    private static void AppendHeightsSummary(StringBuilder builder)
    {
        builder.AppendLine();
        builder.AppendLine("heights: accounts=" + _accounts);
        foreach (var view in HeightViews)
        {
            builder.AppendLine("heights_view=" + view.View + " items=" + view.ItemCount + " renders_delta=" +
                               view.Renders + " cards_created_delta=" + view.CardsCreated);
        }

        foreach (var viewGroup in Heights.GroupBy(x => x.View))
        {
            foreach (var variantGroup in viewGroup.GroupBy(x => x.Variant).OrderBy(x => x.Key))
            {
                var values = variantGroup.Where(x => x.Realized).Select(x => x.CardHeight).OrderBy(x => x).ToList();
                if (values.Count == 0)
                {
                    builder.AppendLine("heights view=" + viewGroup.Key + " variant=" + variantGroup.Key + " n=0");
                    continue;
                }

                var containers = variantGroup.Where(x => !double.IsNaN(x.ContainerHeight))
                    .Select(x => x.ContainerHeight).OrderBy(x => x).ToList();
                builder.AppendLine("heights view=" + viewGroup.Key + " variant=" + variantGroup.Key + " n=" +
                                   values.Count + " min=" + Px(values[0]) + " median=" +
                                   Px(values[values.Count / 2]) + " max=" + Px(values[^1]) + " distinct=" +
                                   CountDistinct(values, 0.5) + (containers.Count == 0
                                       ? ""
                                       : " container_min=" + Px(containers[0]) + " container_max=" + Px(containers[^1])));
            }

            var realized = viewGroup.Where(x => x.Realized).Select(x => x.CardHeight).ToList();
            if (realized.Count == 0)
            {
                builder.AppendLine("heights_consistency view=" + viewGroup.Key + " realized=0");
                continue;
            }

            var min = realized.Min();
            var max = realized.Max();
            builder.AppendLine("heights_consistency view=" + viewGroup.Key + " consistent=" +
                               (max - min <= 0.5 ? "true" : "false") + " min=" + Px(min) + " max=" + Px(max) +
                               " distinct=" + CountDistinct(realized.OrderBy(x => x).ToList(), 0.5) +
                               " tolerance=0.5");
        }
    }

    /// <summary>按 0.5px 容差数不同的高度值个数（用来量化“高度有几种”）。</summary>
    private static int CountDistinct(List<double> sorted, double tolerance)
    {
        if (sorted.Count == 0)
        {
            return 0;
        }

        var count = 1;
        var anchor = sorted[0];
        foreach (var value in sorted)
        {
            if (value - anchor > tolerance)
            {
                count++;
                anchor = value;
            }
        }

        return count;
    }

    private static string BuildEnvironmentJson()
    {
        var aot = !RuntimeFeature.IsDynamicCodeSupported;
        var gc = GC.GetGCMemoryInfo();
        return "{\n" +
               "  \"label\": \"" + Escape(_label) + "\",\n" +
               "  \"mode\": \"" + Escape(_mode) + "\",\n" +
               "  \"version\": \"" + Escape(typeof(MainWindow).Assembly.GetName().Version?.ToString() ?? "") + "\",\n" +
               "  \"runtime\": \"" + Escape(RuntimeInformation.FrameworkDescription) + "\",\n" +
               "  \"aot\": " + (aot ? "true" : "false") + ",\n" +
               "  \"gc_server\": " + (GCSettings.IsServerGC ? "true" : "false") + ",\n" +
               "  \"gc_latency_mode\": \"" + Escape(GCSettings.LatencyMode.ToString()) + "\",\n" +
               "  \"gc_concurrent\": " + (gc.Concurrent ? "true" : "false") + ",\n" +
               "  \"os\": \"" + Escape(Environment.OSVersion.ToString()) + "\",\n" +
               "  \"arch\": \"" + Escape(RuntimeInformation.ProcessArchitecture.ToString()) + "\",\n" +
               "  \"cores\": " + Environment.ProcessorCount + ",\n" +
               "  \"machine\": \"" + Escape(Environment.MachineName) + "\",\n" +
               "  \"timestamp\": \"" + DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) + "\",\n" +
               "  \"rounds\": " + _rounds + ",\n" +
               "  \"accounts\": " + _accounts + ",\n" +
               "  \"probe_interval_ms\": " + ProbeIntervalMs + ",\n" +
               "  \"startup_private_mb\": " + Mb(_startupPrivateBytes) + ",\n" +
               "  \"startup_working_set_mb\": " + Mb(_startupWorkingSetBytes) + ",\n" +
               "  \"startup_managed_mb\": " + Mb(_startupManagedBytes) + ",\n" +
               "  \"gc_collections\": [" + GC.CollectionCount(0) + ", " + GC.CollectionCount(1) + ", " +
               GC.CollectionCount(2) + "],\n" +
               "  \"gc_pause_total_ms\": " +
               GC.GetTotalPauseDuration().TotalMilliseconds.ToString("0.0", CultureInfo.InvariantCulture) + ",\n" +
               "  \"total_wall_ms\": " + RunWatch.Elapsed.TotalMilliseconds.ToString("0.0", CultureInfo.InvariantCulture) + "\n" +
               "}\n";
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
#endif
