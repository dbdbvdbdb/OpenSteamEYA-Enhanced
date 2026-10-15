using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenSteamEya.AccountManagement;
using SteamEyaWinUI.Models;
using SteamEyaWinUI.Services;

namespace SteamEyaWinUI.Pages;

public sealed partial class HistoryPage
{
    private async Task LoginBlackCacheAsync(SteamAccountHistoryItem item, IProgress<string> progress, CancellationToken token)
    {
        var doc = await ManagedAccounts.Store.LoadAsync();
        var managed = AccountClassification.Find(doc, item.AccountName, item.SteamId)
            ?? new ManagedAccount { LoginName = item.AccountName, SteamId = item.SteamId };
        var cached = AppState.LoginService.GetCachedLoginAccounts();
        var mapping = AccountMapping.Resolve(managed, cached.Select(x => new ExistingIdentity("cache:" + x.CacheKey, x.AccountName, x.SteamId)));
        var match = cached.FirstOrDefault(x => "cache:" + x.CacheKey == mapping.Key);
        if (match is null)
            throw new InvalidOperationException("没有可用或唯一关联的 EYA／Steam 缓存凭据，请先在登录页补充凭据。");
        if (!await SteamPathCoordinator.EnsureResolvedAsync()) throw new OperationCanceledException();
        await Task.Run(() => AppState.LoginService.RestoreCachedLogin(match, progress), token);
        await ManagedAccounts.Store.UpdateAsync(current =>
        {
            var account = AccountClassification.Find(current, item.AccountName, item.SteamId);
            if (account is not null) account.LastUsedAt = DateTimeOffset.UtcNow;
        });
    }

    private async Task<int> SetBlackGroupsAsync(IReadOnlyList<SteamAccountHistoryItem> accounts, string groupId, bool add)
    {
        var id = Guid.Parse(groupId);
        int changed = 0;
        await ManagedAccounts.Store.UpdateAsync(doc =>
        {
            foreach (var item in accounts)
            {
                var account = AccountClassification.Find(doc, item.AccountName, item.SteamId);
                if (account is null) continue;
                var ids = AccountGroups.Ids(account).ToHashSet();
                if (add) ids.Add(id); else ids.Remove(id);
                AccountGroups.Set(account, ids); changed++;
            }
        });
        AccountClassification.NotifyChanged(); return changed;
    }

    private async Task<AccountGroup> CreateBlackGroupAsync(string name)
    {
        ManagedGroup group = new() { Name = name.Trim() };
        await ManagedAccounts.Store.UpdateAsync(doc =>
        {
            var existing = doc.Groups.FirstOrDefault(x => x.Name.Equals(group.Name, StringComparison.OrdinalIgnoreCase));
            if (existing is not null) { group = existing; return; }
            group.Order = doc.Groups.Count; doc.Groups.Add(group);
        });
        _blackDocument = await ManagedAccounts.Store.LoadAsync(); LoadGroups();
        return new() { Id = group.Id.ToString(), Name = group.Name, Order = group.Order };
    }

    private async Task ManageBlackGroupsAsync()
    {
        if (_isDialogFlowActive) return;
        _isDialogFlowActive = true;
        try
        {
            _blackDocument = await ManagedAccounts.Store.LoadAsync(); LoadGroups();
            var list = new ComboBox { Header = "分组（与白号管理共用定义）", HorizontalAlignment = HorizontalAlignment.Stretch };
            foreach (var group in _groups) list.Items.Add(new ComboBoxItem { Content = group.Name, Tag = group.Id });
            var operation = new ComboBox { Header = "操作", HorizontalAlignment = HorizontalAlignment.Stretch };
            foreach (var label in new[] { "创建分组", "重命名分组", "删除分组，保留所有账号" }) operation.Items.Add(label);
            operation.SelectedIndex = 0;
            var name = new TextBox { Header = "分组名称" };
            var panel = new StackPanel { Spacing = 12 }; panel.Children.Add(list); panel.Children.Add(operation); panel.Children.Add(name);
            var dialog = new ContentDialog { Title = "管理分组", Content = panel, PrimaryButtonText = "保存", CloseButtonText = "取消", XamlRoot = XamlRoot };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            if (operation.SelectedIndex == 0)
            {
                if (string.IsNullOrWhiteSpace(name.Text)) throw new System.IO.InvalidDataException("请输入分组名称。");
                await CreateBlackGroupAsync(name.Text);
            }
            else
            {
                if (list.SelectedItem is not ComboBoxItem selected) throw new System.IO.InvalidDataException("请选择分组。");
                var id = Guid.Parse((string)selected.Tag);
                if (operation.SelectedIndex == 2) await ManagedAccounts.Store.DeleteGroupAsync(id);
                else
                {
                    var label = name.Text.Trim();
                    if (label.Length == 0) throw new System.IO.InvalidDataException("请输入分组名称。");
                    await ManagedAccounts.Store.UpdateAsync(doc =>
                    {
                        if (doc.Groups.Any(x => x.Id != id && x.Name.Equals(label, StringComparison.OrdinalIgnoreCase)))
                            throw new System.IO.InvalidDataException("分组名称重复。");
                        doc.Groups.First(x => x.Id == id).Name = label;
                    });
                }
            }
            AccountClassification.NotifyChanged();
        }
        catch (Exception ex) { AppState.ShowStatus(ex.Message, InfoBarSeverity.Error); }
        finally { _isDialogFlowActive = false; }
    }
}
