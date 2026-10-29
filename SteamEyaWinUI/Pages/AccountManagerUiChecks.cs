#if MANAGER_UI_CHECKS
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using OpenSteamEya.AccountManagement;
using SteamEyaWinUI.Models;
using SteamEyaWinUI.Services;

namespace SteamEyaWinUI.Pages;

public sealed partial class AccountManagerPage
{
    internal async Task PrepareUiCheckAsync(int count)
    {
        var fixture = new ManagerDocument();
        for (int i = 0; i < count; i++) fixture.Accounts.Add(new() { LoginName = $"qa-account-{i:000}",
            Nickname = $"测试账号 {i + 1:000} · Long name", SteamId = $"7656119800000{i:0000}", Password = i % 4 == 0 ? "" : "synthetic-pass-" + i,
            Note = "仅用于自动渲染测试", Order = i, Favorite = i % 5 == 0,
            Background = i % 3 == 0 ? new() { Mode = "local", ImageFile = "missing.png" } : new() });
        if (count == 4)
        {
            fixture.Accounts[0].LoginName += "-missing-avatar";
            fixture.Accounts[1].Nickname = "测试账号 · 非常长的昵称用于检查省略号和按钮不被挤出 · Very long Steam persona name";
            fixture.Accounts[1].LoginName += "-very-long-login-name-for-ellipsis-verification";
            fixture.Accounts[2].Background = new() { Mode = "local", ImageFile = "fixture-background.png" };
        }
        await ManagedAccounts.Store.ReplaceAsync(fixture); selected.Clear();
        selected.Add(fixture.Accounts[0].Id); selected.Add(fixture.Accounts[^1].Id);
        await ReloadAsync();
    }

    // ---- 虚拟化下的取卡片工具：条目只是数据，卡片控件由容器实现时才存在 ----

    /// <summary>条目当前已实现的卡片；容器未实现（或已回收）时为 null。</summary>
    private AccountCard? Realized(ManagedAccountCardItem item) =>
        // 容器由 ItemTemplate 生成：Content 是条目本身，控件树根在这张容器的可视子树里。
        Cards.ContainerFromItem(item) is GridViewItem container ? CardIn(container) : null;

    /// <summary>等到条目真正构建出卡片控件树；必要时先滚到可见位置。</summary>
    private async Task<AccountCard> RealizeCardAsync(ManagedAccountCardItem item, bool scrollIntoView = true)
    {
        if (Realized(item) is { } realized) return realized;
        if (scrollIntoView) Cards.ScrollIntoView(item);
        for (int attempt = 0; attempt < 80; attempt++)
        {
            await Task.Delay(50);
            if (Realized(item) is { } card)
            {
                for (int settle = 0; settle < 20 && (card.ActualWidth <= 250 || card.ActualHeight <= 100); settle++) await Task.Delay(30);
                UpdateLayout();
                return card;
            }
        }
        throw new InvalidOperationException("card container was not realized for item " + item.Account.LoginName);
    }

    private async Task<TextBlock> CooldownLabelAsync(ManagedAccountCardItem item) =>
        Walk(await RealizeCardAsync(item)).OfType<TextBlock>().First(x => x.Name == "AccountCooldownStatus");

