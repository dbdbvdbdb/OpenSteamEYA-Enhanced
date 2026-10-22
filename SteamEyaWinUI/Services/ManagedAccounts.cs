using OpenSteamEya.AccountManagement;
using SteamEyaWinUI.Models;

namespace SteamEyaWinUI.Services;

/// <summary>
/// One reload's worth of account data, indexed once so cards never re-read or re-scan per card.
/// A round of the manager page loads exactly one snapshot instead of N lookups.
/// </summary>
internal sealed class AccountSnapshot
{
    public static AccountSnapshot Empty { get; } = new(
        new Dictionary<string, SteamAccountHistoryItem>(StringComparer.Ordinal),
        new Dictionary<string, CachedSteamLoginAccount>(StringComparer.Ordinal));

    public AccountSnapshot(
        IReadOnlyDictionary<string, SteamAccountHistoryItem> history,
        IReadOnlyDictionary<string, CachedSteamLoginAccount> cached)
    {
        History = history;
        Cached = cached;
    }

    public IReadOnlyDictionary<string, SteamAccountHistoryItem> History { get; }
    public IReadOnlyDictionary<string, CachedSteamLoginAccount> Cached { get; }

    public SteamAccountHistoryItem? HistoryFor(string? key) =>
        key is not null && History.TryGetValue(key, out var item) ? item : null;

    public CachedSteamLoginAccount? CachedFor(string? key) =>
        key is not null && Cached.TryGetValue(key, out var item) ? item : null;
}

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
    public static List<ExistingIdentity> Identities() => Identities(null);

    public static List<ExistingIdentity> Identities(AccountSnapshot? snapshot)
    {
#if MANAGER_UI_CHECKS
        if (ManagerUiChecks.Enabled) return [];
#endif
        var history = AppState.HistoryAccounts.Select(x => new ExistingIdentity("history:" + AccountHistoryService.GetAccountKey(x), x.AccountName, x.SteamId)).ToList();
        var cachedSource = snapshot is null ? AppState.LoginService.GetCachedLoginAccounts() : snapshot.Cached.Values.ToList();
        var cached = cachedSource.Where(x => !history.Any(h =>
            x.SteamId.Length > 0 && h.SteamId == x.SteamId && h.LoginName.Equals(x.AccountName, StringComparison.OrdinalIgnoreCase)))
            .Select(x => new ExistingIdentity("cache:" + x.CacheKey, x.AccountName, x.SteamId));
        return history.Concat(cached).ToList();
    }

    /// <summary>
    /// Builds the per-round index. The in-memory history list is captured on the caller's
    /// thread; the cached-login file is read and parsed off the UI thread, exactly once.
    /// </summary>
    public static async Task<AccountSnapshot> SnapshotAsync()
    {
        var historySource = AppState.HistoryAccounts;
        var cachedSource = await Task.Run(() =>
        {
            try { return AppState.LoginService.GetCachedLoginAccounts().ToList(); }
            catch { return []; }
        });
        var history = new Dictionary<string, SteamAccountHistoryItem>(historySource.Count, StringComparer.Ordinal);
        foreach (var item in historySource) history["history:" + AccountHistoryService.GetAccountKey(item)] = item;
        var cached = new Dictionary<string, CachedSteamLoginAccount>(cachedSource.Count, StringComparer.Ordinal);
        foreach (var item in cachedSource) cached["cache:" + item.CacheKey] = item;
        return new AccountSnapshot(history, cached);
    }

    public static SteamAccountHistoryItem? History(string? key) => AppState.HistoryAccounts.FirstOrDefault(x => "history:" + AccountHistoryService.GetAccountKey(x) == key);

    public static CachedSteamLoginAccount? Cached(string? key) => AppState.LoginService.GetCachedLoginAccounts().FirstOrDefault(x => "cache:" + x.CacheKey == key);
}
