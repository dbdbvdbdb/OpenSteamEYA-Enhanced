namespace OpenSteamEya.AccountManagement;

public static class AccountGroups
{
    public static IEnumerable<Guid> Ids(ManagedAccount account) =>
        (account.GroupId is { } legacy ? new[] { legacy } : Array.Empty<Guid>())
        .Concat(account.GroupIds ?? []).Distinct();
    public static void Set(ManagedAccount account, IEnumerable<Guid> ids)
    {
        account.GroupIds = ids.Distinct().ToList();
        account.GroupId = account.GroupIds.Count > 0 ? account.GroupIds[0] : null;
    }
    public static bool Matches(ManagedAccount account, string? filter) => filter is null or "all" or "default" ||
        Guid.TryParse(filter, out var id) && Ids(account).Contains(id);
    public static bool InExportRange(ManagedAccount account, IReadOnlySet<string> tags) =>
        tags.Contains("default") || Ids(account).Any(id => tags.Contains(id.ToString()));
}