    internal void VerifyCapturedCardPixels(FrameworkElement root, int width, int height, byte[] pixels)
    {
        if (Realized(visible[0]) is not { } card)
        {
            ManagerUiChecks.Check(false, "first card realized for pixel verification");
            return;
        }
        var point = card.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point(card.ActualWidth / 2, card.ActualHeight - 8));
        int x = (int)(point.X * width / root.ActualWidth), y = (int)(point.Y * height / root.ActualHeight);
        ManagerUiChecks.Check(x >= 0 && y >= 0 && x < width && y < height, "first card inside screenshot");
        // QA captures replace the Mica backdrop with a flat colour (see ManagerUiChecks.CaptureAsync),
        // so "the card painted" means "this spot is not the bare page background". Comparing against
        // that known colour keeps the check valid across themes and card designs instead of relying on
        // one hard-coded channel order.
        int backdrop = root.ActualTheme == ElementTheme.Light ? 243 : 32;
        bool painted = false;
        for (int dy = -4; dy <= 4 && !painted; dy++)
        {
            for (int dx = -4; dx <= 4 && !painted; dx++)
            {
                int sampleX = Math.Clamp(x + dx, 0, width - 1), sampleY = Math.Clamp(y + dy, 0, height - 1);
                int sample = (sampleY * width + sampleX) * 4;
                painted = Math.Abs(pixels[sample] - backdrop) >= 6 ||
                          Math.Abs(pixels[sample + 1] - backdrop) >= 6 ||
                          Math.Abs(pixels[sample + 2] - backdrop) >= 6;
            }
        }
        ManagerUiChecks.Check(painted, "actual card pixels captured rather than an empty virtualized list");
    }
    internal async Task WaitForVisibleCardAsync()
    {
        if (visible.Count == 0) return;
        Cards.ScrollIntoView(visible[0]);
        for (int i = 0; i < 60; i++)
        {
            UpdateLayout();
            var container = Cards.ContainerFromItem(visible[0]) as GridViewItem;
            var card = container?.ContentTemplateRoot as AccountCard;
            if (container is { ActualHeight: > 100 } && card is not null &&
                card.IsLoaded && card.ActualWidth > 250 && card.ActualHeight > 100)
            { await Task.Delay(180); return; }
            await Task.Delay(50);
        }
        ManagerUiChecks.Check(false, "first card realized after theme/resize");
    }
    internal async Task CheckCardStatesAsync(Func<string, Task> capture)
    {
        await WaitForVisibleCardAsync();
        var item = visible[0];
        var card = await RealizeCardAsync(item, false);
        var container = (GridViewItem)Cards.ContainerFromItem(item);
        var presenter = Walk(container).OfType<ListViewItemPresenter>().First();
        ManagerUiChecks.Check(container.CornerRadius.TopLeft == AccountCard.CardRadius && presenter.CornerRadius.TopLeft == AccountCard.CardRadius && card.OutlineRadius.TopLeft == AccountCard.CardRadius, "native presenter and card share 14px radius");
        ManagerUiChecks.Check(!presenter.SelectionCheckMarkVisualEnabled && new[] { presenter.PointerOverBackground, presenter.SelectedBackground, presenter.SelectedPointerOverBackground, presenter.PointerOverBorderBrush, presenter.SelectedBorderBrush, presenter.SelectedPointerOverBorderBrush }.All(x => x is SolidColorBrush { Color.A: 0 }), "native hover/selection layers do not paint rectangular overlays");
        Cards.SelectedItems.Remove(item); card.SetHovered(false);
        ManagerUiChecks.Check(card.EmphasisOpacity == 0, "normal card has no emphasis"); await capture("state-normal");
        card.SetHovered(true); await Task.Delay(180); ManagerUiChecks.Check(Math.Abs(card.EmphasisOpacity - .6) < .001, "hover uses rounded soft outline"); await capture("state-hover");
        Cards.SelectedItems.Add(item); card.SetHovered(false);
        ManagerUiChecks.Check(card.IsSelectedCard && card.EmphasisOpacity == 1, "selected outline persists without hover"); await capture("state-selected");
        card.SetHovered(true); ManagerUiChecks.Check(card.EmphasisOpacity == 1, "hover and selection compose"); await capture("state-selected-hover");
        card.SetHovered(false); ManagerUiChecks.Check(container.Focus(FocusState.Keyboard) && container.UseSystemFocusVisuals, "keyboard focus remains enabled"); await capture("state-keyboard-focus");
        ManagerUiChecks.Check(!card.Actions["login"].IsEnabled, "unmapped synthetic account cannot trigger Steam login");
        // 右键菜单走真实路径：从容器解析卡片 → 建菜单 → 弹出（模板化后卡片本身不再持有 ContextFlyout）。
        var menu = OpenCardMenuForCheck(container);
        ManagerUiChecks.Check(menu is not null, "right-click resolves the card from its item container");
        await Task.Delay(150);
        var items = menu!.Items.OfType<MenuFlyoutItem>().Select(x => x.Text).ToList();
        ManagerUiChecks.Check(items.Count == 14 && items.Contains("设置账号背景") && items.Contains("设置账号分组") && items.Contains("删除账号") && items.Contains("转到黑号管理"), "right-click menu retains original actions and adds classification move");
        ManagerUiChecks.Check(VisualTreeHelper.GetOpenPopupsForXamlRoot(XamlRoot).Count > 0 && !card.Actions.ContainsKey("more"), "right-click menu opens and footer ellipsis is removed");
        menu.Hide(); await Task.Delay(80);
        await CheckNativeDialogAsync(() => Invoke(card.Actions["details"]), "账号详情");
        menu.ShowAt(card); await Task.Delay(150);
        var backgroundItem = menu.Items.OfType<MenuFlyoutItem>().First(x => x.Text == "设置账号背景");
        await CheckNativeDialogAsync(() => Invoke(backgroundItem), "卡片背景");
        menu.ShowAt(card); await Task.Delay(150);
        var favoriteItem = menu.Items.OfType<MenuFlyoutItem>().First(x => x.Text.Contains("收藏"));
        bool wasFavorite = card.Account.Favorite; Invoke(favoriteItem);
        for (int attempt = 0; document.Accounts.First(x => x.Id == card.Account.Id).Favorite == wasFavorite && attempt < 40; attempt++) await Task.Delay(50);
        ManagerUiChecks.Check(document.Accounts.First(x => x.Id == card.Account.Id).Favorite != wasFavorite, "native favorite menu persists to synthetic store");
        await WaitForVisibleCardAsync();
        card = await RealizeCardAsync(visible[0], false);
        var title = Walk(card).OfType<TextBlock>().First(x => x.Text.StartsWith("测试账号"));
        ManagerUiChecks.Check(title.Text.Contains("（仅用于自动渲染测试）"), "card nickname appends note in parentheses");
        ManagerUiChecks.Check(card.IsDoubleTapEnabled && card.IsCardDoubleTapSource(title) && !card.IsCardDoubleTapSource(card.Actions["login"]) && !card.IsCardDoubleTapSource(card.Actions["copy-account"]), "double-tap edits card surface but excludes action buttons");
        await CheckNativeDialogAsync(() => _ = card.OpenEditorAsync(), "账号信息");
        var withPasswordItem = visible.First(x => x.Account.Password.Length > 0);
        var withPassword = await RealizeCardAsync(withPasswordItem);
        Invoke(withPassword.Actions["reveal"]); await Task.Delay(80);
        ManagerUiChecks.Check(withPassword.VisiblePassword == withPassword.Account.Password, "native reveal button invokes original password behavior");
        Invoke(withPassword.Actions["reveal"]); await Task.Delay(80);
        ManagerUiChecks.Check(withPassword.VisiblePassword == "••••••••", "native reveal button hides password");
        ManagerUiChecks.Check(!card.Actions["copy-password"].IsEnabled && card.VisiblePassword == "未保存密码", "missing password disables copying and displays correct text");
        ManagerUiChecks.Info($"card geometry DIP: width={card.ActualWidth:F2}; height={card.ActualHeight:F2}; artW={card.HeaderArtwork.ActualWidth:F2}; artH={card.HeaderArtwork.ActualHeight:F2}; corner={card.OutlineRadius.TopLeft}");
        ManagerUiChecks.Check(card.HeaderArtwork.ActualHeight >= card.ActualHeight - 4 && card.HeaderArtwork.ActualWidth >= card.ActualWidth - 4, "artwork fills the whole account card");
        ManagerUiChecks.Check(Walk(await RealizeCardAsync(visible[1], false)).OfType<TextBlock>().Where(x => x.Text.Contains("非常长") || x.Text.Contains("very-long-login")).All(x => x.TextTrimming == TextTrimming.CharacterEllipsis), "long nickname/login use ellipsis");
        ManagerUiChecks.Check((await RealizeCardAsync(visible[0], false)).HeaderArtwork.Background is LinearGradientBrush && (await RealizeCardAsync(visible[2], false)).HeaderArtwork.Background is ImageBrush, "missing background falls back while valid local background loads");

    }
    internal async Task CheckNewSelectionActionsAsync()
    {
        var saved = selected.ToHashSet();
        ManagerUiChecks.Check(SelectionActions.Visibility == Visibility.Visible && ((StackPanel)Privacy.Parent).Children.IndexOf(SelectionActions) == ((StackPanel)Privacy.Parent).Children.IndexOf(Privacy) + 1, "selection toolbar appears immediately after privacy mode");
        Invoke(SelectAll); await Task.Delay(50);
        ManagerUiChecks.Check(selected.Count == visible.Count && Cards.SelectedItems.Count == visible.Count, "native select-all button selects current visible accounts");
        Invoke(InvertSelection); await Task.Delay(50);
        ManagerUiChecks.Check(selected.Count == 0 && SelectionActions.Visibility == Visibility.Collapsed, "native inverse button clears an all-selected collection");
        Cards.SelectedItems.Add(visible[0]); var hiddenId = visible[0].Account.Id;
        Search.Text = "qa-account-010";
        for (int attempt = 0; visible.Count != 1 && attempt < 60; attempt++) await Task.Delay(50);
        Invoke(SelectAll); await Task.Delay(50);
        ManagerUiChecks.Check(visible.Count == 1 && selected.Count == 2 && selected.Contains(hiddenId), $"filtered select-all preserves hidden selections (visible={visible.Count}, selected={selected.Count}, hidden={selected.Contains(hiddenId)})");
        Invoke(InvertSelection); await Task.Delay(50);
        ManagerUiChecks.Check(selected.Count == 1 && selected.Contains(hiddenId) && Cards.SelectedItems.Count == 0, "filtered inverse affects visible records only");
        Invoke(ClearSelection); await Task.Delay(50);
        ManagerUiChecks.Check(selected.Count == 0 && Cards.SelectedItems.Count == 0 && SelectionActions.Visibility == Visibility.Collapsed, "native clear-selection removes hidden and visible selections");
        Search.Text = "";
        for (int attempt = 0; visible.Count != document.Accounts.Count && attempt < 60; attempt++) await Task.Delay(50);
        selected.UnionWith(saved); Render();
        Invoke(QuerySelection);
        for (int attempt = 0; !document.Groups.Any(x => x.Name == "未查询") && attempt < 60; attempt++) await Task.Delay(50);
        ManagerUiChecks.Check(!AppState.IsBusy && selected.SetEquals(saved), "unmapped selection query skips without launching Steam or changing selection");
        var unqueried = document.Groups.First(x => x.Name == "未查询").Id;
        ManagerUiChecks.Check(document.Accounts.Where(x => saved.Contains(x.Id)).All(x => AccountGroups.Ids(x).Contains(unqueried)), "native skipped-query action assigns unqueried membership");
        AppState.SetBusy(true); await Task.Delay(80);
        try { ManagerUiChecks.Check(!QuerySelection.IsEnabled, "selected query button respects original global busy state"); }
        finally { AppState.SetBusy(false); await Task.Delay(80); }
    }
    internal async Task CheckNativeBatchAddAsync()
    {
        int before = document.Accounts.Count;
        AddAccountButton.Flyout.ShowAt(AddAccountButton); await Task.Delay(100); Invoke(BatchAddMenuItem);
        var elements = await FindDialogElementsAsync("批量添加账号");
        var input = elements.OfType<TextBox>().First(x => x.Header?.ToString()?.StartsWith("每行一个账号") == true);
        var primary = elements.OfType<Button>().First(x => x.Content?.ToString() == "添加账号");
        ManagerUiChecks.Check(input.AcceptsReturn && !primary.IsEnabled, "batch-add opens multiline input with empty submission disabled");
        input.Text = "synthetic-invalid-input"; await Task.Delay(150); Invoke(primary); await Task.Delay(200);
        ManagerUiChecks.Check(document.Accounts.Count == before && VisualTreeHelper.GetOpenPopupsForXamlRoot(XamlRoot).Count > 0 && elements.OfType<TextBlock>().Any(x => x.Text.StartsWith("第 1 行：缺少")), "batch-add invalid line keeps dialog open and writes no records");
        const string expected = " space\"\\word----tail ";
        input.Text = "qa-batch-one----" + expected + "\r\nqa-batch-two----\nQA-BATCH-ONE----duplicate";
        await Task.Delay(150);
        Invoke(primary);
        for (int attempt = 0; document.Accounts.Count != before + 2 && attempt < 60; attempt++) await Task.Delay(50);
        ManagerUiChecks.Check(document.Accounts.Count == before + 2 && document.Accounts.Single(x => x.LoginName == "qa-batch-one").Password == expected, "native batch-add submits literal passwords and skips duplicate login");
        ManagerUiChecks.Check(document.Accounts.Single(x => x.LoginName == "qa-batch-two").Password == "", "native batch-add saves empty password as an unsaved record");
        await Task.Delay(150);
        ManagerUiChecks.Check(!VisualTreeHelper.GetOpenPopupsForXamlRoot(XamlRoot).Any(x => Walk(x.Child).OfType<TextBlock>().Any(t => t.Text == "批量添加账号")), "native batch-add closes on successful commit");
    }
    private async Task<List<DependencyObject>> FindDialogElementsAsync(string title)
    {
        for (int attempt = 0; attempt < 60; attempt++)
        {
            await Task.Delay(50);
            foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(XamlRoot))
            {
                var elements = Walk(popup.Child).ToList();
                if (elements.OfType<TextBlock>().Any(x => x.Text == title)) return elements;
            }
        }
        throw new InvalidOperationException("Native dialog did not open: " + title);
    }
    private async Task CheckNativeDialogAsync(Action invoke, string title)
    {
        invoke();
        for (int attempt = 0; attempt < 40; attempt++)
        {
            await Task.Delay(50);
            foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(XamlRoot))
            {
                var elements = Walk(popup.Child).ToList();
                if (!elements.OfType<TextBlock>().Any(x => x.Text == title)) continue;
                var close = elements.OfType<Button>().First(x => x.Content?.ToString() == "取消"); Invoke(close);
                await Task.Delay(150); ManagerUiChecks.Check(true, "native " + title + " dialog opens and closes"); return;
            }
        }
        ManagerUiChecks.Check(false, "native " + title + " dialog opens");
    }
    private static void Invoke(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    private static void Invoke(MenuFlyoutItem item) => ((IInvokeProvider)new MenuFlyoutItemAutomationPeer(item).GetPattern(PatternInterface.Invoke)).Invoke();
    internal async Task CheckCardClipboardAsync()
    {
        var card = await RealizeCardAsync(visible.First(x => x.Account.Password.Length > 0));
        foreach (var (action, expected) in new[] { ("copy-account", card.Account.LoginName), ("copy-password", card.Account.Password), ("copy-id", card.Account.SteamId) })
        {
            await ManagerUiChecks.CheckClipboardTextAsync(() => Invoke(card.Actions[action]), expected, "native button " + action);
        }
    }
    internal async Task SwitchUiCheckAsync(string mode, bool privateMode)
    {
        await ManagedAccounts.Store.UpdateAsync(doc => { doc.Preferences.View = mode; doc.Preferences.Privacy = privateMode; });
        privacy = privateMode; Privacy.IsChecked = privateMode;
        Search.Visibility = GroupFilter.Visibility = privateMode ? Visibility.Collapsed : Visibility.Visible;
        await ReloadAsync();
    }
    internal async Task VerifyUiCheckAsync(string mode, bool privateMode)
    {
        ManagerUiChecks.Check(visible.Count == document.Accounts.Count, mode + " record count");
        ManagerUiChecks.Check(selected.Count == 2 && Cards.SelectedItems.Count == 2, mode + " selection preserved");
        ManagerUiChecks.Check(visible.All(x => x.CardWidth >= 260), mode + " responsive card widths");
        foreach (var item in visible.Take(3))
        {
            var texts = Walk(await RealizeCardAsync(item, false)).OfType<TextBlock>().Where(x => x.Visibility == Visibility.Visible).Select(x => x.Text).ToList();
            ManagerUiChecks.Check(texts.Any(x => x.Contains(privateMode ? "隐私账号" : "测试账号")), mode + " title displayed");
            ManagerUiChecks.Check(texts.All(x => !x.Contains("synthetic-pass-")), mode + " password masked");
            if (privateMode) ManagerUiChecks.Check(texts.All(x => !x.Contains("qa-account-") && !x.Contains("测试账号") && !x.Contains("仅用于自动渲染测试")), "privacy identifiers hidden");
        }
    }
    internal async Task CheckFilterSelectionAsync()
    {
        var saved = selected.ToHashSet(); Search.Text = "qa-account-000";
        for (int attempt = 0; visible.Count != 1 && attempt < 40; attempt++) await Task.Delay(50);
        ManagerUiChecks.Check(visible.Count == 1 && selected.SetEquals(saved), "search preserves offscreen selection");
        Search.Text = "";
        for (int attempt = 0; visible.Count != document.Accounts.Count && attempt < 40; attempt++) await Task.Delay(50);
        ManagerUiChecks.Check(visible.Count == 100 && selected.SetEquals(saved), "clear search restores collection and selection");
        var card = await RealizeCardAsync(visible.First(x => x.Account.Password.Length > 0));
        card.TogglePassword(); ManagerUiChecks.Check(card.VisiblePassword == card.Account.Password, "show saved password");
        card.TogglePassword(); ManagerUiChecks.Check(card.VisiblePassword == "••••••••", "hide saved password");
        var firstId = visible[0].Account.Id; BeginReorder(); visible.Move(0, 3); FinishReorder(); await PersistVisibleOrderAsync();
        ManagerUiChecks.Check(visible[3].Account.Id == firstId && (await ManagedAccounts.Store.LoadAsync()).Accounts.First(x => x.Id == firstId).Order == 3, "custom reorder persists");
        ManagerUiChecks.Check(selected.SetEquals(saved), "reorder preserves selected identities");
        await SwitchUiCheckAsync("cards", true);
        card = await RealizeCardAsync(visible.First(x => x.Account.Password.Length > 0));
        card.TogglePassword();
        ManagerUiChecks.Check(!card.VisiblePassword.Contains("synthetic-pass-"), "privacy prevents password reveal");
        await SwitchUiCheckAsync("cards", false);
        var group = new ManagedGroup { Name = "合成分组" };
        await ManagedAccounts.Store.UpdateAsync(doc =>
        { doc.Groups.Add(group); for (int i = 0; i < 50; i++) doc.Accounts[i].GroupId = group.Id; });
        await ReloadAsync(); SelectTag(GroupFilter, group.Id.ToString()); await Task.Delay(100);
        ManagerUiChecks.Check(visible.Count == 50, "group filter");
        Search.Text = "qa-account-010";
        for (int attempt = 0; visible.Count != 1 && attempt < 40; attempt++) await Task.Delay(50);
        ManagerUiChecks.Check(visible.Count == 1 && selected.Count == 2,
            $"combined search and group preserve selection (visible={visible.Count}, selected={selected.Count}, syncing={syncing}, query={Search.Text})");
        Search.Text = ""; SelectTag(GroupFilter, "all");
        for (int attempt = 0; visible.Count != 100 && attempt < 40; attempt++) await Task.Delay(50);
        ManagerUiChecks.Check(visible.Count == 100, "clear combined filter");
        var swReload = System.Diagnostics.Stopwatch.StartNew();
        await ReloadAsync();
        swReload.Stop();
        ManagerUiChecks.Info($"perf reload({visible.Count} cards) = {swReload.ElapsedMilliseconds} ms");
    }
    private static IEnumerable<DependencyObject> Walk(DependencyObject root)
    {
        yield return root;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Walk(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
}
#endif
