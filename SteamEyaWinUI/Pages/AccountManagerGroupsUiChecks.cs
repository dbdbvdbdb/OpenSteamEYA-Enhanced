#if MANAGER_UI_CHECKS
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using OpenSteamEya.AccountManagement;
using SteamEyaWinUI.Models;
using SteamEyaWinUI.Services;

namespace SteamEyaWinUI.Pages;

public sealed partial class AccountManagerPage
{
    internal async Task CheckMultipleGroupsAsync()
    {
        var a = new ManagedGroup { Name = "多选分组 A" }; var b = new ManagedGroup { Name = "多选分组 B" };
        var record = document.Accounts[0]; string password = record.Password; string? mapping = record.LinkedKey;
        await ManagedAccounts.Store.UpdateAsync(doc =>
        {
            doc.Groups.Add(a); doc.Groups.Add(b);
            AccountGroups.Set(doc.Accounts[0], [a.Id, b.Id]); AccountGroups.Set(doc.Accounts[1], [b.Id]);
        });
        await ReloadAsync(); SelectTag(GroupFilter, a.Id.ToString()); await Task.Delay(100);
        ManagerUiChecks.Check(visible.Count == 1, "multiple-membership first group filter");
        SelectTag(GroupFilter, b.Id.ToString()); await Task.Delay(100);
        ManagerUiChecks.Check(visible.Count == 2, "multiple-membership second group filter");
        SelectTag(GroupFilter, "default"); await Task.Delay(100);
        ManagerUiChecks.Check(visible.Count == document.Accounts.Count, "default group always shows all accounts including assigned members");
        var card = visible.First(x => x.Account.Id == record.Id);
        var menu = (MenuFlyout)card.ContextFlyout; menu.ShowAt(card); await Task.Delay(100);
        Invoke(menu.Items.OfType<MenuFlyoutItem>().Single(x => x.Text == "设置账号分组"));
        var elements = await FindDialogElementsAsync("设置账号分组");
        var groups = elements.OfType<ListView>().Single(x => x.Header?.ToString()?.StartsWith("所属分组") == true);
        ManagerUiChecks.Check(groups.SelectionMode == ListViewSelectionMode.Multiple && groups.SelectedItems.Count == 2, "native group menu opens multi-select with existing memberships checked");
        groups.SelectedItems.Clear(); groups.SelectedItems.Add(groups.Items.OfType<ListViewItem>().Single(x => (Guid)x.Tag == b.Id));
        Invoke(elements.OfType<Button>().First(x => x.Content?.ToString() == "保存"));
        for (int attempt = 0; AccountGroups.Ids(document.Accounts.First(x => x.Id == record.Id)).Count() != 1 && attempt < 60; attempt++) await Task.Delay(50);
        var saved = document.Accounts.First(x => x.Id == record.Id);
        ManagerUiChecks.Check(saved.GroupIds.SequenceEqual([b.Id]) && saved.Password == password && saved.LinkedKey == mapping, "native group save removes only unchecked membership and preserves account credentials");
        ManagerUiChecks.Check(visible.Count == document.Accounts.Count && GroupName(saved).Contains("默认分组") && GroupName(saved).Contains(b.Name), "default filter and group metadata remain correct after multi-select save");
        var edit = EditAsync(saved); elements = await FindDialogElementsAsync("账号信息");
        groups = elements.OfType<ListView>().Single(x => x.Header?.ToString()?.StartsWith("所属分组") == true);
        groups.SelectedItems.Add(groups.Items.OfType<ListViewItem>().Single(x => (Guid)x.Tag == a.Id));
        Invoke(elements.OfType<Button>().First(x => x.Content?.ToString() == "保存")); await edit;
        saved = document.Accounts.First(x => x.Id == record.Id);
        ManagerUiChecks.Check(AccountGroups.Ids(saved).ToHashSet().SetEquals([a.Id, b.Id]) && saved.Password == password, "native account editor persists multiple groups without changing password");
        SelectTag(GroupFilter, "all"); await Task.Delay(100);
    }
    internal async Task CheckCooldownStatusAsync(Func<string, Task> capture)
    {
        var accounts = document.Accounts.OrderBy(x => x.Order).ToList();
        ManagerUiChecks.StatusFixtures[accounts[0].Id] = new() { CooldownSeconds = 3605, GcVacBanned = true, CsStatusUpdatedAt = DateTimeOffset.Now };
        ManagerUiChecks.StatusFixtures[accounts[1].Id] = new() { CooldownSeconds = 0, GcVacBanned = false };
        ManagerUiChecks.StatusFixtures[accounts[2].Id] = new() { CooldownSeconds = uint.MaxValue, GcVacBanned = false };
        await ManagedAccounts.Store.UpdateAsync(doc =>
        {
            doc.Accounts[0].Nickname = "合成账号 A"; doc.Accounts[0].Note = "冷却与标记测试";
            doc.Accounts[1].Nickname = "合成账号 B"; doc.Accounts[1].Note = "正常状态测试";
        });
        await ReloadAsync(); await WaitForVisibleCardAsync();
        TextBlock Status(AccountCard card) => Walk(card).OfType<TextBlock>().First(x => x.Name == "AccountCooldownStatus");
        var cooling = Status(visible[0]); var time = cooling.Inlines.OfType<Run>().First(); var vac = cooling.Inlines.OfType<Run>().Last();
        bool Yellow(Brush brush) => brush is SolidColorBrush { Color: { R: 240, G: 196, B: 92 } };
        bool Red(Brush brush) => brush is SolidColorBrush { Color: { R: 242, G: 111, B: 123 } };
        ManagerUiChecks.Check(Yellow(time.Foreground) && time.Text != "存在冷却" && Red(vac.Foreground) && vac.Text.Contains("GCVAC"), "card shows remaining cooldown time yellow and flagged GCVAC red");
        ManagerUiChecks.Check(cooling.ActualHeight > 20 && cooling.MaxLines == 2 && visible[0].HasActiveCooldownTimer, "cooldown and GCVAC use two visible lines with live countdown timer");
        string before = time.Text; await Task.Delay(1250);
        ManagerUiChecks.Check(before != time.Text, "visible card remaining cooldown refreshes without querying Steam");
        var normal = Status(visible[1]).Inlines.OfType<Run>().ToList();
        ManagerUiChecks.Check(normal.Count == 1 && normal[0].Text == "正常" && !Yellow(normal[0].Foreground) && !Red(normal[0].Foreground), "normal cooldown and unflagged GCVAC collapse into only normal");
        ManagerUiChecks.Check(Status(visible[2]).Inlines.OfType<Run>().First().Text == "正常", "expired unsigned cooldown sentinel is treated as normal");
        var unknown = Status(visible[3]).Inlines.OfType<Run>().ToList();
        ManagerUiChecks.Check(unknown.Count == 1 && unknown[0].Text == "未知" && !Red(unknown[0].Foreground), "missing cooldown and GCVAC remain unknown without fabricated flags");
        ManagerUiChecks.StatusFixtures[accounts[2].Id] = new() { CooldownSeconds = 3605, GcVacBanned = false };
        ManagerUiChecks.StatusFixtures[accounts[3].Id] = new() { CooldownSeconds = 0, GcVacBanned = true };
        await ReloadAsync(); await WaitForVisibleCardAsync();
        var onlyCooldown = Status(visible[2]).Inlines.OfType<Run>().ToList();
        var onlyVac = Status(visible[3]).Inlines.OfType<Run>().ToList();
        ManagerUiChecks.Check(onlyCooldown.Count == 1 && Yellow(onlyCooldown[0].Foreground) && !onlyCooldown[0].Text.Contains("GCVAC"), "cooldown-only card hides normal and unflagged GCVAC");
        ManagerUiChecks.Check(onlyVac.Count == 1 && Red(onlyVac[0].Foreground) && onlyVac[0].Text.StartsWith("GCVAC"), "GCVAC-only card hides normal cooldown");
        await capture("cooldown-groups-dark");
        visible[0].Stop(); ManagerUiChecks.Check(!visible[0].HasActiveCooldownTimer, "stopped card stops countdown timer");
        await SwitchUiCheckAsync("list", false); await WaitForVisibleCardAsync();
        var compact = Status(visible[0]).Inlines.OfType<Run>().ToList();
        ManagerUiChecks.Check(Yellow(compact[1].Foreground) && Red(compact[^1].Foreground), "compact list also shows yellow cooldown time and red GCVAC");
        ManagerUiChecks.Check(Status(visible[1]).Inlines.OfType<Run>().Count() == 2 && Status(visible[2]).Inlines.OfType<Run>().Count() == 2 &&
            Status(visible[3]).Inlines.OfType<Run>().Count() == 2, "compact list omits redundant normal and unflagged status alongside anomalies");
        await capture("cooldown-groups-list");
        await SwitchUiCheckAsync("cards", false); await WaitForVisibleCardAsync();
        ManagerUiChecks.StatusFixtures[accounts[2].Id] = new() { CooldownSeconds = 3, GcVacBanned = false, CsStatusUpdatedAt = DateTimeOffset.Now };
        Render(); await WaitForVisibleCardAsync(); await Task.Delay(3250);
        var expired = Status(visible[2]).Inlines.OfType<Run>().ToList();
        ManagerUiChecks.Check(expired.Count == 1 && expired[0].Text == "正常" && !visible[2].HasActiveCooldownTimer, "expired countdown transitions to only normal and stops timer");
    }
}
#endif
