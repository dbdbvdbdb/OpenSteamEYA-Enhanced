using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenSteamEya.AccountManagement;
using SteamEyaWinUI.Models;

namespace SteamEyaWinUI.Services;

internal static class AccountClassification
{
    internal static CategoryStore Categories { get; } = new(ManagedAccounts.Store.Root);
    internal static event Action? Changed;
    internal static void NotifyChanged() => Changed?.Invoke();
    internal static bool Same(ManagedAccount x, string name, string id) => CategoryIdentity.Matches(x.LoginName, x.SteamId, name, id);
    internal static ManagedAccount? Find(ManagerDocument doc, string name, string id) =>
        (!string.IsNullOrWhiteSpace(id) ? doc.Accounts.FirstOrDefault(x => x.SteamId == id) : null)
        ?? doc.Accounts.FirstOrDefault(x => Same(x, name, id));

    internal static async Task<bool> AddAsync(FrameworkElement owner, string name, string id, bool black,
        string? password = null, bool explicitMove = false)
    {
        var doc = await ManagedAccounts.Store.LoadAsync();
        var categories = await Categories.LoadAsync();
        var existing = Find(doc, name, id);
        if (existing is null && doc.Accounts.Any(x => x.LoginName.Equals(name, StringComparison.OrdinalIgnoreCase)))
            throw new System.IO.InvalidDataException("登录账号对应不同 SteamID，请先核对已有记录，未改变其归属。");
        var wasBlack = categories.IsBlack(name, id);
        bool conflict = black ? existing is not null && !wasBlack : wasBlack;
        if (conflict || explicitMove)
        {
            var form = new StackPanel { Spacing = 14, MaxWidth = 560 };
            form.Children.Add(new TextBlock { Text = "同一账号只能保留一个管理归属。请选择要保留的分类；取消不会改变现有归属。", TextWrapping = TextWrapping.Wrap });
            var columns = new Grid { ColumnSpacing = 20 };
            columns.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            columns.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            TextBlock Record(string title, string login, string steam, string note, bool hasPassword) => new()
            { Text = $"{title}\n账号：{login}\nSteamID：{steam}\n备注：{note}\n密码：{(hasPassword ? "已保存（隐藏）" : "未保存")}", TextWrapping = TextWrapping.Wrap };
            columns.Children.Add(Record(wasBlack ? "现有：黑号管理" : "现有：白号管理", existing?.LoginName ?? name,
                existing?.SteamId ?? id, existing?.Note ?? "", existing?.Password.Length > 0));
            var incoming = Record(black ? "本次：黑号管理" : "本次：白号管理", name, id, "保留现有备注、分组、收藏和背景", password is not null);
            Grid.SetColumn(incoming, 1); columns.Children.Add(incoming); form.Children.Add(columns);
            form.Children.Add(new TextBlock { Text = password is null ? "本次不会修改已有密码。" : "确认保留在白号管理将保存本次输入的密码；选择黑号管理则保留原密码。", TextWrapping = TextWrapping.Wrap });
            var dialog = new ContentDialog { Title = "账号归属冲突 / 转移", Content = form, PrimaryButtonText = "保留在白号管理",
                SecondaryButtonText = "保留在黑号管理", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close,
                XamlRoot = owner.XamlRoot, RequestedTheme = owner.ActualTheme };
            var choice = await dialog.ShowAsync();
            if (choice == ContentDialogResult.None) return false;
            black = choice == ContentDialogResult.Secondary;
            if (black) password = null;
        }
        if (black) await Categories.UpdateAsync(x => x.Set(name, id, true));
        try
        {
            // Atomic update preserves all user metadata and refuses conflicting known Steam IDs.
            await ManagedAccounts.Store.UpdateAsync(current =>
            {
                var account = Find(current, name, id);
                if (account is null)
                {
                    account = new() { LoginName = name, SteamId = id, Order = current.Accounts.Count };
                    var history = AppState.HistoryAccounts.FirstOrDefault(x => CategoryIdentity.Matches(name, id, x.AccountName, x.SteamId));
                    account.Nickname = history?.PersonaName ?? "";
                    account.Note = history?.Note ?? "";
                    if (history is not null)
                    {
                        var groupIds = new List<Guid>();
                        foreach (var oldGroup in AppState.SettingsService.Load().Groups.Where(x => history.GroupIds.Contains(x.Id)))
                        {
                            var group = current.Groups.FirstOrDefault(x => x.Name == oldGroup.Name);
                            if (group is null) { group = new() { Name = oldGroup.Name, Order = current.Groups.Count }; current.Groups.Add(group); }
                            groupIds.Add(group.Id);
                        }
                        AccountGroups.Set(account, groupIds);
                    }
                    current.Accounts.Add(account);
                }
                if (account.SteamId.Length == 0) account.SteamId = id;
                if (password is not null) account.Password = password;
            });
        }
        catch
        {
            if (black && !wasBlack) await Categories.UpdateAsync(x => x.Set(name, id, false));
            throw;
        }
        if (!black) await Categories.UpdateAsync(x => x.Set(name, id, false));
        NotifyChanged();
        return true;
    }

