using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using OpenSteamEya.AccountManagement;
using SteamEyaWinUI.Services;

namespace SteamEyaWinUI.Pages;

public sealed partial class AccountManagerPage
{
#if MANAGER_UI_CHECKS
    /// <summary>自检用：最近一次由右键路径创建的卡片菜单（正常构建下不持有该引用）。</summary>
    internal MenuFlyout? LastCardMenu { get; private set; }
#endif

    private void UpdateSelectionToolbar()
    {
        SelectionActions.Visibility = selected.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        QuerySelection.IsEnabled = selected.Count > 0 && !AppState.IsBusy;
    }

    private void ChangeVisibleSelection(bool invert)
    {
        syncing = true;
        try
        {
            foreach (var item in visible)
            {
                if (invert && selected.Contains(item.Account.Id)) selected.Remove(item.Account.Id);
                else selected.Add(item.Account.Id);
            }
            Cards.SelectedItems.Clear();
            foreach (var item in visible)
            {
                bool isSelected = selected.Contains(item.Account.Id);
                item.IsSelected = isSelected;
                if (isSelected) Cards.SelectedItems.Add(item);
            }
            foreach (var card in RealizedCards()) card.SetSelected(card.Item?.IsSelected == true);
            Summary.Text = $"{document.Accounts.Count} 个账号 · 当前 {visible.Count} 个 · 已选 {selected.Count} 个";
            UpdateSelectionToolbar();
        }
        finally { syncing = false; }
    }
    private void SelectAll_Click(object sender, RoutedEventArgs e) => ChangeVisibleSelection(false);
    private void InvertSelection_Click(object sender, RoutedEventArgs e) => ChangeVisibleSelection(true);
    private void ClearSelection_Click(object sender, RoutedEventArgs e)
    {
        selected.Clear(); Cards.SelectedItems.Clear();
        foreach (var item in visible) item.IsSelected = false;
        foreach (var card in RealizedCards()) card.SetSelected(false);
        Summary.Text = $"{document.Accounts.Count} 个账号 · 当前 {visible.Count} 个 · 已选 0 个";
        UpdateSelectionToolbar();
    }
    private async void QuerySelection_Click(object sender, RoutedEventArgs e) => await SafeAsync(QuerySelectedAsync);
    private async Task QuerySelectedAsync()
    {
        if (AppState.IsBusy || selected.Count == 0) return;
        // Snapshot identities, including selections hidden by filters. Never feed managed passwords to Steam.
        var accounts = document.Accounts.Where(x => selected.Contains(x.Id)).ToList();
        var keys = accounts.Select(x => resolved.GetValueOrDefault(x.Id))
            .Select(key => ManagedAccounts.History(key) is not null ? key : null).ToList();
        if (!keys.Any(x => x is not null))
        {
            await ManagedAccounts.Store.UpdateAsync(doc => AccountQueryGrouping.Apply(doc,
                accounts.ToDictionary(x => x.Id, _ => AccountQueryDisposition.Unqueried)));
            await ReloadAsync();
            AppState.ShowStatus($"所选 {keys.Count} 个账号没有可用的 EYA 查询凭据，已加入未查询分组。请先关联原账号。", InfoBarSeverity.Warning); return;
        }
        if (AppState.LoginPage is not { } loginPage)
        { AppState.ShowStatus("原查询页面尚未就绪，请重新进入页面后重试。", InfoBarSeverity.Warning); return; }
        var token = AppState.BeginBusyOperation();
        try
        {
            var result = await AccountQueryBatch.RunAsync(keys, async (key, cancellation) =>
            {
                var history = ManagedAccounts.History(key) ?? throw new InvalidOperationException();
                await loginPage.QueryAndSaveCsStatusAsync(history.AccountName, history.EyaToken, cancellation);
            }, token, (index, total) => AppState.ShowStatus($"正在查询 CS2 状态：{index}/{total}", InfoBarSeverity.Informational));
            await ManagedAccounts.Store.UpdateAsync(doc => AccountQueryGrouping.ApplyBatch(doc, accounts.Select(x => x.Id).ToList(), keys, result));
            AppState.ReloadHistory(); await ReloadAsync();
            AppState.ShowStatus($"CS2 查询{(result.Cancelled ? "已取消" : "完成")}：成功 {result.Succeeded} · 失败 {result.Failed} · 跳过 {result.Skipped}",
                result.Failed > 0 || result.Skipped > 0 || result.Cancelled ? InfoBarSeverity.Warning : InfoBarSeverity.Success);
        }
        finally { AppState.EndBusyOperation(); }
    }

    private void CardsContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        // 菜单不再挂在卡片上：由被点中的条目即时创建，避免 100 张卡片各持一个 MenuFlyout。
        // 事件源可能是卡片内部元素，也可能是 GridViewItem（容器的 Content 现在是轻量条目）。
        if (FindContextCard(args.OriginalSource) is not { } card) return;
        ShowCardMenu(card, card.CreateMenu(), args);
    }

    private void ShowCardMenu(AccountCard card, MenuFlyout menu, ContextRequestedEventArgs? args)
    {
#if MANAGER_UI_CHECKS
        LastCardMenu = menu;
#endif
        if (args is not null && args.TryGetPosition(card, out var position)) menu.ShowAt(card, new FlyoutShowOptions { Position = position });
        else menu.ShowAt(card);
        if (args is not null) args.Handled = true;
    }

    /// <summary>从右键事件源向上/向下解析出卡片（容器的 Content 现在是条目，不能直接强转）。</summary>
    internal AccountCard? FindContextCard(object? originalSource)
    {
        for (var source = originalSource as DependencyObject; source is not null; source = VisualTreeHelper.GetParent(source))
        {
            if (source is AccountCard card) return card;
            if (source is GridViewItem container) return CardIn(container);
        }
        return null;
    }

    /// <summary>容器（GridViewItem）当前实现的卡片；未实现/已回收时为 null。</summary>
    internal static AccountCard? CardIn(DependencyObject container)
    {
        // 容器由 ItemTemplate 生成：Content 是条目本身，控件树在容器的可视子树里，
        // 且 ContentTemplateRoot 不会从 ListViewItemPresenter 冒泡到 GridViewItem，只能向下找。
        if (container is AccountCard direct) return direct;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(container); i++)
        {
            if (CardIn(VisualTreeHelper.GetChild(container, i)) is { } found) return found;
        }
        return null;
    }

#if MANAGER_UI_CHECKS
    /// <summary>自检用：走与真实右键完全相同的解析/建菜单/弹出路径（区别只是不依赖指针事件）。</summary>
    internal MenuFlyout? OpenCardMenuForCheck(DependencyObject source)
    {
        if (FindContextCard(source) is not { } card) return null;
        var menu = card.CreateMenu();
        ShowCardMenu(card, menu, null);
        return menu;
    }
#endif
}
