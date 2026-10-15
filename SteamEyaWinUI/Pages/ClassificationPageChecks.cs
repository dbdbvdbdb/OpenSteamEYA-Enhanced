#if MANAGER_UI_CHECKS
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using OpenSteamEya.AccountManagement;
using SteamEyaWinUI.Services;

namespace SteamEyaWinUI.Pages;

public sealed partial class LoginPage
{
    internal void CheckSourcePreservation()
    {
        var previousMode = ModeSelector.SelectedItem;
        ModeSelector.SelectedItem = ManualModeItem;
        _credentialOriginName = "synthetic-origin";
        _credentialOriginToken = "synthetic-origin-token";
        ManagerUiChecks.Check(!IsNewTokenSource("SYNTHETIC-ORIGIN", "synthetic-origin-token"), "credential origin survives switching to manual mode");
        ManagerUiChecks.Check(IsNewTokenSource("synthetic-other", "synthetic-origin-token") && IsNewTokenSource("synthetic-origin", "synthetic-other-token"), "changed manual identity or token is a new import source");
        ModeSelector.SelectedItem = AutoModeItem;
        ManagerUiChecks.Check(IsNewTokenSource("synthetic-origin", "synthetic-origin-token"), "explicit automatic import retains automatic origin");
        _credentialOriginName = _credentialOriginToken = null;
        ModeSelector.SelectedItem = previousMode;
    }
}

public sealed partial class HistoryPage
{
    internal async Task CheckBlackAnnotationsAsync()
    {
        ManagerUiChecks.Check(_blackMode && _allItems.Count == 1, "black scope is established before annotation checks");
        ((IInvokeProvider)new ButtonAutomationPeer(SelectAllHistoryButton).GetPattern(PatternInterface.Invoke)).Invoke();
        ManagerUiChecks.Check(GetCheckedAccounts().Count == 1 && MoveToWhiteButton.Visibility == Visibility.Visible, "native black select-all exposes transfer action");
        UpdateLayout();
        var moveBottom = MoveToWhiteButton.TransformToVisual(BatchActionBar).TransformPoint(new Windows.Foundation.Point(0, MoveToWhiteButton.ActualHeight)).Y;
        var summaryTop = BatchSelectionText.TransformToVisual(BatchActionBar).TransformPoint(new Windows.Foundation.Point(0, 0)).Y;
        ManagerUiChecks.Check(moveBottom <= summaryTop, "black transfer button does not overlap selection summary");
        var account = _allItems.Single();
        HistoryAccountList.SelectedIndex = 0;
        var group = await CreateBlackGroupAsync("黑号测试分组");
        await SetBlackGroupsAsync([account], group.Id, true);
        await Task.Delay(200);
        var persisted = AccountClassification.Find(await ManagedAccounts.Store.LoadAsync(), account.AccountName, account.SteamId)!;
        ManagerUiChecks.Check(AccountGroups.Ids(persisted).Contains(Guid.Parse(group.Id)), "black group membership persists in canonical manager metadata");
        HistoryAccountList.SelectedIndex = 0;
        HistoryDetailNoteBox.Text = "黑号备注测试";
        FlushPendingNote(); await Task.Delay(200);
        persisted = AccountClassification.Find(await ManagedAccounts.Store.LoadAsync(), account.AccountName, account.SteamId)!;
        ManagerUiChecks.Check(persisted.Note == "黑号备注测试", "black detail note persists without replacing the credential store");
    }
}
#endif