    internal static async Task<ImportResult> ImportWhiteAsync(FrameworkElement owner, ImportPreview preview, DuplicatePolicy policy)
    {
        int added = 0, updated = 0, skipped = 0;
        foreach (var row in preview.Rows)
        {
            var categories = await Categories.LoadAsync();
            var current = await ManagedAccounts.Store.LoadAsync();
            var existing = Find(current, row.Account.LoginName, row.Account.SteamId);
            if (categories.IsBlack(row.Account.LoginName, row.Account.SteamId))
            {
                var password = policy == DuplicatePolicy.Skip || policy == DuplicatePolicy.FillMissing && existing?.Password.Length > 0 ? null : row.Account.Password;
                if (!await AddAsync(owner, row.Account.LoginName, row.Account.SteamId, false, password) ||
                    (await Categories.LoadAsync()).IsBlack(row.Account.LoginName, row.Account.SteamId)) { skipped++; continue; }
                updated++; continue;
            }
            var imported = AccountStore.Clone(row.Account);
            if (existing is not null) imported.LoginName = existing.LoginName;
            var result = await ManagedAccounts.Store.ImportAsync(new([new(row.Line, imported)], []), policy);
            added += result.Added; updated += result.Updated; skipped += result.Skipped;
        }
        NotifyChanged();
        return new(added, updated, skipped, preview.Issues.Count);
    }

    internal static async Task<IReadOnlyList<SteamAccountHistoryItem>> BlackItemsAsync()
    {
        var categories = await Categories.LoadAsync();
        var doc = await ManagedAccounts.Store.LoadAsync();
        var result = new List<SteamAccountHistoryItem>();
        foreach (var identity in categories.BlackAccounts)
        {
            var history = AppState.HistoryAccounts.FirstOrDefault(x => CategoryIdentity.Matches(identity.LoginName, identity.SteamId, x.AccountName, x.SteamId));
            var manager = Find(doc, identity.LoginName, identity.SteamId);
            if (history is not null)
            {
                var clone = System.Text.Json.JsonSerializer.Deserialize(
                    System.Text.Json.JsonSerializer.Serialize(new AccountHistoryDocument { Accounts = [history] }, AccountHistoryJsonContext.Default.AccountHistoryDocument),
                    AccountHistoryJsonContext.Default.AccountHistoryDocument)!.Accounts[0];
                if (manager is not null) { clone.Note = manager.Note; clone.GroupIds = AccountGroups.Ids(manager).Select(x => x.ToString()).ToList(); }
                result.Add(clone);
            }
            else if (manager is not null) result.Add(new() { AccountName = manager.LoginName, SteamId = manager.SteamId,
                PersonaName = manager.Nickname, Note = manager.Note, GroupIds = AccountGroups.Ids(manager).Select(x => x.ToString()).ToList(), LastLoginAt = manager.LastUsedAt ?? default });
        }
        return result.DistinctBy(AccountHistoryService.GetAccountKey, StringComparer.OrdinalIgnoreCase).ToList();
    }

