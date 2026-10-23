#if MANAGER_UI_CHECKS
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using SteamEyaWinUI.Pages;

namespace SteamEyaWinUI.Services;

// Compiled only into an explicit QA build.
internal static partial class ManagerUiChecks
{
    private static async Task CheckNavigationAsync(MainWindow window)
    {
        var nav = FindElements(window.Content).OfType<NavigationView>().Single();
        var items = nav.MenuItems.OfType<NavigationViewItem>().ToArray();
        Check(items.Length == 6, "navigation keeps six primary entries");
        Check(items[0].Tag?.ToString() == "login", "login entry is first in navigation");
        Check(!items.Any(i => i.Tag?.ToString() is "loadout" or "personalization"), "loadout and personalization folded into the CS2 hub");

        nav.SelectedItem = items.First(i => i.Tag?.ToString() == "login");
        await Task.Delay(500);
        nav.SelectedItem = items.First(i => i.Tag?.ToString() == "manager");
        await Task.Delay(800);
        Check(nav.SelectedItem is NavigationViewItem { Tag: "manager" }, "manager navigation selection sticks after switching");
        await CaptureAsync(window, "nav-indicator");

        nav.SelectedItem = items.First(i => i.Tag?.ToString() == "cs2LocalSettings");
        await Task.Delay(800);
        var hub = FindElements(window.Content).OfType<Cs2HubPage>().Single();
        var toggles = FindElements(hub).OfType<ToggleButton>()
            .Where(x => x.Name is "UnifiedTab" or "LoadoutTab" or "PersonalizeTab").ToArray();
        Check(toggles.Length == 3, "cs2 hub exposes three tabs");
        await CaptureAsync(window, "cs2-hub-unified");

        hub.SelectTab(1);
        await Task.Delay(900);
        Check(FindElements(hub).OfType<LoadoutPage>().Any(), "cs2 hub loads the loadout tab");
        await CaptureAsync(window, "cs2-hub-loadout");

        hub.SelectTab(2);
        await Task.Delay(900);
        Check(FindElements(hub).OfType<PersonalizationPage>().Any(), "cs2 hub loads the personalization tab");
        await CaptureAsync(window, "cs2-hub-personalize");

        hub.SelectTab(0);
        await Task.Delay(600);
        nav.SelectedItem = items.First(i => i.Tag?.ToString() == "manager");
        await Task.Delay(600);
    }
}
#endif
