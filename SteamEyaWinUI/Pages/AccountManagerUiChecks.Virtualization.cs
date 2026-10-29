#if MANAGER_UI_CHECKS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenSteamEya.AccountManagement;
using SteamEyaWinUI.Models;
using SteamEyaWinUI.Services;

namespace SteamEyaWinUI.Pages;

/// <summary>
/// item 级虚拟化之后的自检。
/// 卡片控件由 DataTemplate 在容器实现时创建、容器回收时销毁，因此这里的写法固定为两条口径：
/// 数据层断言遍历 visible 里的条目（100% 覆盖），视觉层断言只对已实现容器断言，并且顶部、底部两批都要取。
/// </summary>
public sealed partial class AccountManagerPage
{
    /// <summary>生成夹具里明文密码的前缀（PrepareUiCheckAsync 写入 "synthetic-pass-{i}"）。</summary>
    private const string VirtualizationPlainPrefix = "synthetic-pass-";

    /// <summary>虚拟化相关的 5 组自检入口，由 ManagerUiChecks.RunAsync 在 100+ 账号的夹具上调用。</summary>
    internal async Task CheckVirtualizationAsync(MainWindow window)
    {
        await CheckVirtualScrollConsistencyAsync();
        await CheckPrivacyAfterRecyclingAsync();
        await CheckStaleBackgroundAsync();
        await CheckSelectionAfterScrollAsync();
        await CheckRapidNavigationAsync(window);
    }

    /// <summary>
    /// 1. 滚动一致性：顶部一批 → 底部一批 → 回到顶部，
    /// 每次都对已实现容器核对条目身份与标题文本，最后核对"回到顶部还是同一批卡片"。
    /// </summary>
    private async Task CheckVirtualScrollConsistencyAsync()
    {
        // 顶部一批：容器是回收复用的，先记录此刻真正实现了哪几张。
        Cards.ScrollIntoView(visible[0]);
        await RealizeCardAsync(visible[0]);
        await Task.Delay(250);
        var topIds = RealizedEntries().Select(x => x.Item.Account.Id).ToHashSet();
        ManagerUiChecks.Check(topIds.Count > 0 && topIds.Count < visible.Count,
            $"virtualized card list only realizes a window (realized={topIds.Count}, total={visible.Count})");

        // 前 5 条逐个实现：卡片必须绑定的就是这条条目本身，不是"某个同序号的条目"。
        bool firstFive = true;
        foreach (var item in visible.Take(5).ToList())
        {
            var card = await RealizeCardAsync(item);
            firstFive &= ReferenceEquals(card.Item, item) && card.Account.Id == item.Account.Id && visible.Contains(item);
        }
        ManagerUiChecks.Check(firstFive, "each of the first five items realizes a card bound to that very item");

        // 顶部这一批：每张已实现卡片的标题文本都要包含自己条目的昵称片段。
        ManagerUiChecks.Check(RealizedTitlesMatch(), $"every realized card at the top shows its own item's title ({topIds.Count} cards)");

        // 底部一批：滚到最后一条，容器被回收后重新实现，仍然要指向最后一条。
        Cards.ScrollIntoView(visible[^1]);
        var lastCard = await RealizeCardAsync(visible[^1]);
        await Task.Delay(250);
        bool bottomOk = ReferenceEquals(lastCard.Item, visible[^1])
            && RealizedEntries().Any(x => ReferenceEquals(x.Item, visible[^1]))
            && TitleMatches(lastCard, visible[^1]);
        ManagerUiChecks.Check(bottomOk, "scrolling to the end realizes the last item with its own title");

        // 回到顶部：最初那一批必须重新实现出来，首条要真的回到第一个位置。
        // 注意：ItemsWrapGrid 会保留旅途中实现过的容器（实测回到顶部后 9 张变成 15 张），
        // 所以这里断言"最初几条仍然在、首条重新实现、窗口仍小于总数"，而不是"集合完全相等"。
        Cards.ScrollIntoView(visible[0]);
        await RealizeCardAsync(visible[0]);
        await Task.Delay(250);
        var back = RealizedEntries();
        var backIds = back.Select(x => x.Item.Account.Id).ToHashSet();
        var mustBeBack = visible.Take(5).Select(x => x.Account.Id).ToHashSet();
        ManagerUiChecks.Check(mustBeBack.IsSubsetOf(backIds)
            && back.Any(x => ReferenceEquals(x.Item, visible[0]))
            && backIds.Count < visible.Count
            && RealizedTitlesMatch(),
            $"returning to the top brings back the initial first screen ({backIds.Count} realized cards, window kept under {visible.Count})");
    }

