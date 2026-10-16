#if MANAGER_UI_CHECKS
using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using SteamEyaWinUI.Cs2Settings;
using SteamEyaWinUI.Pages;

namespace SteamEyaWinUI.Services;

internal static partial class ManagerUiChecks
{
    private static async Task CheckCs2SettingsAsync(MainWindow window)
    {
        foreach(var info in FindElements(window.Content).OfType<InfoBar>())info.IsOpen=false;
        var steam=Path.Combine(DataRoot,"cs2-fixture");
        string Cfg(uint id)=>Path.Combine(steam,"userdata",id.ToString(),"730","local","cfg");
        Directory.CreateDirectory(Cfg(123)); Directory.CreateDirectory(Cfg(456));
        var remote=Path.Combine(steam,"userdata","123","730","remote");Directory.CreateDirectory(remote);
        await File.WriteAllTextAsync(Path.Combine(Cfg(123),LocalConfigStore.UserFile),"""
        "config" { "convars" {
          "sensitivity" "1.5" "zoom_sensitivity_ratio" "1" "cl_sniper_auto_rezoom" "true"
          "cl_crosshair_length" "3" "cl_crosshair_gap" "-2" "cl_crosshair_thickness" "1"
          "cl_crosshaircolor_r" "50" "cl_crosshaircolor_g" "255" "cl_crosshaircolor_b" "50"
          "cl_crosshairalpha" "255" "cl_crosshairdot" "false" "cl_crosshair_t" "false"
          "viewmodel_fov" "68" "cl_showloadout" "true" "cl_use_opens_buy_menu" "false"
          "unknown" "preserve-synthetic-value"
        } }
        """);
        await File.WriteAllTextAsync(Path.Combine(Cfg(123),LocalConfigStore.MachineFile),"""
        "config" { "convars" {
          "volume" "0.8" "snd_gamevolume" "1" "snd_voipvolume" "0.7" "snd_musicvolume$2" "0.3"
          "snd_menumusic_volume$4" "0.2" "snd_mvp_volume$4" "0.5" "snd_tensecondwarning_volume$4" "0.2"
          "hud_scaling$3" "0.9" "cl_radar_scale" "0.5" "cl_radar_rotate" "true"
          "cl_radar_always_centered" "false" "fps_max" "300" "fps_max_ui$2" "120" "r_fullscreen_gamma" "2.2"
        } }
        """);
        await File.WriteAllTextAsync(Path.Combine(Cfg(123),LocalConfigStore.VideoFile),"""
        "video.cfg" { "setting.defaultres" "1920" "setting.defaultresheight" "1080" "setting.mat_vsync" "0" "setting.msaa_samples" "4" }
        """);
        await File.WriteAllTextAsync(Path.Combine(Cfg(123),LocalConfigStore.KeysFile),"config { bindings { w +forward x \"custom;synthetic\" } }");
        await File.WriteAllTextAsync(Path.Combine(Cfg(456),LocalConfigStore.MachineFile),"config { convars { unknown preserved cl_showloadout future-value } }");
        await File.WriteAllTextAsync(Path.Combine(remote,"cs2_user_convars.vcfg"),"config { convars { sensitivity 1.5 unknown remote-preserved } }");
        await File.WriteAllTextAsync(Path.Combine(remote,"cs2_user_keys.vcfg"),"config { bindings { w +forward } }");
        bool running=false; int pushes=0; bool failCloud=false;
        var store=new LocalConfigStore(steam,Path.Combine(DataRoot,"cs2-fixture-backups"),()=>running);
        var nav=FindElements(window.Content).OfType<NavigationView>().Single();
        nav.SelectedItem=nav.MenuItems.OfType<NavigationViewItem>().Single(i=>i.Tag?.ToString()=="cs2LocalSettings");
        await Task.Delay(250);
        var page=FindElements(window.Content).OfType<Cs2LocalSettingsPage>().Single();
        await page.PrepareForCheckAsync(store);
        page.TestPush=(files,id)=>
        {
            pushes++;
            if(id!="76561197960265851")throw new Exception("wrong expected target account");
            if(files.Any(f=>f.Name is not "cs2_user_convars.vcfg" and not "cs2_user_keys.vcfg"))throw new Exception("unexpected cloud file");
            return failCloud ? new(false,0,"模拟云端未连接") : new(true,files.Count,null);
        };
        T Find<T>(string name) where T:FrameworkElement => FindElements(page).OfType<T>().Single(x=>x.Name==name);
        TextBox Input(string key)=>FindElements(page).OfType<TextBox>().Single(x=>x.Tag?.ToString()==key);
        async Task Tab(string name)
        {
            var tab=FindElements(page).OfType<ToggleButton>().Single(x=>x.Tag?.ToString()==name);
            ((IToggleProvider)new ToggleButtonAutomationPeer(tab).GetPattern(PatternInterface.Toggle)).Toggle();
            // Toggle automation changes the state without Click; invoke the routed user action through the page test seam.
            page.SelectCategoryForCheck(name); await Task.Delay(80);
        }
        async Task Wait()
        {
            for(int i=0;i<200 && page.IsBusyForCheck;i++)await Task.Delay(20);
            Check(!page.IsBusyForCheck,"CS2 UI operation completes");
        }
        async Task Choose(string choice)
        {
            for(int i=0;i<80;i++)
            {
                var button=Microsoft.UI.Xaml.Media.VisualTreeHelper.GetOpenPopupsForXamlRoot(page.XamlRoot).SelectMany(p=>FindElements(p.Child)).OfType<Button>().FirstOrDefault(b=>b.Content?.ToString()==choice);
                if(button is not null) { Press(button);await Task.Delay(250);return; }
                await Task.Delay(50);
            }
            throw new InvalidOperationException("CS2 dialog did not open: "+choice);
        }
        Check(Find<ComboBox>("ProfilesBox").Items.Count==2,"CS2 UI discovers only synthetic local profiles");
        Check(!Find<Button>("SaveButton").IsEnabled,"CS2 initial render does not dirty original config");
        window.ApplyTheme(ElementTheme.Dark);
        await CaptureAsync(window,"cs2-settings-video-dark");
        foreach(var category in new[]{"音频","游戏 / HUD","准星","鼠标键盘"})
        { await Tab(category);Check(Find<StackPanel>("RowsPanel").Children.Count>0,"CS2 category controls "+category); await CaptureAsync(window,"cs2-settings-"+(category=="游戏 / HUD"?"hud":category=="音频"?"audio":category=="准星"?"crosshair":"keyboard")); }
        Input("sensitivity").Text="2.25";
        Find<ComboBox>("KeyBox").SelectedItem="SPACE";
        Find<ComboBox>("ActionBox").SelectedItem=Find<ComboBox>("ActionBox").Items.OfType<ComboBoxItem>().Single(i=>i.Tag?.ToString()=="+jump");
        Check(Find<Button>("SaveButton").IsEnabled,"CS2 setting and binding edits enable save");
        Press(Find<Button>("SaveButton"));await Wait();
        Check(pushes==1 && Find<InfoBar>("StatusBar").Severity==InfoBarSeverity.Success,"CS2 save automatically pushes only allowed files to selected SteamID");
        var snapshot=store.Load(store.GetProfiles()[0]);
        Check(snapshot.Files[LocalConfigStore.UserFile].Document.Find("config/convars","sensitivity")?.Value=="2.25","CS2 native UI writes edited sensitivity");
        Check(snapshot.Files[LocalConfigStore.KeysFile].Document.Find("config/bindings","SPACE")?.Value=="+jump","CS2 native UI writes selected key action");
        Check(!Find<Button>("SaveButton").IsEnabled && Find<Button>("RestoreButton").IsEnabled,"CS2 saved edits reset and backup restore becomes available");
        await CaptureAsync(window,"cs2-settings-saved");
        Input("sensitivity").Text="3";running=true;Press(Find<Button>("SaveButton"));await Wait();running=false;
        Check(pushes==1 && Find<InfoBar>("StatusBar").Message.Contains("关闭 CS2"),"CS2 running-game rejection never pushes cloud");
        failCloud=true;Press(Find<Button>("SaveButton"));await Wait();
        Check(pushes==2 && Find<InfoBar>("StatusBar").Severity==InfoBarSeverity.Warning && Find<InfoBar>("StatusBar").Message.Contains("已保存"),"CS2 failed cloud accurately preserves local success");
        await CaptureAsync(window,"cs2-settings-cloud-warning");
        Input("sensitivity").Text="NaN";Press(Find<Button>("SaveButton"));await Wait();
        Check(pushes==2 && Find<InfoBar>("StatusBar").Severity==InfoBarSeverity.Error,"CS2 invalid numeric input is rejected before cloud");
        Press(Find<Button>("UndoButton"));Check(!Find<Button>("SaveButton").IsEnabled,"CS2 undo restores snapshot");
        Press(Find<Button>("RestoreButton"));await Choose("恢复本地文件");await Wait();
        Check(store.Load(store.GetProfiles()[0]).Files[LocalConfigStore.UserFile].Document.Find("config/convars","sensitivity")?.Value=="2.25","CS2 restore button restores latest saved backup");
        Check(pushes==2,"CS2 explicitly local restore does not silently overwrite cloud");
        window.ApplyTheme(ElementTheme.Light);await Tab("音频");await CaptureAsync(window,"cs2-settings-audio-light");
        var scale=page.XamlRoot.RasterizationScale;
        window.AppWindow.Resize(new Windows.Graphics.SizeInt32((int)(1100*scale),(int)(800*scale)));await Task.Delay(250);
        await CaptureAsync(window,"cs2-settings-light-narrow");
        var save=Find<Button>("SaveButton");var position=save.TransformToVisual(page).TransformPoint(new Windows.Foundation.Point(0,0));
        Check(position.X>=0 && position.X+save.ActualWidth<=page.ActualWidth+1,"CS2 save action fits narrow window");
        Find<TextBox>("SearchBox").Text="主音量";await Task.Delay(100);
        Check(Find<StackPanel>("RowsPanel").Children.Count==1,"CS2 search filters actual settings");Find<TextBox>("SearchBox").Text="";await Task.Delay(100);
        await Tab("鼠标键盘");Input("sensitivity").Text="5";
        Find<ComboBox>("ProfilesBox").SelectedIndex=1;await Choose("继续编辑");
        Check(Find<ComboBox>("ProfilesBox").SelectedIndex==0 && Find<Button>("SaveButton").IsEnabled,"CS2 cancelling account switch preserves unsaved edits");
        Press(Find<Button>("UndoButton"));Find<ComboBox>("ProfilesBox").SelectedIndex=1;await Wait();
        Check(Find<StackPanel>("RowsPanel").Children.Count==0 && !Find<Button>("SaveButton").IsEnabled,"CS2 missing supported settings show empty state without invented values");
        await CaptureAsync(window,"cs2-settings-empty-light");
        await Tab("游戏 / HUD");
        Check(FindElements(page).OfType<TextBlock>().Any(t=>t.Text=="当前值：future-value（保留）") && !FindElements(page).OfType<ToggleSwitch>().Any(),"CS2 unknown boolean value is displayed honestly instead of false");
        Info("CS2 editor screenshots use synthetic local files and a simulated cloud sender. No real game configuration or cloud content was modified.");
    }
}
#endif
