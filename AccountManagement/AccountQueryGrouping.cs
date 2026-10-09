namespace OpenSteamEya.AccountManagement;

public enum AccountQueryDisposition { Success, Failed, Unqueried }

public static class AccountQueryGrouping
{
    public const string FailedGroup = "失败";
    public const string UnqueriedGroup = "未查询";
    public static void ApplyBatch(ManagerDocument doc, IReadOnlyList<Guid> accountIds, IReadOnlyList<string?> keys, AccountQueryBatchResult result)
    {
        if (accountIds.Count != keys.Count || result.ProcessedCount < 0 || result.ProcessedCount > keys.Count)
            throw new ArgumentException("批量查询结果范围无效。");
        var outcomes = new Dictionary<Guid, AccountQueryDisposition>();
        for (int i = 0; i < result.ProcessedCount; i++)
        {
            if (string.IsNullOrEmpty(keys[i])) outcomes[accountIds[i]] = AccountQueryDisposition.Unqueried;
            else if (result.Outcomes.TryGetValue(keys[i]!, out var succeeded))
                outcomes[accountIds[i]] = succeeded ? AccountQueryDisposition.Success : AccountQueryDisposition.Failed;
        }
        Apply(doc, outcomes);
    }
    public static void Apply(ManagerDocument doc, IReadOnlyDictionary<Guid, AccountQueryDisposition> outcomes)
    {
        Guid? Find(string name) => doc.Groups.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Id;
        Guid Ensure(string name)
        {
            if (Find(name) is { } id) return id;
            var group = new ManagedGroup { Name = name, Order = doc.Groups.Select(x => x.Order).DefaultIfEmpty(-1).Max() + 1 };
            doc.Groups.Add(group); return group.Id;
        }
        foreach (var account in doc.Accounts)
        {
            if (!outcomes.TryGetValue(account.Id, out var outcome)) continue;
            // Add/remove only query labels, preserving every user-defined membership and credential field.
            var groups = AccountGroups.Ids(account).Where(id => id != Find(FailedGroup) && id != Find(UnqueriedGroup)).ToList();
            if (outcome != AccountQueryDisposition.Success)
                groups.Add(Ensure(outcome == AccountQueryDisposition.Failed ? FailedGroup : UnqueriedGroup));
            AccountGroups.Set(account, groups);
        }
    }
}
