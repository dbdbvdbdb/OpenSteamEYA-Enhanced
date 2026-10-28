#if PERF_HARNESS
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SteamEyaWinUI.Pages;

namespace SteamEyaWinUI.Services;

/// <summary>
/// 性能测量脚手架（仅在 EnablePerfHarness=true 时编译进来）。
/// 用法：SteamEyaWinUI.exe --perf-run [--perf-rounds=5] [--perf-accounts=100] [--perf-dir=&lt;目录&gt;]
/// 输出：&lt;dir&gt;\measure.csv、environment.json、summary.txt
/// </summary>
internal static class PerfHarness
{
    internal static bool Enabled { get; private set; }
    private static int _rounds = 5;
    private static int _accounts = 100;
    private static string _baseDir = "";

    private static readonly List<(string Scenario, int Round, double ActionMs, double? StableMs, string Note)> Rows = [];
    private static readonly List<(string Point, long Private, long WorkingSet, long Managed)> Memory = [];
    private static DispatcherQueue? _dispatcher;

    internal static void Configure(string[] args)
    {
        Enabled = args.Any(x => string.Equals(x, "--perf-run", StringComparison.OrdinalIgnoreCase));
        if (!Enabled)
        {
            return;
        }

        _rounds = Math.Max(1, ReadInt(args, "--perf-rounds=", 5));
        _accounts = Math.Max(10, ReadInt(args, "--perf-accounts=", 100));
        var dir = args.FirstOrDefault(x => x.StartsWith("--perf-dir=", StringComparison.OrdinalIgnoreCase));
        _baseDir = dir is null
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SteamEYA", "perf",
                DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture))
            : dir[("--perf-dir=".Length)..];
        Directory.CreateDirectory(_baseDir);
    }

    private static int ReadInt(string[] args, string prefix, int fallback)
    {
        var value = args.FirstOrDefault(x => x.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        return value is not null && int.TryParse(value[prefix.Length..], out var parsed) ? parsed : fallback;
    }

    internal static async Task RunAsync(MainWindow window)
    {
        var log = new List<string>();
        void Note(string text)
        {
            log.Add(text);
            Console.WriteLine("[perf] " + text);
        }

        try
        {
            Note("start dir=" + _baseDir + " rounds=" + _rounds + " accounts=" + _accounts);
            CaptureMemory("startup");

            _dispatcher = window.DispatcherQueue;
            window.Nav.SelectedItem = window.ManagerNav;
            var page = await WaitForManagerAsync(window, 15_000);
            if (page is null)
            {
                Note("FAIL manager page not found");
                return;
            }

#if MANAGER_UI_CHECKS
            await page.PrepareUiCheckAsync(_accounts);
#endif
            await SettleAsync(300);
            var cards = FindFirst<GridView>(page, "Cards")!;
            var search = FindFirst<TextBox>(page, "Search")!;
            CaptureMemory("after_seed");

            // S1 批量变更通知：连续 20 次 QueueReload，观察合并效果。
            await MeasureAsync("S1_batch_notifications", async () =>
            {
                for (var i = 0; i < 20; i++)
                {
                    page.QueueReload();
                    await Task.Delay(5);
                }
            }, log);

            // S2 搜索输入：逐字符输入 8 个字符再清空。
            await MeasureAsync("S2_search_typing", async () =>
            {
                search.Text = "qa-acc";
                await SettleAsync(40);
            }, log);
            search.Text = "";

            // S3 窗口缩放：1180x780 → 1680x980 → 复原。
            await MeasureAsync("S3_resize", async () =>
            {
                window.AppWindow.Resize(new Windows.Graphics.SizeInt32(1180, 780));
                await SettleAsync(60);
                window.AppWindow.Resize(new Windows.Graphics.SizeInt32(1680, 980));
                await SettleAsync(60);
                window.AppWindow.Resize(new Windows.Graphics.SizeInt32(1180, 780));
            }, log);

            // S4 滚动：滚到尾部再回顶。
            await MeasureAsync("S4_scroll", async () =>
            {
                if (cards.Items.Count > 0)
                {
                    cards.ScrollIntoView(cards.Items[^1]);
                    await SettleAsync(60);
                    cards.ScrollIntoView(cards.Items[0]);
                }
            }, log);

            // S5 切换主题：浅 → 深 → 浅。
            await MeasureAsync("S5_theme_switch", async () =>
            {
                window.ApplyTheme(ElementTheme.Light);
                await SettleAsync(60);
                window.ApplyTheme(ElementTheme.Dark);
                await SettleAsync(60);
                window.ApplyTheme(ElementTheme.Light);
            }, log);

            // S6 页面切换：白号管理 → 设置 → 白号管理。
            await MeasureAsync("S6_navigation", async () =>
            {
                window.Nav.SelectedItem = window.SettingsNav;
                await SettleAsync(120);
                _dispatcher = window.DispatcherQueue;
            window.Nav.SelectedItem = window.ManagerNav;
                await SettleAsync(120);
            }, log);

            // S7 快照加载（纯数据层）。
            await MeasureAsync("S7_snapshot", async () =>
            {
                _ = await ManagedAccounts.SnapshotAsync();
            }, log);

            // S8 冷却 tick：页面级计时器驱动全部卡片。
            Note("S8 skipped: cooldown ticker needs live cooldown data");

            // S9 隐私模式切换。
            var privacy = FindFirst<Microsoft.UI.Xaml.Controls.Primitives.ToggleButton>(page, "Privacy");
            if (privacy is not null)
            {
                await MeasureAsync("S9_privacy_toggle", async () =>
                {
                    page.TogglePrivacyForCheck(true);
                    await SettleAsync(60);
                    page.TogglePrivacyForCheck(false);
                    await SettleAsync(60);
                }, log);
            }
            else
            {
                Note("S9 skipped: privacy toggle not found");
            }

            // S10 固定预算批量刷新：清空后重新载入 10k 账号。
            Note("S10 skipped: fixed-budget 10k reload not implemented");

            CaptureMemory("after_scenarios");
            WriteOutputs(log);
            Note("done dir=" + _baseDir);
        }
        catch (Exception ex)
        {
            Note("FAIL " + ex.GetType().Name + ": " + ex.Message);
            try
            {
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

    private static async Task MeasureAsync(string scenario, Func<Task> action, List<string> log)
    {
        var rounds = Math.Max(1, _rounds);
        for (var i = 0; i < rounds + 1; i++)
        {
            var watch = Stopwatch.StartNew();
            await action();
            var actionMs = watch.Elapsed.TotalMilliseconds;
            var stableMs = await SettleAsync(0);
            if (i == 0)
            {
                log.Add("[perf] warmup " + scenario + " " + actionMs.ToString("0.0", CultureInfo.InvariantCulture) + " ms");
                continue;
            }

            Rows.Add((scenario, i, actionMs, stableMs, ""));
            log.Add("[perf] " + scenario + " #" + i + " action=" + actionMs.ToString("0.0", CultureInfo.InvariantCulture) +
                    "ms stable=" + (stableMs?.ToString("0.0", CultureInfo.InvariantCulture) ?? "-") + "ms");
        }
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

    private static void CaptureMemory(string point)
    {
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        Memory.Add((point, process.PrivateMemorySize64, process.WorkingSet64, GC.GetTotalMemory(false)));
    }

    private static void WriteOutputs(List<string> log)
    {
        var csv = new StringBuilder("scenario,round,action_ms,stable_ms,note\n");
        foreach (var row in Rows)
        {
            csv.Append(row.Scenario).Append(',').Append(row.Round).Append(',')
                .Append(row.ActionMs.ToString("0.0", CultureInfo.InvariantCulture)).Append(',')
                .Append(row.StableMs?.ToString("0.0", CultureInfo.InvariantCulture) ?? "").Append(',')
                .Append(row.Note).Append('\n');
        }

        var mem = new StringBuilder("point,private_mb,working_set_mb,managed_mb\n");
        foreach (var row in Memory)
        {
            mem.Append(row.Point).Append(',')
                .Append((row.Private / 1024.0 / 1024.0).ToString("0.0", CultureInfo.InvariantCulture)).Append(',')
                .Append((row.WorkingSet / 1024.0 / 1024.0).ToString("0.0", CultureInfo.InvariantCulture)).Append(',')
                .Append((row.Managed / 1024.0 / 1024.0).ToString("0.0", CultureInfo.InvariantCulture)).Append('\n');
        }

        File.WriteAllText(Path.Combine(_baseDir, "measure.csv"), csv.ToString());
        File.WriteAllText(Path.Combine(_baseDir, "memory.csv"), mem.ToString());
        File.WriteAllText(Path.Combine(_baseDir, "log.txt"), string.Join(Environment.NewLine, log));
        File.WriteAllText(Path.Combine(_baseDir, "environment.json"), BuildEnvironmentJson());
        File.WriteAllText(Path.Combine(_baseDir, "summary.txt"), BuildSummary());
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

        return builder.ToString();
    }

    private static string BuildEnvironmentJson()
    {
        using var process = Process.GetCurrentProcess();
        return "{\n" +
               "  \"version\": \"" + Escape(typeof(MainWindow).Assembly.GetName().Version?.ToString() ?? "") + "\",\n" +
               "  \"runtime\": \"" + Escape(System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription) + "\",\n" +
               "  \"aot\": " + (System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported ? "false" : "true") + ",\n" +
               "  \"os\": \"" + Escape(Environment.OSVersion.ToString()) + "\",\n" +
               "  \"cores\": " + Environment.ProcessorCount + ",\n" +
               "  \"machine\": \"" + Escape(Environment.MachineName) + "\",\n" +
               "  \"startup_private_mb\": " + (process.PrivateMemorySize64 / 1024.0 / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + ",\n" +
               "  \"rounds\": " + _rounds + ",\n" +
               "  \"accounts\": " + _accounts + "\n" +
               "}\n";
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
#endif
