#if MANAGER_UI_CHECKS
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using OpenSteamEya.AccountManagement;
using SteamEyaWinUI.Models;
using SteamEyaWinUI.Pages;
using System.Text.Json;

namespace SteamEyaWinUI.Services;

internal static partial class ManagerUiChecks
{
    private static void Press(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    private static async Task<List<DependencyObject>> DialogElementsAsync(FrameworkElement owner)
    {
        for (int i = 0; i < 60; i++)
        {
            var elements = VisualTreeHelper.GetOpenPopupsForXamlRoot(owner.XamlRoot).SelectMany(x => FindElements(x.Child)).ToList();
            if (elements.OfType<Button>().Any(x => x.Content?.ToString() == "取消")) return elements;
            await Task.Delay(50);
        }
        throw new InvalidOperationException("classification dialog did not open");
    }
    private static async Task ChooseAsync(FrameworkElement owner, string choice)
    {
        var elements = await DialogElementsAsync(owner);
        Press(elements.OfType<Button>().First(x => x.Content?.ToString() == choice));
        await Task.Delay(180);
    }
    private static async Task CheckClassificationsAsync(MainWindow window, AccountManagerPage page)
    {
        void Navigate(string tag)
        {
            var nav = FindElements(window.Content).OfType<NavigationView>().Single();
            nav.SelectedItem = nav.MenuItems.OfType<NavigationViewItem>().First(x => x.Tag?.ToString() == tag);
        }
        var group = new ManagedGroup { Name = "保留的分组" };
        var white = new ManagedAccount { LoginName = "synthetic-white", SteamId = "76561198000000501", Password = "synthetic-password", Nickname = "白号示例", Note = "保留备注", Favorite = true, GroupId = group.Id };
        await ManagedAccounts.Store.ReplaceAsync(new() { Accounts = [white], Groups = [group] });
        await AccountClassification.Categories.UpdateAsync(x => { x.BlackAccounts.Clear(); x.AddCredentialsToWhite = true; });
        AppState.AccountHistoryService.ImportAccounts([
            new() { AccountName = white.LoginName, SteamId = white.SteamId, EyaToken = "synthetic-not-a-token" },
            new() { AccountName = "synthetic-black", SteamId = "76561198000000502", EyaToken = "synthetic-not-a-token" },
            new() { AccountName = "synthetic-history-only", SteamId = "76561198000000503", EyaToken = "synthetic-not-a-token" }
        ]);
        AppState.ReloadHistory();
        AccountClassification.NotifyChanged(); await Task.Delay(350);
        Check((await ManagedAccounts.Store.LoadAsync()).Accounts.Count == 1, "history refresh no longer auto-adds all accounts");
        await AccountClassification.AddAsync(page, "synthetic-black", "76561198000000502", true);
        Check((await AccountClassification.BlackItemsAsync()).Count == 1, "black view excludes unrelated history");

        var conflict = AccountClassification.AddAsync(page, white.LoginName, white.SteamId, true);
        await DialogElementsAsync(page); await CaptureAsync(window, "classification-conflict");
        await ChooseAsync(page, "取消");
        Check(!await conflict && !(await AccountClassification.Categories.LoadAsync()).IsBlack(white.LoginName, white.SteamId), "cancel conflicting import preserves white ownership");
        conflict = AccountClassification.AddAsync(page, white.LoginName, white.SteamId, true);
        await ChooseAsync(page, "保留在黑号管理"); await conflict;
        Check((await AccountClassification.Categories.LoadAsync()).IsBlack(white.LoginName, white.SteamId), "explicit conflict choice transfers to black");
        var moved = (await ManagedAccounts.Store.LoadAsync()).Accounts.Single(x => x.Id == white.Id);
        Check(moved.Note == white.Note && moved.Favorite && moved.Password == white.Password && AccountGroups.Ids(moved).Contains(group.Id), "transfer retains password note favorite and group");
        conflict = AccountClassification.AddAsync(page, white.LoginName, white.SteamId, false, "synthetic-updated-password");
        await ChooseAsync(page, "保留在白号管理"); await conflict;
        Check((await ManagedAccounts.Store.LoadAsync()).Accounts.Single(x => x.Id == white.Id).Password == "synthetic-updated-password", "confirmed credentials update password without duplicate");
        Check(!(await AccountClassification.Categories.LoadAsync()).IsBlack(white.LoginName, white.SteamId), "account has only one category after conflict choice");
        var idCandidate = new ManagedAccount { LoginName = "synthetic-known-id", SteamId = white.SteamId };
        var ambiguousDoc = new ManagerDocument { Accounts = [new() { LoginName = white.LoginName }, idCandidate] };
        Check(AccountClassification.Find(ambiguousDoc, white.LoginName, white.SteamId) == idCandidate, "known SteamID takes precedence over an earlier name-only candidate");
        var whiteCount = (await ManagedAccounts.Store.LoadAsync()).Accounts.Count;
        var repeated = await AccountClassification.ImportWhiteAsync(page, new([new(1, new() { LoginName = "synthetic-renamed", SteamId = white.SteamId, Password = "synthetic-repeated" })], []), DuplicatePolicy.Overwrite);
        Check(repeated.Updated == 1 && (await ManagedAccounts.Store.LoadAsync()).Accounts.Count == whiteCount, "white import deduplicates by known SteamID even if login name differs");
        bool identityRejected = false;
        try { await AccountClassification.AddAsync(page, white.LoginName, "76561198000000999", true); } catch (System.IO.InvalidDataException) { identityRejected = true; }
        Check(identityRejected && !(await AccountClassification.Categories.LoadAsync()).IsBlack(white.LoginName, "76561198000000999"), "conflicting known SteamID cannot create a classification residue");
        AccountClassification.NotifyChanged(); await Task.Delay(350);
        await CaptureAsync(window, "classification-white");

        Navigate("login"); await Task.Delay(300);
        var login = FindElements(window.Content).OfType<LoginPage>().Single();
        login.CheckSourcePreservation();
        var selector = FindElements(login).OfType<SelectorBar>().Single();
        selector.SelectedItem = selector.Items.First(x => x.Name == "CredsModeItem");
        var checkbox = FindElements(login).OfType<CheckBox>().Single(x => x.Name == "AddToWhiteBox");
        Check(checkbox.IsChecked == true, "native credential checkbox defaults checked");
        checkbox.IsChecked = false; await Task.Delay(250);
        Check(!(await new CategoryStore(ManagedAccounts.Store.Root).LoadAsync()).AddCredentialsToWhite, "native checkbox persists choice");
        checkbox.IsChecked = true; await Task.Delay(180);
        await CaptureAsync(window, "classification-login");

        Navigate("black"); await Task.Delay(500);
        var history = FindElements(window.Content).OfType<HistoryPage>().Single();
        var list = FindElements(history).OfType<ListView>().Single(x => x.Name == "HistoryAccountList");
        Check(list.Items.Count == 1 && ((SteamAccountHistoryItem)list.Items[0]).AccountName == "synthetic-black", "native black page shows only its own account");
        await history.CheckBlackAnnotationsAsync();
        await CaptureAsync(window, "classification-black");
        window.ApplyTheme(ElementTheme.Light); await Task.Delay(200); await CaptureAsync(window, "classification-black-light");
        window.ApplyTheme(ElementTheme.Dark);
        Navigate("history"); await Task.Delay(300);
        list = FindElements(window.Content).OfType<ListView>().Single(x => x.Name == "HistoryAccountList");
        Check(list.Items.Count == 3, "history navigation retains full history after black scope");
        Navigate("black"); await Task.Delay(300);
        history = FindElements(window.Content).OfType<HistoryPage>().Single();
        list = FindElements(history).OfType<ListView>().Single(x => x.Name == "HistoryAccountList");
        Check(list.Items.Count == 1, "switching back never leaks the full history into black scope");

        var cacheService = new SteamLoginCacheService();
        Directory.CreateDirectory(cacheService.AppFolderPath);
        async Task SeedDeletionAsync()
        {
            AppState.AccountHistoryService.ImportAccounts([new() { AccountName = "synthetic-delete", SteamId = "76561198000000504", EyaToken = "synthetic-not-a-token" }]);
            AppState.ReloadHistory();
            await AccountClassification.AddAsync(history, "synthetic-delete", "76561198000000504", true);
            await File.WriteAllTextAsync(cacheService.CacheFilePath, JsonSerializer.Serialize(new CachedSteamLoginDocument { Accounts = [new() { AccountName = "synthetic-delete", SteamId = "76561198000000504", ConnectCacheToken = "synthetic-encrypted-placeholder" }] }, SteamLoginCacheJsonContext.Default.CachedSteamLoginDocument));
        }
        async Task DeleteChoice(bool records, bool caches, bool cancel = false)
        {
            var task = AccountClassification.DeleteAsync(history, [new() { LoginName = "synthetic-delete", SteamId = "76561198000000504" }]);
            var elements = await DialogElementsAsync(history);
            elements.OfType<CheckBox>().Single(x => x.Content?.ToString() == "删除应用内记录").IsChecked = records;
            elements.OfType<CheckBox>().Single(x => x.Content?.ToString() == "删除 Steam 登录缓存").IsChecked = caches;
            Check(elements.OfType<Button>().Single(x => x.Content?.ToString() == "删除所选内容").IsEnabled == (records || caches), "deletion requires at least one selected scope");
            if (cancel) await CaptureAsync(window, "classification-delete-options");
            await ChooseAsync(history, cancel ? "取消" : "删除所选内容"); await task;
        }
        await SeedDeletionAsync(); await DeleteChoice(false, false, true);
        Check(AppState.HistoryAccounts.Any(x => x.AccountName == "synthetic-delete") && cacheService.LoadAll().Count == 1, "cancel deletion changes neither records nor cache");
        await DeleteChoice(false, true);
        Check(AppState.HistoryAccounts.Any(x => x.AccountName == "synthetic-delete") && cacheService.LoadAll().Count == 0, "cache-only deletion retains history and manager");
        await SeedDeletionAsync(); await DeleteChoice(true, false);
        Check(!AppState.HistoryAccounts.Any(x => x.AccountName == "synthetic-delete") && cacheService.LoadAll().Count == 1, "records-only deletion retains independent Steam cache");
        Check(!(await ManagedAccounts.Store.LoadAsync()).Accounts.Any(x => x.LoginName == "synthetic-delete") && !(await AccountClassification.Categories.LoadAsync()).BlackAccounts.Any(x => x.LoginName == "synthetic-delete"), "records deletion removes management and classification membership");
        await SeedDeletionAsync(); await DeleteChoice(true, true);
        Check(!AppState.HistoryAccounts.Any(x => x.AccountName == "synthetic-delete") && cacheService.LoadAll().Count == 0, "combined deletion clears both selected scopes");
        Check(AppState.HistoryAccounts.Count == 3, "deletion leaves unrelated accounts untouched");
        Info("Classification screenshots and UI checks use synthetic accounts only; no real Steam authentication or CS2 requests.");
    }
}
#endif
