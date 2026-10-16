#if MANAGER_UI_CHECKS
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SteamEyaWinUI.Models;
using SteamEyaWinUI.Pages;

namespace SteamEyaWinUI.Services;

internal static partial class ManagerUiChecks
{
    internal static SteamPaths Cs2FixturePaths
    {
        get
        {
            string root = Path.Combine(DataRoot, "cs2-steam");
            return new(root, Path.Combine(root, "local.vdf"), Path.Combine(root, "config"), Path.Combine(root, "userdata"));
        }
    }

    private static async Task CheckCs2SettingsAsync(MainWindow window)
    {
        var paths = Cs2FixturePaths;
        string cfg = Path.Combine(paths.UserdataPath, "123", "730", "local", "cfg");
        string remote = Path.Combine(paths.UserdataPath, "123", "730", "remote");
        Directory.CreateDirectory(cfg);
        Directory.CreateDirectory(remote);
        Directory.CreateDirectory(Path.Combine(paths.UserdataPath, "456", "730"));
        await File.WriteAllTextAsync(Path.Combine(cfg, "cs2_user_keys_0_slot0.vcfg"), "config { bindings { w +forward MOUSE1 +attack SPACE +jump } }");
        await File.WriteAllTextAsync(Path.Combine(cfg, "cs2_user_convars_0_slot0.vcfg"), "config { convars { sensitivity 1.25 cl_crosshairsize 2.5 volume 0.8 cl_radar_scale 0.7 } }");
        await File.WriteAllTextAsync(Path.Combine(cfg, "cs2_video.txt"), "\"video.cfg\" { \"setting.defaultres\" \"1920\" \"setting.defaultresheight\" \"1080\" }");
        await File.WriteAllTextAsync(Path.Combine(remote, "cs2_user_convars.vcfg"), "config { convars { sensitivity 9 } }");
        var before = Directory.GetFiles(paths.UserdataPath, "*", SearchOption.AllDirectories).ToDictionary(p => p, File.ReadAllBytes);

        var nav = FindElements(window.Content).OfType<NavigationView>().Single();
        nav.SelectedItem = nav.MenuItems.OfType<NavigationViewItem>().Single(i => i.Tag?.ToString() == "cs2Settings");
        await Task.Delay(200);
        var page = FindElements(window.Content).OfType<Cs2SettingsPage>().Single();
        await page.CheckSettingsUiAsync();
        window.ApplyTheme(ElementTheme.Dark);
        await CaptureAsync(window, "cs2-local-settings-dark");
        window.ApplyTheme(ElementTheme.Light);
        await CaptureAsync(window, "cs2-local-settings-light");
        Check(before.All(p => File.ReadAllBytes(p.Key).SequenceEqual(p.Value)), "CS2 UI leaves configuration files unchanged");
        Info("CS2 settings UI uses synthetic local VCFG/video fixtures; real Steam profiles were not accessed.");
    }
}
#endif