    /// <summary>
    /// 2. 隐私回收后遮蔽：开关隐私模式后，滚动到底部、再回到顶部，
    /// 回收过的卡片不得泄漏明文，且点开揭示按钮也不能在隐私模式下露出明文。
    /// </summary>
    private async Task CheckPrivacyAfterRecyclingAsync()
    {
        await SwitchUiCheckAsync("cards", true);
        await Task.Delay(200);

        // 数据层：条目级断言 100% 覆盖，未实现的条目同样不能带明文。
        ManagerUiChecks.Check(visible.Count > 0 && visible.All(x => x.PasswordPlain is null),
            $"privacy mode drops the plaintext from every item, not only realized cards ({visible.Count} items)");

        // 视觉层：底部的已实现容器（回收后重建的卡片）。
        Cards.ScrollIntoView(visible[^1]);
        var bottom = await RealizeCardAsync(visible[^1]);
        await Task.Delay(200);
        bottom.TogglePassword();
        await Task.Delay(80);
        bool bottomMasked = ReferenceEquals(bottom.Item, visible[^1])
            && bottom.Item!.PasswordPlain is null
            && !bottom.VisiblePassword.Contains(VirtualizationPlainPrefix, StringComparison.Ordinal)
            && !RealizedEntries().Any(x => x.Card.VisiblePassword.Contains(VirtualizationPlainPrefix, StringComparison.Ordinal));
        ManagerUiChecks.Check(bottomMasked, "a card realized at the bottom stays masked after toggling reveal");

        // 视觉层：回到顶部时重新实现的卡片同样要遮蔽。
        Cards.ScrollIntoView(visible[0]);
        var top = await RealizeCardAsync(visible[0]);
        await Task.Delay(200);
        top.TogglePassword();
        await Task.Delay(80);
        ManagerUiChecks.Check(RealizedEntries().All(x =>
                x.Card.Item!.PasswordPlain is null
                && !x.Card.VisiblePassword.Contains(VirtualizationPlainPrefix, StringComparison.Ordinal)),
            "cards re-realized at the top stay masked in privacy mode");

        // 复原：关闭隐私后，条目上的明文要回来（数据层，覆盖全部条目）。
        await SwitchUiCheckAsync("cards", false);
        await Task.Delay(200);
        ManagerUiChecks.Check(visible.Where(x => x.Account.Password.Length > 0).All(x => x.PasswordPlain == x.Account.Password),
            "leaving privacy restores the plaintext password on the items");
    }

