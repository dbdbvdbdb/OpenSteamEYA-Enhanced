#if MANAGER_UI_CHECKS
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SteamEyaWinUI.Cs2Settings;
using SteamEyaWinUI.Pages;

namespace SteamEyaWinUI.Services;

internal static partial class ManagerUiChecks
{
    private static async Task CheckCs2SettingsAsync(MainWindow window)
    {
        foreach (var bar in FindElements(window.Content).OfType<InfoBar>()) bar.IsOpen = false;
        string steam = Path.Combine(DataRoot, "unified-steam");
        var store = new UnifiedConfigStore(Path.Combine(DataRoot, "unified-template"));
        var nav = FindElements(window.Content).OfType<NavigationView>().Single();
        var previousLanguage = Localization.Loc.CurrentCode;
        try
        {
            foreach (var language in Localization.Loc.AvailablePacks)
            {
                Localization.Loc.SetLanguage(language.Code, persist: false);
                await Task.Delay(50);
                var items = nav.MenuItems.OfType<NavigationViewItem>().ToArray();
                Check(!items.Any(i => i.Tag?.ToString() == "cs2Settings"), "retired CS2 viewer absent in " + language.Code);
                Check(items.Count(i => i.Tag?.ToString() == "cs2LocalSettings") == 1,
                    "unified CS2 navigation retained in " + language.Code);
            }
        }
        finally { Localization.Loc.SetLanguage(previousLanguage, persist: false); }
        nav.SelectedItem = nav.MenuItems.OfType<NavigationViewItem>().Single(i => i.Tag?.ToString() == "cs2LocalSettings");
        await Task.Delay(200);
        var page = FindElements(window.Content).OfType<Cs2LocalSettingsPage>().Single();
        await page.PrepareForCheckAsync(store, steam);
        T Find<T>(string name) where T : FrameworkElement => FindElements(page).OfType<T>().Single(x => x.Name == name);
        TextBox Input(string key) => FindElements(page).OfType<TextBox>().Single(x => x.Tag?.ToString() == key);
        async Task Wait()
        {
            for (int i = 0; i < 250 && page.IsBusyForCheck; i++) await Task.Delay(20);
            Check(!page.IsBusyForCheck, "unified CS2 UI operation completes");
        }
        async Task Choose(string text)
        {
            for (int i = 0; i < 160; i++)
            {
                var button = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetOpenPopupsForXamlRoot(page.XamlRoot)
                    .SelectMany(p => FindElements(p.Child)).OfType<Button>().FirstOrDefault(b => b.Content?.ToString() == text);
                if (button is not null) { Press(button); await Task.Delay(100); return; }
                await Task.Delay(50);
            }
            throw new InvalidOperationException("Unified CS2 dialog did not appear: " + text);
        }
        async Task Tab(string name) { page.SelectCategoryForCheck(name); await Task.Delay(60); }
        Check(!FindElements(page).OfType<ComboBox>().Any(c => c.Name == "ProfilesBox"), "unified CS2 editor has no account selection");
        Check(!Directory.Exists(steam) && store.Load().Files.Count == 4, "unified CS2 template can be created without Steam accounts");
        Check(!Find<Button>("SaveButton").IsEnabled, "unified CS2 initial load has no unsaved changes");
        window.ApplyTheme(ElementTheme.Dark); await CaptureAsync(window, "cs2-unified-video-dark");
        var card = FindElements(page).OfType<Grid>().First(g => g.Tag?.ToString()?.StartsWith("setting-row:") == true);
        var background = card.Background as Microsoft.UI.Xaml.Media.SolidColorBrush;
        Check(background is not null && (background.Color.R * background.Color.A / 255.0 + 32 * (255 - background.Color.A) / 255.0) < 100,
            "unified CS2 cards resolve a dark theme background with readable contrast");
        var msaa = FindElements(page).OfType<ComboBox>().Single(c => c.Tag?.ToString() == "setting.msaa_samples");
        Check(msaa.Items.Count == 4, "unified CS2 MSAA uses the verified four menu choices");
        msaa.StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = 0, AnimationDesired = false }); await Task.Delay(250);
        await CaptureAsync(window, "cs2-unified-video-advanced-dark");
        var ao = FindElements(page).OfType<ComboBox>().Single(c => c.Tag?.ToString() == "setting.videocfg_ao_detail");
        Check(ao.Items.OfType<ComboBoxItem>().Select(i => i.Tag?.ToString()).SequenceEqual(new[] { "0", "2", "3" }), "unified CS2 AO control has only current menu values");

        foreach (string category in new[] { "音频", "游戏", "HUD", "准星", "鼠标键盘" })
        {
            await Tab(category);
            Check(Find<StackPanel>("RowsPanel").Children.Count > 0, "unified CS2 category " + category);
        }
        Input("sensitivity").Text = "2.25";
        Check(Find<Button>("SaveButton").IsEnabled && !Find<Button>("ApplyButton").IsEnabled, "unified CS2 separates dirty edits from apply");
        await Tab("视频"); await Tab("鼠标键盘");
        Check(Input("sensitivity").Text == "2.25", "unified CS2 category switch preserves draft");
        Input("sensitivity").Text = "NaN";
        Check(!Find<Button>("SaveButton").IsEnabled && Find<TextBlock>("DirtyText").Text.Contains("无效"), "unified CS2 invalid input disables save immediately");
        Input("sensitivity").Text = "2.25";
        await Tab("键位绑定");
        Find<ComboBox>("KeyBox").SelectedItem = "SPACE";
        var actions = Find<ComboBox>("ActionBox");
        actions.SelectedItem = actions.Items.OfType<ComboBoxItem>().Single(i => i.Tag?.ToString() == "+jump");
        Press(Find<Button>("SaveButton")); await Wait();
        Check(!Directory.Exists(steam), "unified CS2 save never creates or modifies Steam accounts");
        var saved = new UnifiedConfigStore(store.Root).Load();
        Check(new ConfigText(saved.Files[LocalConfigStore.UserFile]).Find("config/convars", "sensitivity")?.Value == "2.25", "unified CS2 save persists sensitivity across reload");
        Check(new ConfigText(saved.Files[LocalConfigStore.KeysFile]).Find("config/bindings", "SPACE")?.Value == "+jump", "unified CS2 save persists explicit binding");
        Check(!Find<Button>("SaveButton").IsEnabled && Find<Button>("ApplyButton").IsEnabled, "unified CS2 saved template is ready to apply");
        Press(Find<Button>("ApplyButton")); await Wait();
        Check(Find<InfoBar>("StatusBar").Message.Contains("未检测到"), "unified CS2 no-account application explains empty targets");
        string Cfg(uint id) => Path.Combine(steam, "userdata", id.ToString(), "730", "local", "cfg");
        foreach (uint id in new uint[] { 123, 456 })
        {
            Directory.CreateDirectory(Cfg(id));
            await File.WriteAllTextAsync(Path.Combine(Cfg(id), LocalConfigStore.UserFile), "// synthetic\nconfig { convars { sensitivity 1 unknown preserved } }");
        }
        var before = File.ReadAllBytes(Path.Combine(Cfg(123), LocalConfigStore.UserFile));
        Press(Find<Button>("ApplyButton")); await Choose("取消"); await Wait();
        Check(File.ReadAllBytes(Path.Combine(Cfg(123), LocalConfigStore.UserFile)).SequenceEqual(before), "unified CS2 confirmation cancellation writes nothing");
        Press(Find<Button>("ApplyButton")); await Choose("确认应用"); await Choose("关闭"); await Wait();
        Check(Find<InfoBar>("StatusBar").Severity == InfoBarSeverity.Success && Find<InfoBar>("StatusBar").Message.Contains("成功 2"), "unified CS2 confirmed bulk apply reports both accounts");
        foreach (uint id in new uint[] { 123, 456 })
        {
            var doc = new ConfigText(File.ReadAllBytes(Path.Combine(Cfg(id), LocalConfigStore.UserFile)));
            Check(doc.Find("config/convars", "sensitivity")?.Value == "2.25" && doc.Find("config/convars", "unknown")?.Value == "preserved", "unified CS2 applies same template without deleting unknown fields " + id);
        }
        var backups = new ConfigApplyService(steam, Path.Combine(store.BackupRoot, "accounts")).AccountBackups();
        Check(backups.Count == 2, "unified CS2 UI application creates independent account backups");
        await Tab("鼠标键盘"); Input("sensitivity").Text = "3";
        Press(Find<Button>("UndoButton"));
        Check(Input("sensitivity").Text == "2.25" && !Find<Button>("SaveButton").IsEnabled, "unified CS2 undo restores saved template");
        await Tab("视频");
        window.ApplyTheme(ElementTheme.Light); await CaptureAsync(window, "cs2-unified-video-light");
        await Tab("音频"); await CaptureAsync(window, "cs2-unified-audio-light");
        var scale = page.XamlRoot.RasterizationScale;
        window.AppWindow.Resize(new Windows.Graphics.SizeInt32((int)(1100 * scale), (int)(800 * scale))); await Task.Delay(200);
        var apply = Find<Button>("ApplyButton"); var p = apply.TransformToVisual(page).TransformPoint(new Windows.Foundation.Point());
        Check(p.X >= 0 && p.X + apply.ActualWidth <= page.ActualWidth + 1, "unified CS2 actions fit narrow window");
        await CaptureAsync(window, "cs2-unified-narrow-light");
        Find<TextBox>("SearchBox").Text = "主音量"; await Task.Delay(150);
        Check(FindElements(page).OfType<TextBox>().Count(t => t.Tag?.ToString() == "volume") == 1, "unified CS2 search retains matching editable parameter");
        Find<TextBox>("SearchBox").Text = ""; await Task.Delay(100);
        var partial = store.Load();
        partial = store.Replace(partial, partial with { Files = new Dictionary<string, byte[]> {
            [LocalConfigStore.UserFile] = UnifiedConfigStore.EmptyFile(LocalConfigStore.UserFile),
            [LocalConfigStore.VideoFile] = UnifiedConfigStore.EmptyFile(LocalConfigStore.VideoFile) } }, "缺失参数测试模板");
        await page.PrepareForCheckAsync(store, steam); await Tab("鼠标键盘");
        Check(!Find<Button>("SaveButton").IsEnabled, "unified CS2 rendering absent defaults does not silently add parameters");
        var addMissing = FindElements(page).OfType<Button>().Single(b => b.Tag?.ToString() == "add:sensitivity");
        Press(addMissing); Press(Find<Button>("SaveButton")); await Wait();
        var inserted = new ConfigText(store.Load().Files[LocalConfigStore.UserFile]);
        Check(inserted.Find("config/convars", "sensitivity")?.Value == "1.25" && inserted.Find("config/convars", "zoom_sensitivity_ratio") is null,
            "unified CS2 explicitly adds only the selected missing default parameter");
        Info("Unified CS2 UI uses isolated templates and synthetic account directories. No Steam session, credentials or cloud APIs were used. Physical monitor/driver and game-effect validation remains pending.");
    }
}
#endif
