namespace OpenSteamEya.AccountManagement;

public static class AccountMapping
{
    public static MappingResult Resolve(ManagedAccount account, IEnumerable<ExistingIdentity> identities)
    {
        var all = identities.GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase).Select(x => x.First()).ToList();
        var byId = string.IsNullOrWhiteSpace(account.SteamId) ? [] : all.Where(x => x.SteamId == account.SteamId).ToList();
        var byName = all.Where(x => x.LoginName.Equals(account.LoginName, StringComparison.OrdinalIgnoreCase)).ToList();
        var conflictingName = !string.IsNullOrWhiteSpace(account.SteamId) && byName.Any(x =>
            !string.IsNullOrWhiteSpace(x.SteamId) && x.SteamId != account.SteamId);
        var candidates = byId.Count > 0 ? byId : byName;
        if (conflictingName) candidates = byId.Concat(byName).DistinctBy(x => x.Key).ToList();
        var manual = all.FirstOrDefault(x => x.Key == account.LinkedKey);
        if (manual is not null && (string.IsNullOrWhiteSpace(account.SteamId) ||
            string.IsNullOrWhiteSpace(manual.SteamId) || manual.SteamId == account.SteamId))
            return new(manual.Key, [manual], false);
        if (conflictingName || candidates.Count > 1) return new(null, candidates, true);
        return new(candidates.SingleOrDefault()?.Key, candidates, false);
    }
}
