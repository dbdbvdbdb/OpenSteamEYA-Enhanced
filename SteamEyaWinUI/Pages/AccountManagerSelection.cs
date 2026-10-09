using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using OpenSteamEya.AccountManagement;
using SteamEyaWinUI.Services;

namespace SteamEyaWinUI.Pages;

public sealed partial class AccountManagerPage
{
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
            foreach (var card in visible)
            {
                if (invert && selected.Contains(card.Account.Id)) selected.Remove(card.Account.Id);
                else selected.Add(card.Account.Id);
            }
            Cards.SelectedItems.Clear();
            foreach (var card in visible)
            {
                bool isSelected = selected.Contains(card.Account.Id);
                if (isSelected) Cards.SelectedItems.Add(card);
                card.SetSelected(isSelected);
            }
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
        foreach (var card in visible) card.SetSelected(false);
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
        // Also expose the right-click menu when keyboard focus is on the native item container.
        for (var source = args.OriginalSource as DependencyObject; source is not null; source = VisualTreeHelper.GetParent(source))
        {
            var card = source as AccountCard ?? (source as GridViewItem)?.Content as AccountCard;
            if (card is null) continue;
            if (args.TryGetPosition(card, out var position)) card.ContextFlyout.ShowAt(card, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions { Position = position });
            else card.ContextFlyout.ShowAt(card);
            args.Handled = true; return;
        }
    }
}
