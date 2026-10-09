using OpenSteamEya.AccountManagement;
using SteamEyaWinUI.Models;

namespace SteamEyaWinUI.Services;

internal static class ManagedAccounts
{
    public static AccountStore Store { get; } = new(
#if MANAGER_UI_CHECKS
        ManagerUiChecks.Enabled ? ManagerUiChecks.DataRoot : null
#else
        null
#endif
    );
    public static BackupService Backup { get; } = new(Store);
    // Prefix records by source. A history token and a cached login can share a SteamID.
    // Prefer history when both represent the same identity; never manufacture a new credential.
    public static List<ExistingIdentity> Identities()
    {
#if MANAGER_UI_CHECKS
        if (ManagerUiChecks.Enabled) return [];
#endif
        var history = AppState.HistoryAccounts.Select(x => new ExistingIdentity("history:" + AccountHistoryService.GetAccountKey(x), x.AccountName, x.SteamId)).ToList();
        var cached = AppState.LoginService.GetCachedLoginAccounts().Where(x => !history.Any(h =>
            x.SteamId.Length > 0 && h.SteamId == x.SteamId && h.LoginName.Equals(x.AccountName, StringComparison.OrdinalIgnoreCase)))
            .Select(x => new ExistingIdentity("cache:" + x.CacheKey, x.AccountName, x.SteamId));
        return history.Concat(cached).ToList();
    }
    public static SteamAccountHistoryItem? History(string? key) => AppState.HistoryAccounts.FirstOrDefault(x => "history:" + AccountHistoryService.GetAccountKey(x) == key);
    public static CachedSteamLoginAccount? Cached(string? key) => AppState.LoginService.GetCachedLoginAccounts().FirstOrDefault(x => "cache:" + x.CacheKey == key);
}
