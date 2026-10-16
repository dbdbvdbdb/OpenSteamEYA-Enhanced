#if MANAGER_UI_CHECKS
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SteamEyaWinUI.Localization;
using SteamEyaWinUI.Models;
using SteamEyaWinUI.Services;

namespace SteamEyaWinUI.Pages;

public sealed partial class Cs2SettingsPage
{
    internal async Task CheckSettingsUiAsync()
    {
        async Task WaitForReadAsync()
        {
            for (int i = 0; i < 250 && _busy; i++) await Task.Delay(20);
            ManagerUiChecks.Check(!_busy && RefreshButton.IsEnabled, "CS2 UI finishes asynchronous read");
        }
        IReadOnlyList<Cs2SettingRow> Rows() => ((IEnumerable<Cs2SettingRow>)SettingsList.ItemsSource).ToArray();
        void ChooseFile(string tag) => FileBox.SelectedItem = FileBox.Items.Cast<ComboBoxItem>().Single(i => (string)i.Tag == tag);
        async Task SearchAsync(string query)
        {
            if (SearchBox.Text == query) return;
            // WinUI delivers TextChanged asynchronously, even when Text is set in code.
            // The page's handler was registered by XAML before this one, so rendering
            // has finished when this task completes. Keep testing the real event path.
            var changed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            TextChangedEventHandler handler = (_, _) => changed.TrySetResult(true);
            SearchBox.TextChanged += handler;
            try
            {
                SearchBox.Text = query;
                await changed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            }
            finally { SearchBox.TextChanged -= handler; }
        }
        await WaitForReadAsync();
        ManagerUiChecks.Check(AccountBox.Items.Count == 2 && Rows().Count == 9, "CS2 UI discovers accounts and defaults to local files");
        CategoryBox.SelectedItem = CategoryBox.Items.Cast<ComboBoxItem>().Single(i => i.Tag as Cs2SettingCategory? == Cs2SettingCategory.Keys);
        ManagerUiChecks.Check(Rows().Count == 3 && Rows().Any(r => r.Value == "+forward"), "CS2 UI key binding category");
        CategoryBox.SelectedIndex = 0;
        await SearchAsync("sensitivity");
        var matches = Rows();
        ManagerUiChecks.Check(matches.Count == 1 && matches[0].Value == "1.25", $"CS2 UI searches raw convar name (matched {matches.Count} rows)");
        ChooseFile("#all");
        ManagerUiChecks.Check(Rows().Count == 2 && Rows().Any(r => r.Value == "9"), "CS2 UI keeps cloud and local values separate");
        await SearchAsync("no-such-setting-fixture");
        ManagerUiChecks.Check(Rows().Count == 0 && EmptyText.Visibility == Visibility.Visible, "CS2 UI empty search state");
        await SearchAsync("");
        ChooseFile("local/cfg/cs2_video.txt");
        ManagerUiChecks.Check(Rows().Count == 2 && SourcePathText.Text.EndsWith("cs2_video.txt"), "CS2 UI selects video file and shows full path");
        string originalLanguage = Loc.CurrentCode;
        try
        {
            Loc.SetLanguage("en", persist: false);
            ManagerUiChecks.Check(Rows().Any(r => r.Title == "Resolution width"), "CS2 UI localizes known setting names");
        }
        finally { Loc.SetLanguage(originalLanguage, persist: false); }
        AccountBox.SelectedIndex = 1;
        await WaitForReadAsync();
        ManagerUiChecks.Check(Rows().Count == 0 && EmptyText.Visibility == Visibility.Visible, "CS2 UI account switch clears previous settings");
        AccountBox.SelectedIndex = 0;
        await WaitForReadAsync();
        ChooseFile("#local");
        await LoadAsync(scanAccounts: true);
        ManagerUiChecks.Check(Rows().Count == 9, "CS2 UI refresh restores local settings");
    }
}
#endif
