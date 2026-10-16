using System.Globalization;
using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using SteamEyaWinUI.Cs2Settings;
using SteamEyaWinUI.Services;

namespace SteamEyaWinUI.Pages;

public sealed partial class Cs2LocalSettingsPage : Page
{
    private LocalConfigStore? _store;
    private ConfigSnapshot? _snapshot;
    private List<SettingValue> _settings = [];
    private readonly Dictionary<string,string> _bindings = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<LocalProfile> _profiles = [];
    private string _category = "画面";
    private bool _busy, _selecting, _bindingSelection;
    private int _selectedIndex = -1;
    private string? _backup;
    private int DirtyCount => _settings.Count(s=>s.Dirty) + _bindings.Count;
#if MANAGER_UI_CHECKS
    internal Func<IReadOnlyList<Cs2CfgFile>,string,Cs2CloudPushResult>? TestPush;
    internal bool IsBusyForCheck => _busy;
    internal void SelectCategoryForCheck(string category) { _category=category;Render(); }
    internal Task PrepareForCheckAsync(LocalConfigStore store) { _store=store; return ReloadAsync(); }
#endif
    public Cs2LocalSettingsPage()
    {
        InitializeComponent(); NavigationCacheMode = NavigationCacheMode.Required;
        foreach (var category in new[] { "画面", "音频", "游戏 / HUD", "准星", "鼠标键盘" })
        {
            var button = new ToggleButton { Content=category, Tag=category, MinWidth=72, Padding=new Thickness(14,9,14,9) };
            button.Click += (_,_) => { _category=category; Render(); };
            TabsPanel.Children.Add(button);
        }
        foreach (var key in SettingCatalog.BindKeys) KeyBox.Items.Add(key);
        Loaded += async (_,_) =>
        {
#if MANAGER_UI_CHECKS
            if (ManagerUiChecks.Enabled) return;
#endif
            if (_store is null) await ReloadAsync();
        };
        Render();
    }
    private void Status(string text, InfoBarSeverity severity=InfoBarSeverity.Informational)
    { StatusBar.Message=text; StatusBar.Severity=severity; StatusBar.IsOpen=true; }
    private void SetBusy(bool busy)
    {
        _busy=busy; BusyRing.IsActive=busy;
        ProfilesBox.IsEnabled=!busy; ReloadButton.IsEnabled=!busy; SettingsScroll.IsEnabled=!busy;
        SearchBox.IsEnabled=!busy;
        foreach(var tab in TabsPanel.Children.OfType<ToggleButton>()) tab.IsEnabled=!busy;
        UpdateActions();
    }
    private void UpdateActions()
    {
        DirtyText.Text=DirtyCount==0 ? "没有未保存修改" : $"{DirtyCount} 项修改尚未保存";
        SaveButton.IsEnabled=!_busy && DirtyCount>0; UndoButton.IsEnabled=!_busy && DirtyCount>0;
        RestoreButton.IsEnabled=!_busy && _snapshot is not null && _backup is not null;
    }
    private async Task<bool> ConfirmDiscardAsync()
    {
        if (DirtyCount==0) return true;
        var dialog=new ContentDialog { XamlRoot=XamlRoot, Title="放弃未保存的修改？", Content="切换账号或重新读取会丢弃当前编辑内容。", PrimaryButtonText="放弃修改", CloseButtonText="继续编辑", DefaultButton=ContentDialogButton.Close };
        return await dialog.ShowAsync()==ContentDialogResult.Primary;
    }
    private async void ReloadClicked(object sender,RoutedEventArgs e)
    { if (!_busy && await ConfirmDiscardAsync()) await ReloadAsync(); }
    private async Task ReloadAsync()
    {
        SetBusy(true);
        try
        {
            bool resolveSteam=true;
#if MANAGER_UI_CHECKS
            if(ManagerUiChecks.Enabled) resolveSteam=false;
#endif
            if (resolveSteam)
            {
                var path=await Task.Run(SteamPathCoordinator.TryResolveInstallPath);
                if (path is null) throw new InvalidOperationException("未找到 Steam。请先在设置页选择 Steam 安装目录。");
                _store=new LocalConfigStore(path,Path.Combine(AppState.SettingsService.AppFolderPath,"cs2-settings-backups"));
            }
            var previous=_snapshot?.Profile.AccountId;
            var store=_store ?? throw new InvalidOperationException("未设置本地配置目录。");
            var profiles=await Task.Run(store.GetProfiles);
            foreach (var profile in profiles)
            {
                var name=AppState.HistoryAccounts.FirstOrDefault(a=>a.SteamId==profile.SteamId)?.PersonaName;
                if (!string.IsNullOrWhiteSpace(name)) profile.Display=name+" · "+profile.SteamId;
            }
            _selecting=true; _profiles.Clear(); _profiles.AddRange(profiles); ProfilesBox.Items.Clear();
            foreach(var profile in profiles) ProfilesBox.Items.Add(profile.Display);
            _selectedIndex=profiles.FindIndex(p=>p.AccountId==previous);
            if (_selectedIndex<0 && profiles.Count>0) _selectedIndex=0;
            ProfilesBox.SelectedIndex=_selectedIndex; _selecting=false;
            _snapshot=null; _settings=[]; _bindings.Clear(); _backup=null;
            if (_selectedIndex>=0) await LoadSelectedAsync();
            else Status("未找到已有 CS2 配置。请先在本机为此 Steam 账号运行一次游戏，然后关闭游戏再重新读取。");
        }
        catch(Exception ex) { Status(ex.Message,InfoBarSeverity.Error); }
        finally { _selecting=false; SetBusy(false); Render(); }
    }
    private async void ProfileChanged(object sender,SelectionChangedEventArgs e)
    {
        if (_selecting || _busy || ProfilesBox.SelectedIndex==_selectedIndex) return;
        int next=ProfilesBox.SelectedIndex;
        _selecting=true; ProfilesBox.SelectedIndex=_selectedIndex;
        try
        {
            if (!await ConfirmDiscardAsync()) return;
            SetBusy(true); _selectedIndex=next; ProfilesBox.SelectedIndex=next;
            _snapshot=null; _settings=[]; _bindings.Clear(); _backup=null;
            if(next>=0) await LoadSelectedAsync();
        }
        catch(Exception ex) { Status(ex.Message,InfoBarSeverity.Error); }
        finally { _selecting=false; SetBusy(false); Render(); }
    }
    private async Task LoadSelectedAsync()
    {
        var profile=_profiles[_selectedIndex];
        var snapshot=await Task.Run(()=>_store!.Load(profile));
        var rows=await Task.Run(()=>LocalConfigStore.GetSettings(snapshot));
        _backup=await Task.Run(()=>_store!.LatestBackup(profile));
        ToolTipService.SetToolTip(RestoreButton,_backup ?? "此账号暂无本地配置备份");
        _snapshot=snapshot; _settings=rows; _bindings.Clear();
        Status($"已读取 {snapshot.Files.Count} 个配置文件。仅编辑此账号已有的 slot0 配置；保存后自动尝试同步用户设置和键位。Steam 需登录同一账号。");
    }
    private void SearchChanged(object sender,TextChangedEventArgs e) { if (RowsPanel is not null) Render(); }
    private void Render()
    {
        if(RowsPanel is null) return;
        foreach(var button in TabsPanel.Children.OfType<ToggleButton>()) button.IsChecked=button.Tag?.ToString()==_category;
        CategoryTitle.Text=_category;
        CategoryHelp.Text=_category switch
        {
            "画面" => "调整分辨率、抗锯齿和帧率。画面与机器配置仅保存到本机；本页不会切换显示模式或覆盖游戏的自动检测结果。",
            "音频" => "音量取值 0–1。按现有文件显示可编辑项目，未生成的设置不会补入配置。",
            "游戏 / HUD" => "调整雷达、界面大小和持枪视角。仅改动选中的项目，其他游戏设置完整保留。",
            "准星" => "调整现有准星参数。新版与经典参数分别保留；实际效果取决于游戏当前准星样式。",
            _ => "调整鼠标灵敏度与按键。选择动作将覆盖该按键的现有绑定，保存前可随时撤销。"
        };
        RowsPanel.Children.Clear();
        string search=SearchBox.Text.Trim();
        foreach (var row in _settings.Where(r=>r.Spec.Category==_category && (search.Length==0 || r.Spec.Label.Contains(search,StringComparison.OrdinalIgnoreCase) || r.Key.Contains(search,StringComparison.OrdinalIgnoreCase))))
            RowsPanel.Children.Add(CreateRow(row));
        EmptyText.Visibility=RowsPanel.Children.Count==0 ? Visibility.Visible : Visibility.Collapsed;
        BindingsPanel.Visibility=_category=="鼠标键盘" && _snapshot?.Files.ContainsKey(LocalConfigStore.KeysFile)==true ? Visibility.Visible : Visibility.Collapsed;
        RenderBinding(); UpdateActions();
    }
    private FrameworkElement CreateRow(SettingValue row)
    {
        var grid=new Grid { Padding=new Thickness(18,12,18,12), ColumnSpacing=18, MinHeight=68, Background=(Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"], CornerRadius=new CornerRadius(4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(1,GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(260) });
        var label=new StackPanel { Spacing=4, VerticalAlignment=VerticalAlignment.Center };
        label.Children.Add(new TextBlock { Text=row.Spec.Label, FontSize=15, TextWrapping=TextWrapping.Wrap });
        label.Children.Add(new TextBlock { Text=row.File==LocalConfigStore.UserFile ? "用户设置 · 自动尝试云同步" : "本机设置", FontSize=12, Opacity=.6 });
        ToolTipService.SetToolTip(label,row.Key+"\n"+row.File); grid.Children.Add(label);
        FrameworkElement control;
        if(row.Spec.Boolean && row.Original is not "0" and not "1" and not "true" and not "false")
            control=new TextBlock { Text="当前值："+row.Original+"（保留）",TextWrapping=TextWrapping.Wrap,HorizontalAlignment=HorizontalAlignment.Right };
        else if(row.Spec.Boolean)
        {
            var toggle=new ToggleSwitch { IsOn=row.Value=="true", OnContent="开启", OffContent="关闭", HorizontalAlignment=HorizontalAlignment.Right };
            toggle.Toggled+=(_,_)=>Change(row,toggle.IsOn?"true":"false"); control=toggle;
        }
        else if(row.Spec.Choices is {} choices)
        {
            var combo=new ComboBox { HorizontalAlignment=HorizontalAlignment.Stretch };
            foreach(var choice in choices) combo.Items.Add(new ComboBoxItem { Content=choice.Label, Tag=choice.Value });
            combo.SelectedItem=combo.Items.OfType<ComboBoxItem>().FirstOrDefault(i=>i.Tag?.ToString()==row.Value);
            if(combo.SelectedIndex<0) { var current=new ComboBoxItem { Content="当前值："+row.Value+"（保留）",Tag=row.Value };combo.Items.Add(current);combo.SelectedItem=current; }
            combo.SelectionChanged+=(_,_)=> { if(combo.SelectedItem is ComboBoxItem item) Change(row,(string)item.Tag); }; control=combo;
        }
        else
        {
            // TextBox preserves unknown/out-of-range originals until the user explicitly edits them.
            var input=new TextBox { Text=row.Value, HorizontalAlignment=HorizontalAlignment.Stretch, Description=$"{row.Spec.Min.ToString(CultureInfo.InvariantCulture)} – {row.Spec.Max.ToString(CultureInfo.InvariantCulture)}" };
            input.Tag=row.Key;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(input,row.Spec.Label);
            if(row.Spec.Max<=255 && double.TryParse(row.Value,NumberStyles.Float,CultureInfo.InvariantCulture,out var number) && double.IsFinite(number) && number>=row.Spec.Min && number<=row.Spec.Max)
            {
                var pair=new Grid { ColumnSpacing=12 };
                pair.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(1,GridUnitType.Star) });
                pair.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(90) });
                var slider=new Slider { Minimum=row.Spec.Min,Maximum=row.Spec.Max,StepFrequency=row.Spec.Step,Value=number,VerticalAlignment=VerticalAlignment.Center };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(slider,row.Spec.Label+"滑块");
                bool updating=false;
                slider.ValueChanged+=(_,_)=>{ if(updating)return;updating=true;input.Text=slider.Value.ToString("0.######",CultureInfo.InvariantCulture);updating=false; };
                input.TextChanging+=(_,_)=> { Change(row,input.Text);if(updating)return;updating=true;if(double.TryParse(input.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out var v) && double.IsFinite(v))slider.Value=Math.Clamp(v,row.Spec.Min,row.Spec.Max);updating=false; };
                pair.Children.Add(slider); Grid.SetColumn(input,1);pair.Children.Add(input);control=pair;
            }
            else { input.TextChanging+=(_,_)=>Change(row,input.Text);control=input; }
        }
        control.Tag=row.Key;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(control,row.Spec.Label);
        control.VerticalAlignment=VerticalAlignment.Center; Grid.SetColumn(control,1); grid.Children.Add(control);return grid;
    }
    private void Change(SettingValue row,string value)
    {
        row.Value=value;
        try { row.Dirty=row.Spec.Validate(value,row.Original)!=row.Original; }
        catch(InvalidDataException) { row.Dirty=true; }
        UpdateActions();
    }
    private void KeyChanged(object sender,SelectionChangedEventArgs e) { if(!_bindingSelection) RenderBinding(); }
    private void RenderBinding()
    {
        _bindingSelection=true;
        try
        {
            ActionBox.Items.Clear();
            if(KeyBox.SelectedItem is not string key || _snapshot is null || !_snapshot.Files.TryGetValue(LocalConfigStore.KeysFile,out var file)) return;
            var existing=file.Document.Entries.Where(e=>e.Section=="config/bindings" && e.Key.Equals(key,StringComparison.OrdinalIgnoreCase)).ToArray();
            if(existing.Length>1) { BindingSummary.Text="此键存在重复定义，保留原文件，请在游戏内修复。";ActionBox.IsEnabled=false;return; }
            ActionBox.IsEnabled=true;
            string? original=existing.SingleOrDefault()?.Value;
            var keep=new ComboBoxItem { Content=original is null ? "保持游戏默认绑定" : "保留现有绑定",Tag="keep" }; ActionBox.Items.Add(keep);
            foreach(var action in SettingCatalog.BindActions) ActionBox.Items.Add(new ComboBoxItem { Content=action.Label,Tag=action.Value });
            ActionBox.SelectedItem=_bindings.TryGetValue(key,out var pending) ? ActionBox.Items.OfType<ComboBoxItem>().First(i=>i.Tag?.ToString()==pending) : keep;
            var known=SettingCatalog.BindActions.FirstOrDefault(a=>a.Value==original)?.Label;
            BindingSummary.Text="当前绑定："+(original is null ? "游戏默认" : known ?? "自定义命令（未修改）")+"。"+(_bindings.Count>0?$"待保存 {_bindings.Count} 个键位。":"");
        }
        finally { _bindingSelection=false; }
    }
    private void ActionChanged(object sender,SelectionChangedEventArgs e)
    {
        if(_bindingSelection || KeyBox.SelectedItem is not string key || ActionBox.SelectedItem is not ComboBoxItem item) return;
        string value=(string)item.Tag;
        var original=_snapshot!.Files[LocalConfigStore.KeysFile].Document.Entries.FirstOrDefault(e=>e.Section=="config/bindings" && e.Key.Equals(key,StringComparison.OrdinalIgnoreCase))?.Value;
        if(value=="keep" || value==original) _bindings.Remove(key); else _bindings[key]=value;
        UpdateActions(); RenderBinding();
    }
    private void UndoClicked(object sender,RoutedEventArgs e)
    { if(_snapshot is null || _busy) return; _settings=LocalConfigStore.GetSettings(_snapshot);_bindings.Clear();Render(); }
    private async void SaveClicked(object sender,RoutedEventArgs e)
    {
        if(_busy || _snapshot is null) return;
        SetBusy(true); bool saved=false;
        try
        {
            var profile=_snapshot.Profile;
            var result=await Task.Run(()=>_store!.Save(_snapshot,_settings,_bindings)); saved=true;
            await LoadSelectedAsync(); Render();
            Status("本地保存成功，正在尝试推送云端…");
            string cloud="本次修改仅需保存到本机。"; bool cloudOk=true;
            if(result.CloudFiles.Count>0)
            {
                var files=result.CloudFiles.Select(f=>new Cs2CfgFile(f.Name,f.Data)).ToArray();
                var pushed=await Task.Run(()=>Push(files,profile.SteamId));
                cloudOk=pushed.Ok && pushed.PartialFailed==0 && !pushed.AccountCloudDisabled;
                cloud=Cs2CloudService.DescribeResult(pushed);
            }
            if(result.CloudNote is not null) { cloudOk=false;cloud=result.CloudNote+"。 "+(result.CloudFiles.Count>0?cloud:""); }
            Status($"已保存 {result.FileCount} 个文件并自动备份。{cloud}",cloudOk?InfoBarSeverity.Success:InfoBarSeverity.Warning);
        }
        catch(Exception ex) { Status((saved?"本地已保存，后续读取或云同步未完成。":"保存未完成：")+ex.Message,InfoBarSeverity.Error); }
        finally { SetBusy(false); }
    }
    private Cs2CloudPushResult Push(IReadOnlyList<Cs2CfgFile> files,string steamId)
    {
#if MANAGER_UI_CHECKS
        if(ManagerUiChecks.Enabled) return TestPush?.Invoke(files,steamId) ?? new(false,0,"测试模式未设置云响应。");
#endif
        if(LocalConfigStore.GameRunning()) return new(false,0,"检测到 CS2 正在运行，未推送云端。");
        return AppState.Cs2CloudService.ForcePush(files,maxWaitSeconds:4,expectedSteamId64:steamId);
    }
    private async void RestoreClicked(object sender,RoutedEventArgs e)
    {
        if(_busy || _snapshot is null || _backup is null) return;
        var dialog=new ContentDialog { XamlRoot=XamlRoot,Title="恢复最近的本地备份？",Content="仅恢复这份备份包含的本地文件；当前文件会先备份。未保存的编辑将丢弃。此操作不会撤回已推送的云端设置。",PrimaryButtonText="恢复本地文件",CloseButtonText="取消",DefaultButton=ContentDialogButton.Close };
        if(await dialog.ShowAsync()!=ContentDialogResult.Primary) return;
        SetBusy(true);
        try { await Task.Run(()=>_store!.Restore(_snapshot,_backup)); await LoadSelectedAsync();Render();Status("本地备份已恢复；恢复前的文件已另行备份。云端设置未变更。",InfoBarSeverity.Success); }
        catch(Exception ex) { Status(ex.Message,InfoBarSeverity.Error); }
        finally { SetBusy(false); }
    }
}