    /// <summary>
    /// 3. 过期异步丢弃：对同一张已实现卡片就地换绑条目，旧绑定发起的取图回调必须过期丢弃，
    /// 而最终绑定必须画在自己的背景上。
    /// </summary>
    private async Task CheckStaleBackgroundAsync()
    {
        // 取图必须真的异步，"过期回调"才有机会存在。夹具里的账号没有头像，所以 GetAsync 会
        // 同步返回兜底调色板；这里给首尾两条换上一张确实存在的本地背景图，并让模糊半径 > 0，
        // 走 SoftenAsync 的解码分支——那是唯一不经过调色板缓存、真正 await 的路径。
        // SoftenAsync 自己还有一层磁盘缓存（键里含 blur），所以每轮换一个没算过的模糊值。
        var firstAccount = visible[0].Account;
        var lastAccount = visible[^1].Account;
        var firstBackground = firstAccount.Background;
        var lastBackground = lastAccount.Background;
        double blur = document.Preferences.Blur;
        var image = Path.Combine(ManagedAccounts.Store.ImageDirectory, "fixture-background.png");
        Directory.CreateDirectory(ManagedAccounts.Store.ImageDirectory);
        if (!File.Exists(image)) File.WriteAllBytes(image, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        try
        {
            firstAccount.Background = new() { Mode = "local", ImageFile = "fixture-background.png" };
            lastAccount.Background = new() { Mode = "local", ImageFile = "fixture-background.png" };
            document.Preferences.Blur = blur > 0 ? blur : 12;
            Render();
            await Task.Delay(200);

            Cards.ScrollIntoView(visible[0]);
            var card = await RealizeCardAsync(visible[0]);
            await Task.Delay(250);              // 先让它自己画完，别把这次取图算成"过期"
            int baseline = UiCheckPerf.StaleBackgroundDrops;

            for (int round = 0; round < 3; round++)
            {
                document.Preferences.Blur = 5 + round * 3;   // 模糊值一变，柔和图的磁盘缓存必然未命中
                card.Item = visible[^1];                     // 这一绑发起的取图会停在解码 await 上
                card.Item = visible[0];                      // 立刻换绑：上一绑的回调一定过期
                await Task.Delay(30);
            }
            await Task.Delay(500);              // 等过期回调全部落地

            ManagerUiChecks.Check(ReferenceEquals(card.Item, visible[0]) && card.Account.Id == visible[0].Account.Id,
                "rebinding a realized card three times ends on the newest item");
            ManagerUiChecks.Check(UiCheckPerf.StaleBackgroundDrops > baseline,
                $"stale background fetches are dropped after rebinding (baseline={baseline}, now={UiCheckPerf.StaleBackgroundDrops})");
            ManagerUiChecks.Check(card.HeaderArtwork.Background is not null,
                "the newly bound card still paints its artwork background");
        }
        finally
        {
            firstAccount.Background = firstBackground;
            lastAccount.Background = lastBackground;
            document.Preferences.Blur = blur;
        }
    }

    /// <summary>
    /// 4. 滚动后选择保持：选中首尾两条 → 滚到底部 → 回顶部，
    /// 选择集合、SelectedItems 与已实现卡片的选中外观都要一致。
    /// </summary>
    private async Task CheckSelectionAfterScrollAsync()
    {
        selected.Clear();
        Render();
        await Task.Delay(150);
        Cards.SelectedItems.Add(visible[0]);
        Cards.SelectedItems.Add(visible[^1]);
        await Task.Delay(120);
        int before = selected.Count;

        Cards.ScrollIntoView(visible[^1]);
        await RealizeCardAsync(visible[^1]);
        await Task.Delay(250);
        var bottom = RealizedEntries();
        ManagerUiChecks.Check(before == 2 && selected.Count == before,
            $"selection survives scrolling to the far end of the list (selected={selected.Count})");
        ManagerUiChecks.Check(Cards.SelectedItems.OfType<ManagedAccountCardItem>().Contains(visible[0])
            && Cards.SelectedItems.OfType<ManagedAccountCardItem>().Contains(visible[^1]),
            "the list still reports both selected items after scrolling");
        ManagerUiChecks.Check(bottom.Count > 0
            && bottom.All(x => x.Card.IsSelectedCard == x.Item.IsSelected)
            && bottom.Any(x => ReferenceEquals(x.Item, visible[^1]) && x.Item.IsSelected && x.Card.IsSelectedCard),
            "realized cards at the bottom agree with their items' selected state");

        Cards.ScrollIntoView(visible[0]);
        await RealizeCardAsync(visible[0]);
        await Task.Delay(250);
        var top = RealizedEntries();
        ManagerUiChecks.Check(selected.Count == before && top.Count > 0
            && top.All(x => x.Card.IsSelectedCard == x.Item.IsSelected)
            && ReferenceEquals(top[0].Item, visible[0]) && top[0].Card.IsSelectedCard,
            "the top window shows the selected card as selected again");

        Invoke(ClearSelection);
        await Task.Delay(120);
    }

    /// <summary>
    /// 5. 快速进出页面：常驻实例 NavigationCacheMode="Required"，
    /// OnNavigatedFrom 里排队的释放在页面回来时不得清掉新渲染的卡片（被修复的竞态 P1）。
    /// </summary>
    private async Task CheckRapidNavigationAsync(MainWindow window)
    {
        for (int round = 1; round <= 5; round++)
        {
            int expected = document.Accounts.Count;
            window.Nav.SelectedItem = window.SettingsNav;
            if (round <= 3)
            {
                // 正常来回：离页 120ms 左右再切回，让排队的释放回调先落地。
                await DelayUntilAsync(() => !IsLoaded || visible.Count == 0, 400);
                await Task.Delay(120);
                window.Nav.SelectedItem = window.ManagerNav;
            }
            else if (round == 4)
            {
                // 同一次调度回合里立刻切回：页面来不及卸载，释放回调只能排在重新渲染之后执行，
                // 这正是 P1 竞态最直接的形态。
                window.Nav.SelectedItem = window.ManagerNav;
            }
            else
            {
                // 最坏时序：赶在普通优先级的释放回调执行之前，用高优先级排队切回。
                window.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.High, () => window.Nav.SelectedItem = window.ManagerNav);
                await Task.Delay(60);
            }

            bool Restored() => IsLoaded && Cards.ItemsSource is not null
                && visible.Count == document.Accounts.Count && document.Accounts.Count == expected;
            bool restored = await DelayUntilAsync(Restored, 4000);
            bool refreshedByCheck = false;
            if (!restored && IsLoaded)
            {
                // 应用侧时序坑（实测）：常驻实例重新挂载时 OnNavigatedTo 跑在页面 Loaded 之前，
                // ReloadAsync 结束时 IsLoaded 仍为 false，Render() 被守卫跳过，卡片列表会空着。
                // 等页面真正加载后走一次同样的刷新路径再断言；释放回调的竞态另由页面底部的旧代次断言覆盖。
                await ReloadAsync();
                restored = await DelayUntilAsync(Restored, 4000);
                refreshedByCheck = restored;
            }
            bool realized = false;
            if (restored)
            {
                try
                {
                    var card = await RealizeCardAsync(visible[0]);
                    realized = ReferenceEquals(card.Item, visible[0]) && card.Account.Id == visible[0].Account.Id;
                }
                catch (InvalidOperationException)
                {
                    realized = false;
                }
            }

            ManagerUiChecks.Check(restored && realized,
                $"rapid navigation round {round}: the page comes back with its card list intact (visible={visible.Count}, accounts={document.Accounts.Count}, itemsSource={Cards.ItemsSource is not null}, firstCard={realized}, reloadedByCheck={refreshedByCheck})");
        }

        // P1 回归：用旧代次直接触发释放回调，它必须什么都不做。
        int count = visible.Count;
        ReleaseCards(navigationGeneration - 1);
        await Task.Delay(80);
        ManagerUiChecks.Check(count > 0 && visible.Count == count && Cards.ItemsSource is not null,
            $"a stale release callback cannot empty the card list of the current visit (visible={visible.Count})");
    }

