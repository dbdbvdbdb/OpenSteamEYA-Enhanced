#if MANAGER_UI_CHECKS
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Net;
using SteamEyaWinUI.Pages;

namespace SteamEyaWinUI.Services;

internal static partial class ManagerUiChecks
{
    private static async Task CheckUpdatesAsync(MainWindow window)
    {
        var original = AppState.UpdateService;
        var handler = new UpdateFixtureHandler();
        using var client = new HttpClient(handler);
        AppState.UpdateService = new GitHubUpdateService(client, "0.2.6-beta");
        try
        {
            var pending = AppState.CheckForUpdatesAsync(isAutomatic: true);
            Check(AppState.IsCheckingForUpdates && !pending.IsCompleted, "startup update check is asynchronous");
            await AppState.CheckForUpdatesAsync(isAutomatic: true);
            await pending;
            await AppState.CheckForUpdatesAsync(isAutomatic: true);
            Check(handler.Calls == 1, "automatic update check runs once per process and suppresses concurrent checks");
            var nav = FindElements(window.Content).OfType<NavigationView>().Single();
            var aboutItem = nav.FooterMenuItems.OfType<NavigationViewItem>().Single(x => x.Tag?.ToString() == "about");
            Check(aboutItem.InfoBadge is not null, "new release lights About badge");
            nav.SelectedItem = aboutItem;
            await Task.Delay(200);
            Check(handler.Calls == 1, "About navigation does not repeat startup request");
            var about = FindElements(window.Content).OfType<AboutPage>().Single();
            var download = FindElements(about).OfType<Button>().Single(b => b.Name == "DownloadUpdateButton");
            var status = FindElements(about).OfType<TextBlock>().Single(b => b.Name == "AboutUpdateStatusText");
            Check(download.IsEnabled && status.Text.Contains("v0.2.7-beta"), "About displays newer beta with portable download action");
            window.ApplyTheme(ElementTheme.Dark);
            await CaptureAsync(window, "update-available-dark");
            window.ApplyTheme(ElementTheme.Light);
            await CaptureAsync(window, "update-available-light");

            if (AppState.LatestUpdate is { IsUpdateAvailable: true } startupUpdate)
            {
                await window.CheckStartupUpdateDialogAsync(startupUpdate);
            }

            handler.Fail = true;
            await AppState.CheckForUpdatesAsync(isAutomatic: false);
            Check(AppState.UpdateCheckError is not null && !AppState.IsCheckingForUpdates && !download.IsEnabled && aboutItem.InfoBadge is null,
                "network error leaves app usable and removes stale download state");
            await AppState.CheckForUpdatesAsync(isAutomatic: true);
            Check(handler.Calls == 2, "automatic failure retry does not poll in same launch");
            handler.Fail = false;
            var checkButton = FindElements(about).OfType<Button>().Single(b => b.Name == "CheckUpdateButton");
            Press(checkButton);
            for (int i = 0; i < 50 && (handler.Calls < 3 || AppState.IsCheckingForUpdates); i++) await Task.Delay(20);
            Check(handler.Calls == 3 && AppState.UpdateCheckError is null && download.IsEnabled, "manual check button retries successfully after failure");
            AppState.UpdateService = new GitHubUpdateService(client, "0.2.8-beta");
            await AppState.CheckForUpdatesAsync(isAutomatic: false);
            Check(!download.IsEnabled && aboutItem.InfoBadge is null && status.Text.Contains("0.2.8-beta"), "current newer build is not offered an older release");
            Results.Add("INFO Update UI uses simulated release responses; no downloads or installer execution.");
        }
        finally { AppState.UpdateService = original; }
    }

    private sealed partial class UpdateFixtureHandler : HttpMessageHandler
    {
        internal int Calls;
        internal bool Fail;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Calls++;
            await Task.Delay(60, token);
            if (Fail) throw new HttpRequestException("Synthetic network failure");
            return new(HttpStatusCode.OK) { Content = new StringContent("""
                [{"tag_name":"v0.2.7-beta","published_at":"2026-10-16T00:00:00Z","draft":false,"body":"更新提示界面测试（虚构版本，仅用于自动化验收）","assets":[
                 {"name":"OpenSteamEYA-Enhanced-0.2.7-beta-win-x64-aot.zip","state":"uploaded","size":30000000,
                  "browser_download_url":"https://github.com/dbdbvdbdb/OpenSteamEYA-Enhanced/releases/download/v0.2.7-beta/OpenSteamEYA-Enhanced-0.2.7-beta-win-x64-aot.zip"}]}]
                """) };
        }
    }
}
#endif
