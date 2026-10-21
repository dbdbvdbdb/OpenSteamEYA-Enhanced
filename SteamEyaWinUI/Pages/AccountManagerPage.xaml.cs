using System.Collections.ObjectModel;
using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Navigation;
using OpenSteamEya.AccountManagement;
using SteamEyaWinUI.Services;

namespace SteamEyaWinUI.Pages;

public sealed partial class AccountManagerPage : Page
{
    private ManagerDocument document = new();
    private readonly ObservableCollection<AccountCard> visible = [];
    private readonly HashSet<Guid> selected = [];
    private HashSet<Guid>? dragSelection;
    private readonly Dictionary<Guid, string?> resolved = [];
    private bool syncing = true;
    private bool privacy;
    private readonly SemaphoreSlim refreshGate = new(1);
    internal ManagerPreferences Preferences => document.Preferences;
    internal bool IsPrivate => privacy;

    public AccountManagerPage()
    {
        InitializeComponent(); Cards.ItemsSource = visible;
        AccountClassification.Changed += () => DispatcherQueue.TryEnqueue(() => _ = SafeAsync(ReloadAsync));
        AppState.HistoryChanged += steamId => DispatcherQueue.TryEnqueue(() => _ = SafeAsync(ReloadAsync));
        AppState.CachedLoginAccountsRefreshed += () => DispatcherQueue.TryEnqueue(() => _ = SafeAsync(ReloadAsync));
        AppState.BusyChanged += _ => DispatcherQueue.TryEnqueue(UpdateSelectionToolbar);
        ActualThemeChanged += (_, _) => { if (!syncing) Render(); };
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    { base.OnNavigatedTo(e); await SafeAsync(ReloadAsync); }

    private async Task ReloadAsync()
    {
        await refreshGate.WaitAsync();
        try
        {
            syncing = true;
            document = await ManagedAccounts.Store.LoadAsync();
            privacy = document.Preferences.Privacy;
            Privacy.IsChecked = privacy;
            ApplyPrivacyPill();
            Search.Visibility = GroupFilter.Visibility = privacy ? Visibility.Collapsed : Visibility.Visible;
            resolved.Clear();
            var identities = ManagedAccounts.Identities();
            var categories = await AccountClassification.Categories.LoadAsync();
            document.Accounts.RemoveAll(x => categories.IsBlack(x.LoginName, x.SteamId));
            foreach (var account in document.Accounts)
                resolved[account.Id] = AccountMapping.Resolve(account, identities).Key;
            var group = (GroupFilter.SelectedItem as ComboBoxItem)?.Tag;
            GroupFilter.Items.Clear(); GroupFilter.Items.Add(new ComboBoxItem { Content = "全部分组", Tag = "all" });
            GroupFilter.Items.Add(new ComboBoxItem { Content = "默认分组", Tag = "default" });
            foreach (var item in document.Groups.OrderBy(x => x.Order)) GroupFilter.Items.Add(new ComboBoxItem { Content = item.Name, Tag = item.Id.ToString() });
            SelectTag(GroupFilter, group?.ToString() ?? "all"); SelectTag(SortChoice, document.Preferences.Sort);
            SelectTag(ViewChoice, document.Preferences.View == "list" ? "list" : "cards");
            selected.IntersectWith(document.Accounts.Select(x => x.Id));
            syncing = false; Render();
        }
        finally { syncing = false; refreshGate.Release(); }
        if (eagerSettings) { eagerSettings = false; await ManagerSettingsAsync(); }
        if (pendingTool is { } tool)
        {
            pendingTool = null;
            if (tool == "import") await ImportAsync();
            else if (tool == "export") await ExportAsync();
            else if (tool == "backup") await BackupDialogAsync();
        }
    }
    private bool eagerSettings;
    private string? pendingTool;
    public void OpenManagerSettings() { eagerSettings = true; if (IsLoaded) _ = SafeAsync(ReloadAsync); }
    public void OpenTool(string tool) { pendingTool = tool; if (IsLoaded) _ = SafeAsync(ReloadAsync); }
    private static void SelectTag(ComboBox combo, string tag) => combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().FirstOrDefault(x => x.Tag?.ToString() == tag) ?? combo.Items[0];

    private void Render()
    {
        if (syncing) return;
        syncing = true;
        try
        {
            foreach (var old in visible) old.Stop();
            visible.Clear();
            var group = (GroupFilter.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            string search = Search.Text.Trim();
            IEnumerable<ManagedAccount> accounts = document.Accounts.Where(x =>
                AccountGroups.Matches(x, group) &&
                (search.Length == 0 || new[] { x.LoginName, x.SteamId, x.Nickname, x.Note,
                    ManagedAccounts.History(resolved.GetValueOrDefault(x.Id))?.PersonaName ?? "" }
                    .Any(v => v.Contains(search, StringComparison.OrdinalIgnoreCase))));
            string sort = document.Preferences.Sort;
            accounts = sort switch
            {
                "nickname" => accounts.OrderBy(x => DisplayName(x), StringComparer.CurrentCultureIgnoreCase),
                "login" => accounts.OrderBy(x => x.LoginName, StringComparer.OrdinalIgnoreCase),
                "recent" => accounts.OrderByDescending(x => x.LastUsedAt ?? ManagedAccounts.History(resolved.GetValueOrDefault(x.Id))?.LastLoginAt),
                "level" => accounts.OrderByDescending(x => ManagedAccounts.History(resolved.GetValueOrDefault(x.Id))?.CsPlayerLevel ?? -1),
                "favorite" => accounts.OrderByDescending(x => x.Favorite).ThenBy(x => x.Order),
                _ => accounts.OrderBy(x => x.Order)
            };
            if (document.Preferences.GroupDisplay == "sections" && group == "all")
                accounts = accounts.OrderBy(x => document.Groups.Where(g => AccountGroups.Ids(x).Contains(g.Id)).Select(g => g.Order).DefaultIfEmpty(-1).Min());
            if (document.Preferences.FavoritesFirst) accounts = accounts.OrderByDescending(x => x.Favorite);
            bool compact = document.Preferences.View == "list";
            Cards.CanReorderItems = Cards.CanDragItems = sort == "custom" && !document.Preferences.FavoritesFirst && document.Preferences.GroupDisplay != "sections";
            foreach (var account in accounts)
                visible.Add(new AccountCard(this, account, resolved.GetValueOrDefault(account.Id), compact));
            UpdateWidths();
            foreach (var card in visible.Where(x => selected.Contains(x.Account.Id))) Cards.SelectedItems.Add(card);
            foreach (var card in visible) card.SetSelected(selected.Contains(card.Account.Id));
            EmptyText.Visibility = visible.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            Summary.Text = $"{document.Accounts.Count} 个账号 · 当前 {visible.Count} 个 · 已选 {selected.Count} 个";
            UpdateSelectionToolbar();
        }
        finally { syncing = false; }
    }

    internal string DisplayName(ManagedAccount account) => account.Nickname.Length > 0 ? account.Nickname :
        ManagedAccounts.History(resolved.GetValueOrDefault(account.Id))?.PersonaName ??
        ManagedAccounts.Cached(resolved.GetValueOrDefault(account.Id))?.PersonaName ?? "未获取昵称";
    internal string GroupName(ManagedAccount account)
    {
        // 已有分组的账号不再同时显示“默认分组”。
        var names = document.Groups.Where(x => AccountGroups.Ids(account).Contains(x.Id))
            .OrderBy(x => x.Order).Select(x => x.Name).ToList();
        return names.Count == 0 ? "默认分组" : string.Join(" · ", names);
    }
    internal string DisplayCardName(ManagedAccount account) => DisplayName(account) + (account.Note.Length == 0 ? "" :
        "（" + account.Note.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ') + "）");

    private void FilterChanged(object sender, TextChangedEventArgs e) { if (!syncing) Render(); }
    private void SelectionFilterChanged(object sender, SelectionChangedEventArgs e) { if (!syncing) Render(); }
    private async void SortChanged(object sender, SelectionChangedEventArgs e)
    {
        if (syncing) return;
        await SafeAsync(async () => { var tag = (SortChoice.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "custom";
            await ManagedAccounts.Store.UpdateAsync(x => x.Preferences.Sort = tag); document.Preferences.Sort = tag; Render(); });
    }
    private async void ViewChanged(object sender, SelectionChangedEventArgs e)
    {
        if (syncing) return;
        await SafeAsync(async () => { var tag = (ViewChoice.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "cards";
            await ManagedAccounts.Store.UpdateAsync(x => x.Preferences.View = tag); document.Preferences.View = tag; Render(); });
    }
    private void ApplyPrivacyPill()
    {
        bool on = Privacy.IsChecked == true;
        Privacy.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[on ? "PclAccentBrush" : "PclSurfaceAltBrush"];
        Privacy.BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[on ? "PclAccentBrush" : "PclBorderBrush"];
        Privacy.Foreground = on
            ? new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White)
            : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["PclTextSecondaryBrush"];
    }
    private async void Privacy_Click(object sender, RoutedEventArgs e)
    {
        await SafeAsync(async () =>
        {
            var enabled = Privacy.IsChecked == true;
            await ManagedAccounts.Store.UpdateAsync(doc => doc.Preferences.Privacy = enabled);
            await ReloadAsync();
        });
    }
    private void CardsSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (syncing) return;
        foreach (var item in e.RemovedItems.OfType<AccountCard>().Where(visible.Contains)) selected.Remove(item.Account.Id);
        foreach (var item in e.AddedItems.OfType<AccountCard>().Where(visible.Contains)) selected.Add(item.Account.Id);
        foreach (var card in visible) card.SetSelected(selected.Contains(card.Account.Id));
        Summary.Text = $"{document.Accounts.Count} 个账号 · 当前 {visible.Count} 个 · 已选 {selected.Count} 个";
        UpdateSelectionToolbar();
    }
    private void CardsSizeChanged(object sender, SizeChangedEventArgs e) => UpdateWidths();
    private void UpdateWidths()
    {
        double width = Math.Max(280, Cards.ActualWidth - 24);
        int columns = Preferences.View == "list" ? 1 : width < 800 ? 1 : width < 1600 ? 2 : Math.Max(3, (int)(width / 520));
        foreach (var card in visible) card.Width = Math.Max(260, width / columns - 14);
    }
    private async void CardsDragCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        FinishReorder();
        await SafeAsync(PersistVisibleOrderAsync);
    }
    private void CardsDragStarting(object sender, DragItemsStartingEventArgs args) => BeginReorder();
    private void BeginReorder() { dragSelection = selected.ToHashSet(); syncing = true; }
    private void FinishReorder()
    {
        if (dragSelection is not null) { selected.Clear(); selected.UnionWith(dragSelection); dragSelection = null; }
        syncing = false;
    }
    private async Task PersistVisibleOrderAsync()
    {
            var ids = visible.Select(x => x.Account.Id).ToList();
            var positions = document.Accounts.Where(x => ids.Contains(x.Id)).OrderBy(x => x.Order).Select(x => x.Order).ToList();
            await ManagedAccounts.Store.UpdateAsync(doc => { for (int i = 0; i < ids.Count; i++) doc.Accounts.First(x => x.Id == ids[i]).Order = positions[i]; });
            await ReloadAsync();
    }

    internal async Task SafeAsync(Func<Task> action)
    {
        try { await action(); }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            // Do not log exceptions from password/file parsing. Only fixed, data-free messages are shown.
            string message = ex is InvalidDataException ? ex.Message : "操作未完成。请检查文件权限、磁盘空间或网络连接后重试。";
            AppState.ShowStatus(message, InfoBarSeverity.Error);
        }
    }
    private async void Add_Click(object sender, RoutedEventArgs e) => await SafeAsync(() => EditAsync(new ManagedAccount()));
    private async void Existing_Click(object sender, RoutedEventArgs e) => await SafeAsync(AddExistingAsync);
    private async void Import_Click(object sender, RoutedEventArgs e) => await SafeAsync(ImportAsync);
    private async void Export_Click(object sender, RoutedEventArgs e) => await SafeAsync(ExportAsync);
    private async void Backup_Click(object sender, RoutedEventArgs e) => await SafeAsync(BackupDialogAsync);
    private async void Groups_Click(object sender, RoutedEventArgs e) => await SafeAsync(GroupsAsync);
    private async void Settings_Click(object sender, RoutedEventArgs e) => await SafeAsync(ManagerSettingsAsync);
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await SafeAsync(async () =>
    { AppState.ReloadHistory(); await ReloadAsync(); });

    internal async Task ChangeAsync(ManagedAccount account, Action<ManagedAccount> change)
    {
        await ManagedAccounts.Store.UpdateAsync(doc => { var record = doc.Accounts.First(x => x.Id == account.Id); change(record); });
        await ReloadAsync();
    }

    internal async Task LoginAsync(ManagedAccount account, string? key)
    {
        if (AppState.IsBusy) return;
        var history = ManagedAccounts.History(key); var cached = ManagedAccounts.Cached(key);
        if (history is null && cached is null) { AppState.ShowStatus("该账号尚未关联可用的原有登录凭证。请在原登录页面添加凭证。", InfoBarSeverity.Warning); return; }
        var token = AppState.BeginBusyOperation();
        try
        {
            var progress = new Progress<string>(text => AppState.ShowStatus(text, InfoBarSeverity.Informational));
            if (history is not null && AppState.LoginPage is { } page)
                await page.QuickLoginAsync(history.AccountName, history.EyaToken, progress, token);
            else if (cached is not null)
            {
                if (!await SteamPathCoordinator.EnsureResolvedAsync()) return;
                await Task.Run(() => AppState.LoginService.RestoreCachedLogin(cached, progress), token);
            }
            else return;
            await ChangeAsync(account, x => x.LastUsedAt = DateTimeOffset.UtcNow);
            AppState.ShowStatus("已请求启动 Steam。", InfoBarSeverity.Success);
        }
        finally { AppState.EndBusyOperation(); }
    }

    internal async Task QueryAsync(string? key)
    {
        var history = ManagedAccounts.History(key);
        if (AppState.IsBusy || history is null || AppState.LoginPage is null) return;
        var token = AppState.BeginBusyOperation();
        try
        {
            await AppState.LoginPage.QueryAndSaveCsStatusAsync(history.AccountName, history.EyaToken, token);
            await ManagedAccounts.Store.UpdateAsync(doc => AccountQueryGrouping.Apply(doc,
                document.Accounts.Where(x => resolved.GetValueOrDefault(x.Id) == key).ToDictionary(x => x.Id, _ => AccountQueryDisposition.Success)));
            AppState.ReloadHistory(); await ReloadAsync();
        }
        finally { AppState.EndBusyOperation(); }
    }
}