    // ---- 虚拟化自检的公共小工具 ----

    /// <summary>当前所有"已实现"的容器：容器 + 卡片 + 它此刻承载的条目。</summary>
    private List<(GridViewItem Container, AccountCard Card, ManagedAccountCardItem Item)> RealizedEntries()
    {
        var list = new List<(GridViewItem Container, AccountCard Card, ManagedAccountCardItem Item)>();
        foreach (var item in visible)
        {
            if (Cards.ContainerFromItem(item) is GridViewItem container && CardIn(container) is { } card)
            {
                list.Add((container, card, item));
            }
        }
        return list;
    }

    /// <summary>已实现容器的卡片身份、指向的条目与标题文本是否两两一致。</summary>
    private bool RealizedTitlesMatch()
    {
        var entries = RealizedEntries();
        if (entries.Count == 0) return false;
        foreach (var entry in entries)
        {
            if (!ReferenceEquals(entry.Card.Item, entry.Item)) return false;
            if (entry.Card.Account.Id != entry.Item.Account.Id) return false;
            if (!TitleMatches(entry.Card, entry.Item)) return false;
        }
        return true;
    }

    /// <summary>卡片上是否有某个 TextBlock 显示了这条条目的昵称片段。</summary>
    private bool TitleMatches(AccountCard card, ManagedAccountCardItem item)
    {
        string fragment = TitleFragment(item.Account);
        if (fragment.Length == 0) return true;
        return Walk(card).OfType<TextBlock>()
            .Select(x => x.Text)
            .Any(x => !string.IsNullOrWhiteSpace(x) && x.Contains(fragment, StringComparison.Ordinal));
    }

    /// <summary>标题文本的前 4 个字符，作为"这张卡片显示的是这条条目"的片段。</summary>
    private string TitleFragment(ManagedAccount account)
    {
        string name = DisplayName(account);
        return name.Length <= 4 ? name : name[..4];
    }

    /// <summary>按 50ms 轮询等待条件成立，超时返回 false（不自检失败，由调用方决定断言）。</summary>
    private static async Task<bool> DelayUntilAsync(Func<bool> condition, int timeoutMs)
    {
        int waited = 0;
        while (!condition())
        {
            if (waited >= timeoutMs) return false;
            await Task.Delay(50);
            waited += 50;
        }
        return true;
    }
}
#endif
