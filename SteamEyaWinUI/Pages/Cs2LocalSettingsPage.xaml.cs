using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using SteamEyaWinUI.Cs2Settings;
using SteamEyaWinUI.Services;
using Windows.Storage.Pickers;

namespace SteamEyaWinUI.Pages;

public sealed partial class Cs2LocalSettingsPage : Page
{
    private UnifiedConfigStore? _templates;
    private TemplateSnapshot? _snapshot, _working;
    private List<TemplateSetting> _settings = [];
    private readonly Dictionary<string, string> _bindings = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<DisplayMode> _modes = [];
    private bool _busy, _syncing, _bindingSelection, _resolutionDirty;
    private string _category = "视频";
    private string? _steamOverride;
    private string? _videoError;
    private int DirtyCount => _settings.Count(s => s.Dirty) + _bindings.Count + (_resolutionDirty ? 1 : 0) +
        (_snapshot is not null && TemplateNameBox.Text != _snapshot.Manifest.Name ? 1 : 0);
#if MANAGER_UI_CHECKS
    internal bool IsBusyForCheck => _busy;
    internal Task PrepareForCheckAsync(UnifiedConfigStore store, string steamRoot) { _templates = store; _steamOverride = steamRoot; return LoadTemplateAsync(); }
    internal void SelectCategoryForCheck(string category) { _category = category; Render(); }
#endif
    public Cs2LocalSettingsPage()
    {
        InitializeComponent(); NavigationCacheMode = NavigationCacheMode.Required;
        foreach (string category in new[] { "视频", "音频", "游戏", "HUD", "准星", "鼠标键盘", "键位绑定" })
        {
            var button = new ToggleButton { Content = category, Tag = category, MinWidth = 62, Padding = new Thickness(12, 8, 12, 8) };
            button.Click += (_, _) => { _category = category; Render(); };
            TabsPanel.Children.Add(button);
        }
        foreach (string key in SettingCatalog.BindKeys) KeyBox.Items.Add(key);
        Loaded += async (_, _) =>
        {
#if MANAGER_UI_CHECKS
            if (ManagerUiChecks.Enabled) return;
#endif
            if (_snapshot is null) await LoadTemplateAsync();
        };
    }
    private void Status(string text, InfoBarSeverity severity = InfoBarSeverity.Informational)
    { StatusBar.Message = text; StatusBar.Severity = severity; StatusBar.IsOpen = true; }
    private void Busy(bool busy)
    {
        _busy = busy; BusyRing.IsActive = busy;
        TemplateNameBox.IsEnabled = ImportButton.IsEnabled = ExportButton.IsEnabled = RestoreButton.IsEnabled = !busy;
        SettingsScroll.IsEnabled = SearchBox.IsEnabled = !busy;
        foreach (var tab in TabsPanel.Children.OfType<ToggleButton>()) tab.IsEnabled = !busy;
        UpdateActions();
    }
    private void UpdateActions()
    {
        int invalid = _settings.Count(s => s.Dirty && s.Error is not null);
        string? Draft(string key) { var row = _settings.FirstOrDefault(r => r.Spec.Key == key); return row is not null && (row.Original is not null || row.Dirty) ? row.Value : null; }
        string? fsr = Draft("setting.videocfg_fsr_detail");
        _videoError = fsr is not null && fsr != "0" && (Draft("setting.msaa_samples") != "0" || Draft("setting.r_csgo_cmaa_enable") != "0") ? "启用 FSR 前请关闭 MSAA 和 CMAA。" : null;
        if (_videoError is not null) invalid++;
        DirtyText.Text = $"未保存修改：{DirtyCount} 项" + (invalid > 0 ? $" · {invalid} 项无效 {_videoError}" : "");
        SaveButton.IsEnabled = !_busy && _snapshot is not null && DirtyCount > 0 && invalid == 0;
        UndoButton.IsEnabled = !_busy && DirtyCount > 0;
        ApplyButton.IsEnabled = !_busy && _snapshot is not null && DirtyCount == 0;
    }
    private void NameChanged(TextBox sender, TextBoxTextChangingEventArgs args) { if (!_syncing && DirtyText is not null) UpdateActions(); }
    private async Task LoadTemplateAsync()
    {
        Busy(true);
        try
        {
            _templates ??= new UnifiedConfigStore(UnifiedConfigStore.DefaultRoot);
            var snapshot = await Task.Run(_templates.LoadOrCreate);
            _modes = await Task.Run(DisplayModeService.Enumerate);
            SetSnapshot(snapshot);
            Status("已读取独立统一模板。可在未安装 Steam 或尚无账号时编辑；保存不会修改账号文件。");
        }
        catch (Exception ex) { Status(ex.Message, InfoBarSeverity.Error); }
        finally { Busy(false); }
    }
    private void SetSnapshot(TemplateSnapshot snapshot)
    {
        _syncing = true;
        try
        {
            _snapshot = _working = snapshot; _settings = UnifiedConfigStore.GetSettings(snapshot); _bindings.Clear(); _resolutionDirty = false;
            TemplateNameBox.Text = snapshot.Manifest.Name;
            DisplayBox.Items.Clear();
            foreach (var device in _modes.DistinctBy(m => m.Device)) DisplayBox.Items.Add(new ComboBoxItem { Content = device.DisplayName + " · " + device.Device, Tag = device.Device });
            if (DisplayBox.Items.Count > 0) DisplayBox.SelectedIndex = 0;
            PopulateResolutions();
        }
        finally { _syncing = false; }
        Render();
    }
    private async Task<bool> ConfirmReplaceAsync()
    {
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "替换统一模板？", Content = "当前已保存模板会先备份。未保存的修改将丢弃，Steam 账号文件不会改变。", PrimaryButtonText = "替换模板", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
    private async Task ImportAsync(Func<Task<TemplateSnapshot?>> load)
    {
        if (_busy || _snapshot is null || !await ConfirmReplaceAsync()) return;
        Busy(true);
        try
        {
            var imported = await load();
            if (imported is null) return;
            string name = TemplateNameBox.Text;
            var saved = await Task.Run(() => _templates!.Replace(_snapshot, imported, name));
            SetSnapshot(saved); Status("模板已独立保存，原账号没有修改。原模板已备份。", InfoBarSeverity.Success);
        }
        catch (Exception ex) { Status(ex.Message, InfoBarSeverity.Error); }
        finally { Busy(false); }
    }
    private async void DefaultClicked(object sender, RoutedEventArgs e) => await ImportAsync(() => Task.FromResult<TemplateSnapshot?>(UnifiedConfigStore.DefaultTemplate()));
    private async Task<string?> PickFolderAsync()
    {
        var picker = new FolderPicker(); picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, MainWindow.Hwnd);
        return (await picker.PickSingleFolderAsync())?.Path;
    }
    private async void ImportFolderClicked(object sender, RoutedEventArgs e) => await ImportAsync(async () =>
    {
        string? folder = await PickFolderAsync();
        return folder is null ? null : await Task.Run(() => _templates!.ReadFolder(folder));
    });
    private async Task<string> SteamRootAsync() => _steamOverride ?? await Task.Run(SteamPathCoordinator.TryResolveInstallPath)
        ?? throw new InvalidOperationException("未找到 Steam。模板可以继续编辑；导入账号或应用前请在设置中选择 Steam 安装目录。");
    private async void ImportAccountClicked(object sender, RoutedEventArgs e) => await ImportAsync(async () =>
    {
        string root = await SteamRootAsync();
        var store = new LocalConfigStore(root, Path.Combine(AppState.SettingsService.AppFolderPath, "cs2-settings-backups"));
        var profiles = await Task.Run(store.GetProfiles);
        if (profiles.Count == 0) throw new InvalidOperationException("没有可用于一次性导入的本机 CS2 配置。");
        var box = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var profile in profiles) box.Items.Add(new ComboBoxItem { Content = profile.Display, Tag = profile });
        box.SelectedIndex = 0;
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "仅导入一次，不持续绑定账号", Content = box, PrimaryButtonText = "导入", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return null;
        var selected = (LocalProfile)((ComboBoxItem)box.SelectedItem).Tag;
        return await Task.Run(() => _templates!.FromProfile(store, selected));
    });
    private async void ExportClicked(object sender, RoutedEventArgs e)
    {
        if (_busy || _snapshot is null) return;
        if (DirtyCount > 0) { Status("请先保存配置，再导出完整模板。", InfoBarSeverity.Warning); return; }
        Busy(true);
        try { var folder = await PickFolderAsync(); if (folder is not null) { await Task.Run(() => _templates!.Export(_snapshot, folder)); Status("完整模板已导出：" + folder, InfoBarSeverity.Success); } }
        catch (Exception ex) { Status(ex.Message, InfoBarSeverity.Error); }
        finally { Busy(false); }
    }
    private void SearchChanged(object sender, TextChangedEventArgs e) { if (RowsPanel is not null) Render(); }
    private void Render()
    {
        if (RowsPanel is null) return;
        foreach (var button in TabsPanel.Children.OfType<ToggleButton>()) button.IsChecked = button.Tag?.ToString() == _category;
        CategoryTitle.Text = _category;
        DisplayPanel.Visibility = _category == "视频" ? Visibility.Visible : Visibility.Collapsed;
        BindingsPanel.Visibility = _category == "键位绑定" ? Visibility.Visible : Visibility.Collapsed;
        RowsPanel.Children.Clear(); string query = SearchBox.Text.Trim(); string group = "";
        foreach (var row in _settings.Where(r => r.Spec.Category == _category && (query.Length == 0 ||
            string.Join(' ', r.Spec.Label, r.Spec.EnglishName, r.Key, r.Spec.Description).Contains(query, StringComparison.OrdinalIgnoreCase))))
        {
            string nextGroup = _category == "视频" ? row.Spec.Key.StartsWith("setting.") || row.Spec.Key == "r_player_visibility_mode" ? "高级视频与支持状态" : "基础视频" : _category;
            if (group != nextGroup) { RowsPanel.Children.Add(new TextBlock { Text = nextGroup, FontSize = 17, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(4, 12, 0, 6) }); group = nextGroup; }
            RowsPanel.Children.Add(CreateRow(row));
        }
        EmptyText.Visibility = RowsPanel.Children.Count == 0 && _category != "键位绑定" ? Visibility.Visible : Visibility.Collapsed;
        RenderBinding(); UpdateActions();
    }
    private FrameworkElement CreateRow(TemplateSetting row)
    {
        var spec = row.Spec;
        var grid = new Grid { Padding = new Thickness(16, 12, 16, 12), ColumnSpacing = 16, MinHeight = 78, Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"], CornerRadius = new CornerRadius(4) };
        grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new() { Width = new GridLength(250) });
        var label = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        label.Children.Add(new TextBlock { Text = spec.Label, FontSize = 15, TextWrapping = TextWrapping.Wrap });
        label.Children.Add(new TextBlock { Text = $"当前：{row.Original ?? "未配置"} · 模板参考：{spec.DefaultValue ?? "无"}", FontSize = 12, Opacity = .7, TextWrapping = TextWrapping.Wrap });
        label.Children.Add(new TextBlock { Text = row.Issue ?? spec.Description, FontSize = 12, Opacity = .7, TextWrapping = TextWrapping.Wrap });
        ToolTipService.SetToolTip(label, $"{spec.EnglishName}\n{row.Key}\n{spec.FileName} / {spec.Section}\n{spec.Evidence}\n{spec.Compatibility}\n{spec.DefaultOrigin}");
        grid.Children.Add(label);
        var error = new TextBlock { Text = row.Error ?? "", TextWrapping = TextWrapping.Wrap, Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"], FontSize = 12 };
        void Change(string value)
        {
            row.Value = value;
            try { row.Dirty = row.Original is null || row.Validated() != row.Original; row.Error = null; }
            catch (InvalidDataException ex) { row.Dirty = true; row.Error = ex.Message; }
            error.Text = row.Error ?? ""; UpdateActions();
        }
        FrameworkElement control;
        if (!row.Enabled) control = new TextBlock { Text = $"{row.Original ?? "未配置"}\n{(spec.Availability == SettingAvailability.HardwareDependent ? "依赖硬件" : spec.Availability == SettingAvailability.Unsupported ? "当前版本不支持" : "尚待验证")}", TextWrapping = TextWrapping.Wrap, Opacity = .65 };
        else if (spec.Boolean && (row.Original is null || row.Original is "0" or "1" or "true" or "false"))
        {
            var toggle = new ToggleSwitch { IsOn = row.Value == "true", OnContent = "开启", OffContent = "关闭" };
            toggle.Toggled += (_, _) => Change(toggle.IsOn ? "true" : "false"); control = toggle;
        }
        else if (spec.Choices is not null || spec.Boolean)
        {
            var combo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
            var choices = spec.Choices ?? [new("true", "开启"), new("false", "关闭")];
            foreach (var choice in choices) combo.Items.Add(new ComboBoxItem { Content = choice.Label, Tag = choice.Value });
            combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == row.Value);
            if (combo.SelectedItem is null || spec.Boolean && row.Original is not null && !row.Dirty)
            {
                var keep = new ComboBoxItem { Content = "原值：" + row.Original + "（保留）", Tag = "#keep" }; combo.Items.Add(keep); combo.SelectedItem = keep;
            }
            combo.SelectionChanged += (_, _) => { if (combo.SelectedItem is ComboBoxItem item && (string)item.Tag != "#keep") Change((string)item.Tag); }; control = combo;
        }
        else
        {
            var input = new TextBox { Text = row.Value, Description = $"{spec.Min.ToString(CultureInfo.InvariantCulture)} – {spec.Max.ToString(CultureInfo.InvariantCulture)}", Tag = row.Key };
            input.TextChanging += (_, _) => Change(input.Text); control = input;
            if (spec.Max <= 32 && double.TryParse(row.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) && number >= spec.Min && number <= spec.Max)
            {
                var slider = new Slider { Minimum = spec.Min, Maximum = spec.Max, StepFrequency = spec.Step, Value = number };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(slider, spec.Label + "滑块");
                slider.ValueChanged += (_, _) => input.Text = slider.Value.ToString("0.######", CultureInfo.InvariantCulture);
                var pair = new StackPanel { Spacing = 4 }; pair.Children.Add(slider); pair.Children.Add(input); control = pair;
            }
        }
        control.Tag = row.Key; Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(control, spec.Label);
        var editor = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        editor.Children.Add(control); editor.Children.Add(error); Grid.SetColumn(editor, 1); grid.Children.Add(editor); return grid;
    }
    private void KeyChanged(object sender, SelectionChangedEventArgs e) { if (!_bindingSelection) RenderBinding(); }
    private void RenderBinding()
    {
        if (ActionBox is null) return;
        _bindingSelection = true;
        try
        {
            ActionBox.Items.Clear(); if (KeyBox.SelectedItem is not string key || _working is null) return;
            var document = new ConfigText(_working.Files.GetValueOrDefault(LocalConfigStore.KeysFile) ?? UnifiedConfigStore.EmptyFile(LocalConfigStore.KeysFile));
            var original = document.Entries.SingleOrDefault(e => e.Section == "config/bindings" && e.Key.Equals(key, StringComparison.OrdinalIgnoreCase))?.Value;
            var keep = new ComboBoxItem { Content = "保留原绑定", Tag = "#keep" }; ActionBox.Items.Add(keep);
            foreach (var action in SettingCatalog.VerifiedBindActions) ActionBox.Items.Add(new ComboBoxItem { Content = action.Label, Tag = action.Value });
            ActionBox.SelectedItem = _bindings.TryGetValue(key, out var value) ? ActionBox.Items.OfType<ComboBoxItem>().First(i => (string)i.Tag == value) : keep;
            BindingSummary.Text = "模板当前绑定：" + (original ?? "未配置") + $" · 待保存 {_bindings.Count} 项";
        }
        finally { _bindingSelection = false; }
    }
    private void ActionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_bindingSelection || KeyBox.SelectedItem is not string key || ActionBox.SelectedItem is not ComboBoxItem item) return;
        string value = (string)item.Tag;
        if (value == "#keep") _bindings.Remove(key); else _bindings[key] = value;
        RenderBinding(); UpdateActions();
    }
    private void UndoClicked(object sender, RoutedEventArgs e) { if (!_busy && _snapshot is not null) SetSnapshot(_snapshot); }
    private async void SaveClicked(object sender, RoutedEventArgs e)
    {
        if (_busy || _working is null || _templates is null) return;
        Busy(true);
        string name = TemplateNameBox.Text;
        try { var saved = await Task.Run(() => _templates.Save(_working, _settings, _bindings, name)); SetSnapshot(saved); Status("统一模板已保存，账号文件未修改，也未推送 Steam Cloud。", InfoBarSeverity.Success); }
        catch (Exception ex) { Status(ex.Message, InfoBarSeverity.Error); }
        finally { Busy(false); }
    }
    private void DisplayChanged(object sender, SelectionChangedEventArgs e) { if (!_syncing) PopulateResolutions(); }
    private void PopulateResolutions()
    {
        bool previous = _syncing; _syncing = true;
        try
        {
            ResolutionBox.Items.Clear();
            if (DisplayBox.SelectedItem is not ComboBoxItem device) return;
            foreach (var mode in _modes.Where(m => m.Device == (string)device.Tag).DistinctBy(m => (m.Width, m.Height)))
                ResolutionBox.Items.Add(new ComboBoxItem { Content = $"{mode.Width} × {mode.Height}", Tag = mode });
        }
        finally { _syncing = previous; }
    }
    private void ResolutionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || _working is null || ResolutionBox.SelectedItem is not ComboBoxItem item) return;
        try
        {
            var mode = (DisplayMode)item.Tag;
            // Re-enumerate before accepting a mode; never persist a stale device index.
            var live = DisplayModeService.Enumerate();
            _working = DisplayModeService.ChangeResolution(_working, mode.Device, mode.Width, mode.Height, live);
            _resolutionDirty = true; UpdateActions();
        }
        catch (Exception ex) { Status(ex.Message, InfoBarSeverity.Warning); }
    }
    private async Task<bool> ConfirmPlanAsync(ApplyPreview preview)
    {
        string details = $"检测到账号：{preview.Accounts.Count}\n" + string.Join("\n", preview.Accounts.Select(a => $"账号 {a.AccountId}：" +
            (a.Error ?? (a.After.Count == 0 ? "无需写入" : string.Join("、", a.After.Keys))) + (a.Notes.Count > 0 ? "\n" + string.Join("；", a.Notes) : ""))) +
            "\n\n覆盖前会逐账号自动备份。仅应用已保存模板；不推送 Steam Cloud，Steam Cloud 后续可能同步回旧配置。";
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "确认应用本地配置？", Content = new ScrollViewer { MaxHeight = 420, Content = new TextBlock { Text = details, TextWrapping = TextWrapping.Wrap } }, PrimaryButtonText = "确认应用", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
    private async Task ShowResultAsync(BatchApplyResult result)
    {
        string message = $"检测到账号：{result.Accounts.Count}\n应用成功：{result.Succeeded}\n无需更改：{result.Unchanged}\n跳过：{result.Skipped}\n应用失败：{result.Failed}\n" +
            string.Join("\n\n", result.Accounts.Select(a => $"账号 {a.AccountId}：{a.Status}\n{a.Error}\n" + string.Join("；", a.Notes) + (a.Backup is null ? "" : "\n备份位置：" + a.Backup))) +
            (result.LogError is null ? "" : "\n" + result.LogError);
        Status($"本地配置处理完成：成功 {result.Succeeded}，无需更改 {result.Unchanged}，跳过 {result.Skipped}，失败 {result.Failed}。未进行云同步。", result.Failed == 0 ? InfoBarSeverity.Success : InfoBarSeverity.Warning);
        await new ContentDialog { XamlRoot = XamlRoot, Title = "CS2 统一配置应用结果", Content = new ScrollViewer { MaxHeight = 460, Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true } }, CloseButtonText = "关闭" }.ShowAsync();
    }
    private async void ApplyClicked(object sender, RoutedEventArgs e)
    {
        if (_busy || _snapshot is null) return;
        if (DirtyCount > 0) { Status("请先保存配置，再应用到所有账号。", InfoBarSeverity.Warning); return; }
        Busy(true);
        try
        {
            string root = await SteamRootAsync();
            var service = new ConfigApplyService(root, Path.Combine(_templates!.BackupRoot, "accounts"));
            var current = await Task.Run(_templates.Load);
            if (current.ManifestHash != _snapshot.ManifestHash) throw new IOException("模板已被外部修改，请重新打开或恢复模板后再应用。");
            var preview = await Task.Run(() => service.Preview(current));
            if (preview.Accounts.Count == 0) { Status("未检测到包含 CS2 数据的账号；模板已保留，可稍后应用。"); return; }
            if (!await ConfirmPlanAsync(preview)) return;
            if ((await Task.Run(_templates.Load)).ManifestHash != current.ManifestHash) throw new IOException("确认期间模板已变化，请重新预览。");
            var progress = new Progress<string>(message => Status(message));
            var result = await Task.Run(() => service.Apply(preview, progress: progress));
            await ShowResultAsync(result);
        }
        catch (Exception ex) { Status(ex.Message, InfoBarSeverity.Error); }
        finally { Busy(false); }
    }
    private sealed record RestoreOption(string Kind, string Directory, string Label);
    private async void RestoreClicked(object sender, RoutedEventArgs e)
    {
        if (_busy || _snapshot is null || _templates is null) return;
        Busy(true);
        try
        {
            var options = (await Task.Run(_templates.TemplateBackups)).Select(b => new RestoreOption("template", b.Directory, "模板备份 · " + b.Name)).ToList();
            string? root = _steamOverride ?? await Task.Run(SteamPathCoordinator.TryResolveInstallPath);
            ConfigApplyService? service = root is null ? null : new(root, Path.Combine(_templates.BackupRoot, "accounts"));
            if (service is not null) options.AddRange((await Task.Run(service.AccountBackups)).Select(b => new RestoreOption("account", b.Directory, b.Name)));
            string legacy = Path.Combine(AppState.SettingsService.AppFolderPath, "cs2-settings-backups");
            SafeConfigIO.RejectLinks(legacy);
            if (root is not null && Directory.Exists(legacy)) foreach (string account in Directory.GetDirectories(legacy))
            {
                SafeConfigIO.RejectLinks(account);
                foreach (string backup in Directory.GetDirectories(account))
                { SafeConfigIO.RejectLinks(backup); if (File.Exists(Path.Combine(backup, "manifest.json"))) options.Add(new("legacy", backup, "旧版账号备份 · " + Path.GetFileName(account) + " · " + Path.GetFileName(backup))); }
            }
            if (options.Count == 0) { Status("暂无可恢复的模板或账号备份。"); return; }
            var box = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
            foreach (var option in options) box.Items.Add(new ComboBoxItem { Content = option.Label, Tag = option }); box.SelectedIndex = 0;
            var content = new StackPanel { Spacing = 10 }; content.Children.Add(new TextBlock { Text = "恢复模板会丢弃未保存修改，并备份当前模板；恢复账号只操作选中备份中的本地文件，不推送云端。", TextWrapping = TextWrapping.Wrap }); content.Children.Add(box);
            var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "恢复配置备份", Content = content, PrimaryButtonText = "恢复", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            var selected = (RestoreOption)((ComboBoxItem)box.SelectedItem).Tag;
            if (selected.Kind == "template") { SetSnapshot(await Task.Run(() => _templates.RestoreTemplate(_snapshot, selected.Directory))); Status("模板已恢复，账号配置未改变。", InfoBarSeverity.Success); }
            else if (selected.Kind == "account")
            { var preview = await Task.Run(() => service!.PreviewRestore(selected.Directory)); if (await ConfirmPlanAsync(preview)) await ShowResultAsync(await Task.Run(() => service!.Apply(preview))); }
            else
            {
                uint id = uint.Parse(Path.GetFileName(Path.GetDirectoryName(selected.Directory)!), CultureInfo.InvariantCulture);
                var store = new LocalConfigStore(root!, legacy);
                var profile = new LocalProfile(id, Path.Combine(root!, "userdata", id.ToString(CultureInfo.InvariantCulture), "730", "local", "cfg"));
                await Task.Run(() => store.Restore(store.Load(profile), selected.Directory)); Status("旧版本地备份已恢复，恢复前文件已备份。未推送云端。", InfoBarSeverity.Success);
            }
        }
        catch (Exception ex) { Status(ex.Message, InfoBarSeverity.Error); }
        finally { Busy(false); }
    }
}