    internal static async Task<int> DeleteAsync(FrameworkElement owner, IReadOnlyList<CategoryIdentity> identities)
    {
        if (identities.Count == 0) return 0;
        var managerSnapshot = await ManagedAccounts.Store.LoadAsync();
        var identityCandidates = managerSnapshot.Accounts.Select(x => new CategoryIdentity { LoginName = x.LoginName, SteamId = x.SteamId })
            .Concat(AppState.HistoryAccounts.Select(x => new CategoryIdentity { LoginName = x.AccountName, SteamId = x.SteamId }))
            .Concat(AppState.LoginService.GetAllCachedLoginAccountsForDeletion().Select(x => new CategoryIdentity { LoginName = x.AccountName, SteamId = x.SteamId })).ToList();
        foreach (var identity in identities.Where(x => string.IsNullOrWhiteSpace(x.SteamId)))
            if (identityCandidates.Where(x => x.LoginName.Equals(identity.LoginName, StringComparison.OrdinalIgnoreCase) && x.SteamId.Length > 0).Select(x => x.SteamId).Distinct().Count() > 1)
                throw new System.IO.InvalidDataException("所选账号名关联多个 SteamID，请先核实 SteamID 再删除，未执行任何清理。");
        var records = new CheckBox { Content = "删除应用内记录", IsChecked = true };
        var cache = new CheckBox { Content = "删除 Steam 登录缓存", IsChecked = false };
        var form = new StackPanel { Spacing = 12 };
        form.Children.Add(new TextBlock { Text = $"已选择 {identities.Count} 个账号。", TextWrapping = TextWrapping.Wrap });
        form.Children.Add(records);
        form.Children.Add(new TextBlock { Text = "删除白号、黑号及历史账号记录，包含保存的密码、备注和历史 EYA Token。", TextWrapping = TextWrapping.Wrap });
        form.Children.Add(cache);
        form.Children.Add(new TextBlock { Text = "删除应用保存的对应 Steam 快速登录缓存；之后可能需要重新登录。不会删除 Steam 安装目录或游戏文件。", TextWrapping = TextWrapping.Wrap });
        var dialog = new ContentDialog { Title = "删除账号", Content = form, PrimaryButtonText = "删除所选内容", CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close, XamlRoot = owner.XamlRoot, RequestedTheme = owner.ActualTheme };
        void Update(object s, RoutedEventArgs e) => dialog.IsPrimaryButtonEnabled = records.IsChecked == true || cache.IsChecked == true;
        records.Checked += Update; records.Unchecked += Update; cache.Checked += Update; cache.Unchecked += Update;
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return 0;
        bool Matches(string name, string id) => identities.Any(x => CategoryIdentity.Matches(name, id, x.LoginName, x.SteamId));
        int removed = 0;
        try
        {
            // Each original store retains its own atomic writer. Stop on failure and report the partial operation.
            if (records.IsChecked == true)
            {
                AppState.AccountHistoryService.DeleteAccounts(AppState.HistoryAccounts.Where(x => Matches(x.AccountName, x.SteamId)).ToList());
                await ManagedAccounts.Store.UpdateAsync(doc => doc.Accounts.RemoveAll(x => Matches(x.LoginName, x.SteamId)));
                await Categories.UpdateAsync(doc => doc.BlackAccounts.RemoveAll(x => Matches(x.LoginName, x.SteamId)));
                removed = identities.Count;
            }
            if (cache.IsChecked == true)
            {
                var caches = AppState.LoginService.GetAllCachedLoginAccountsForDeletion().Where(x => Matches(x.AccountName, x.SteamId)).ToList();
                removed = Math.Max(removed, AppState.LoginService.DeleteCachedLoginAccounts(caches));
            }
        }
        catch (Exception ex) { throw new System.IO.IOException("删除未全部完成；已完成的项目不会回滚，请刷新后检查并重试剩余项目。", ex); }
        finally { AppState.ReloadHistory(); AppState.NotifyCachedLoginAccountsRefreshed(); NotifyChanged(); }
        return removed;
    }
}
