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
    // “默认分组”只包含尚未加入任何分组的账号；账号一旦归入分组就不会再出现在这里。
    public static bool IsUngrouped(ManagedAccount account) => !Ids(account).Any();
    public static bool Matches(ManagedAccount account, string? filter) => filter switch
    {
        null or "all" => true,
        "default" => IsUngrouped(account),
        _ => Guid.TryParse(filter, out var id) && Ids(account).Contains(id),
    };
    public static bool InExportRange(ManagedAccount account, IReadOnlySet<string> tags) =>
        (tags.Contains("default") && IsUngrouped(account)) || Ids(account).Any(id => tags.Contains(id.ToString()));
}
