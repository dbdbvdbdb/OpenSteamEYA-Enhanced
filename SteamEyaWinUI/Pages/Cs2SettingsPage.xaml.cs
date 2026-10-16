using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SteamEyaWinUI.Localization;
using SteamEyaWinUI.Models;
using SteamEyaWinUI.Services;

namespace SteamEyaWinUI.Pages;

public sealed partial class Cs2SettingsPage : Page, INotifyPropertyChanged
{
    private readonly Cs2LocalSettingsService _service = new();
    private Cs2LocalSettingsSnapshot _snapshot = new([], []);
    private IReadOnlyList<Cs2SettingsIssue> _scanIssues = [];
    private SteamPaths? _paths;
    private CancellationTokenSource? _loadCts;
    private bool _syncing;
    private bool _busy;
    private bool _active;

    public event PropertyChangedEventHandler? PropertyChanged;
    internal LocalizedStrings Strings => Loc.Strings;

    public Cs2SettingsPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_active) return;
        _active = true;
        Loc.LanguageChanged += OnLanguageChanged;
        RebuildFilters();
        await LoadAsync(scanAccounts: true);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _active = false;
        Loc.LanguageChanged -= OnLanguageChanged;
        _loadCts?.Cancel();
    }

    private void OnLanguageChanged()
    {
        PropertyChanged?.Invoke(this, new(nameof(Strings)));
        RebuildFilters();
        Render();
    }

    private uint? SelectedAccountId => (AccountBox.SelectedItem as ComboBoxItem)?.Tag as uint?;
    private string SelectedFile => (FileBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "#local";
    private Cs2SettingCategory? SelectedCategory => (CategoryBox.SelectedItem as ComboBoxItem)?.Tag as Cs2SettingCategory?;

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await LoadAsync(scanAccounts: true);
    private async void AccountBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_syncing && !_busy && _active) await LoadAsync(scanAccounts: false);
    }

    private async Task LoadAsync(bool scanAccounts)
    {
        _loadCts?.Cancel();
        var cts = new CancellationTokenSource();
        _loadCts = cts;
        var token = cts.Token;
        SetBusy(true);
        _snapshot = new([], []);
        SettingsList.ItemsSource = null;
        EmptyText.Text = Loc.T("Cs2Settings_Loading");
        EmptyText.Visibility = Visibility.Visible;
        ReadInfoBar.IsOpen = false;
        CountText.Text = "";
        SourcePathText.Text = "";
        try
        {
            if (scanAccounts)
            {
                uint? previous = SelectedAccountId;
                var result = await Task.Run(() =>
                {
#if MANAGER_UI_CHECKS
                    var paths = ManagerUiChecks.Enabled ? ManagerUiChecks.Cs2FixturePaths : SteamPathCoordinator.ResolvePathsOrThrow();
#else
                    var paths = SteamPathCoordinator.ResolvePathsOrThrow();
#endif
                    var scan = _service.ScanAccounts(paths.UserdataPath, token);
#if MANAGER_UI_CHECKS
                    var names = ManagerUiChecks.Enabled ? new Dictionary<string, OfflineAccountName>() :
                        SteamAccountNameService.BuildOfflineNames(paths,
                            scan.Accounts.Select(a => new Cs2SettingsSource(a.SteamId64, a.AccountId)).ToArray());
#else
                    var names = SteamAccountNameService.BuildOfflineNames(paths,
                        scan.Accounts.Select(a => new Cs2SettingsSource(a.SteamId64, a.AccountId)).ToArray());
#endif
                    return (paths, scan, names);
                }, token);
                if (token.IsCancellationRequested || !_active) return;
                _paths = result.paths;
                _scanIssues = result.scan.Issues;
                _syncing = true;
                try
                {
                    AccountBox.Items.Clear();
                    foreach (var account in result.scan.Accounts)
                    {
                        result.names.TryGetValue(account.SteamId64, out var name);
                        var history = AppState.HistoryAccounts.FirstOrDefault(a => a.SteamId == account.SteamId64);
                        var label = new[] { history?.PersonaName, name?.PersonaName, history?.AccountName, name?.AccountName }
                            .FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));
                        AccountBox.Items.Add(new ComboBoxItem { Tag = account.AccountId,
                            Content = label is null ? account.SteamId64 : $"{label} ({account.SteamId64})" });
                    }
                    AccountBox.SelectedItem = AccountBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (uint)i.Tag == previous)
                                              ?? AccountBox.Items.FirstOrDefault();
                }
                finally { _syncing = false; }
            }

            if (_paths is not null && SelectedAccountId is { } id)
            {
                var snapshot = await Task.Run(() => _service.ReadAccount(_paths.UserdataPath, id, token), token);
                if (token.IsCancellationRequested || !_active) return;
                _snapshot = snapshot;
            }
            if (token.IsCancellationRequested || !_active) return;
            RebuildFilters();
            Render();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception)
        {
            if (token.IsCancellationRequested || !_active) return;
            // No config values or account details are logged on failure.
            EmptyText.Text = Loc.T("Cs2Settings_LoadFailed");
            EmptyText.Visibility = Visibility.Visible;
        }
        finally
        {
            if (ReferenceEquals(_loadCts, cts))
            {
                _loadCts = null;
                SetBusy(false);
            }
            cts.Dispose();
        }
    }

    private void SetBusy(bool value)
    {
        _busy = value;
        AccountBox.IsEnabled = RefreshButton.IsEnabled = FileBox.IsEnabled = CategoryBox.IsEnabled = SearchBox.IsEnabled = !value;
        LoadingRing.IsActive = value;
        LoadingRing.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RebuildFilters()
    {
        string file = SelectedFile;
        var category = SelectedCategory;
        _syncing = true;
        try
        {
            FileBox.Items.Clear();
            FileBox.Items.Add(new ComboBoxItem { Content = Loc.T("Cs2Settings_LocalFiles"), Tag = "#local" });
            FileBox.Items.Add(new ComboBoxItem { Content = Loc.T("Cs2Settings_AllFiles"), Tag = "#all" });
            foreach (var item in _snapshot.Files)
                FileBox.Items.Add(new ComboBoxItem { Content = item.RelativePath, Tag = item.RelativePath });
            if (file == "#local" && _snapshot.Files.Count > 0 && _snapshot.Files.All(f => f.IsCloudCache)) file = "#all";
            FileBox.SelectedItem = FileBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == file) ?? FileBox.Items[0];
            CategoryBox.Items.Clear();
            CategoryBox.Items.Add(new ComboBoxItem { Content = Loc.T("Cs2Settings_AllCategories") });
            foreach (var item in Enum.GetValues<Cs2SettingCategory>())
                CategoryBox.Items.Add(new ComboBoxItem { Content = CategoryName(item), Tag = item });
            CategoryBox.SelectedItem = CategoryBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => i.Tag as Cs2SettingCategory? == category)
                                       ?? CategoryBox.Items[0];
        }
        finally { _syncing = false; }
    }

    private void Filter_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (!_syncing && !_busy) Render(); }
    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) { if (!_syncing && !_busy) Render(); }

    private static string CategoryName(Cs2SettingCategory category) => Loc.T("Cs2Settings_Category_" + category);

    private void Render()
    {
        if (!_active || SettingsList is null) return;
        string fileFilter = SelectedFile;
        var category = SelectedCategory;
        string query = SearchBox.Text.Trim();
        var files = _snapshot.Files.Where(f => fileFilter == "#all" ||
            (fileFilter == "#local" ? !f.IsCloudCache : f.RelativePath == fileFilter)).ToArray();
        var rows = new List<Cs2SettingRow>();
        int total = files.Sum(f => f.Settings.Count);
        foreach (var file in files)
        foreach (var setting in file.Settings)
        {
            if (category is not null && category != setting.Category) continue;
            string labelKey = "Cs2Settings_Option_" + setting.Key.TrimEnd('$').ToLowerInvariant();
            string label = Loc.T(labelKey);
            if (label == labelKey) label = setting.Key;
            string detail = $"{CategoryName(setting.Category)} · {setting.Section}/{setting.Key}";
            string source = $"{Loc.T(file.IsCloudCache ? "Cs2Settings_CloudCache" : "Cs2Settings_Local")} · {file.RelativePath} · {file.LastWriteTimeUtc.ToLocalTime():g}";
            if (query.Length > 0 && !string.Join(' ', label, detail, setting.Value, source).Contains(query, StringComparison.CurrentCultureIgnoreCase)) continue;
            rows.Add(new() { Title = label, Detail = detail, Value = setting.Value, Source = source });
        }
        SettingsList.ItemsSource = rows;
        EmptyText.Text = Loc.T(SelectedAccountId is null ? "Cs2Settings_NoAccounts" :
            _snapshot.Files.Count == 0 ? "Cs2Settings_NoFiles" : "Cs2Settings_NoMatches");
        EmptyText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        CountText.Text = Loc.Tf("Cs2Settings_Count", rows.Count, total, files.Length);
        SourcePathText.Text = files.Length == 1 ? files[0].Path :
            _paths is not null && SelectedAccountId is { } id ? Path.Combine(_paths.UserdataPath, id.ToString(System.Globalization.CultureInfo.InvariantCulture), "730") : "";
        var issues = _scanIssues.Concat(_snapshot.Issues).ToArray();
        ReadInfoBar.Message = issues.Length == 0 ? "" : Loc.Tf("Cs2Settings_Issues", issues.Length) + "\n" +
            string.Join('\n', issues.Take(3).Select(i => $"{i.Path}: {Loc.T("Cs2Settings_" + i.Reason)}"));
        ReadInfoBar.IsOpen = issues.Length > 0;
    }
}
