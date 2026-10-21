using System.Text.Json;
using OpenSteamEya.AccountManagement;

internal static class GroupStatusChecks
{
    internal static async Task RunAsync(Action<bool, string> check, string root)
    {
        var store = new AccountStore(Path.Combine(root, "multi-group"));
        var first = new ManagedGroup { Name = "A" }; var second = new ManagedGroup { Name = "B" };
        var account = new ManagedAccount { LoginName = "group-account", Password = " original\\\"---- ", Note = "keep", LinkedKey = "history:synthetic", GroupId = first.Id };
        var legacyJson = "{\"version\":1,\"groups\":[{\"id\":\"" + first.Id + "\",\"name\":\"A\"}],\"accounts\":[{\"loginName\":\"legacy\",\"password\":\" exact \",\"groupId\":\"" + first.Id + "\"}]}";
        var legacy = JsonSerializer.Deserialize(legacyJson, ManagerJsonContext.Default.ManagerDocument)!;
        AccountStore.Validate(legacy);
        check(legacy.Accounts[0].GroupIds.SequenceEqual([first.Id]) && legacy.Accounts[0].Password == " exact ", "legacy v1 single group migrates without changing password");
        await store.ReplaceAsync(new() { Groups = [first, second], Accounts = [account] });
        await store.UpdateAsync(doc => AccountGroups.Set(doc.Accounts[0], [first.Id, second.Id, first.Id]));
        var loaded = (await store.LoadAsync()).Accounts[0];
        check(loaded.GroupIds.SequenceEqual([first.Id, second.Id]) && loaded.GroupId == first.Id, "multiple memberships persist uniquely with legacy first group");
        check(!AccountGroups.Matches(loaded, "default") && AccountGroups.Matches(loaded, first.Id.ToString()) && AccountGroups.Matches(loaded, second.Id.ToString()), "grouped account leaves default and appears in each assigned group");
        check(AccountGroups.InExportRange(loaded, new HashSet<string> { second.Id.ToString() }) && !AccountGroups.InExportRange(loaded, new HashSet<string> { "default" }), "export range follows memberships and default excludes grouped accounts");
        var parsed = ImportExport.Parse(ImportExport.Export([loaded], "json"), "json").Rows[0].Account;
        check(parsed.GroupId is null && parsed.GroupIds.Count == 0 && parsed.Password == account.Password, "portable export keeps passwords while isolating all group IDs");
        var backup = new BackupService(store); var path = Path.Combine(root, "multi-group.zip"); await backup.CreateAsync(path);
        var destination = new AccountStore(Path.Combine(root, "multi-group-restore")); var restore = new BackupService(destination);
        var preview = await restore.PreviewAsync(path); await restore.RestoreAsync(preview, RestoreMode.Replace, DuplicatePolicy.Overwrite);
        check((await destination.LoadAsync()).Accounts[0].GroupIds.Count == 2, "replace backup restore preserves all memberships");
        var existingA = new ManagedGroup { Name = "A" }; var existingB = new ManagedGroup { Name = "B" };
        await destination.ReplaceAsync(new() { Groups = [existingB, existingA] });
        await restore.RestoreAsync(preview, RestoreMode.Merge, DuplicatePolicy.Skip);
        var merged = (await destination.LoadAsync()).Accounts[0];
        check(merged.GroupIds.SequenceEqual([existingA.Id, existingB.Id]) && merged.GroupId == existingA.Id, "merge backup restore remaps every membership by group name");
        await destination.DeleteGroupAsync(existingA.Id); merged = (await destination.LoadAsync()).Accounts[0];
        check(merged.GroupIds.SequenceEqual([existingB.Id]) && merged.GroupId == existingB.Id && merged.Password == account.Password, "deleting one group preserves other memberships and password");
        var bad = AccountStore.Clone(await store.LoadAsync()); bad.Accounts[0].GroupIds.Add(Guid.NewGuid());
        bool rejected = false; try { AccountStore.Validate(bad); } catch (InvalidDataException) { rejected = true; }
        check(rejected, "invalid multi-group references are rejected before writing");
        var doc = await store.LoadAsync();
        var untouched = new ManagedAccount { LoginName = "untouched", Password = "keep" }; doc.Accounts.Add(untouched);
        AccountQueryGrouping.Apply(doc, new Dictionary<Guid, AccountQueryDisposition> { [account.Id] = AccountQueryDisposition.Failed });
        var failedId = doc.Groups.Single(x => x.Name == "失败").Id;
        check(AccountGroups.Ids(doc.Accounts[0]).ToHashSet().SetEquals([first.Id, second.Id, failedId]), "failed query adds failure group without replacing manual memberships");
        AccountQueryGrouping.Apply(doc, new Dictionary<Guid, AccountQueryDisposition> { [account.Id] = AccountQueryDisposition.Unqueried });
        var unqueriedId = doc.Groups.Single(x => x.Name == "未查询").Id;
        check(!AccountGroups.Ids(doc.Accounts[0]).Contains(failedId) && AccountGroups.Ids(doc.Accounts[0]).Contains(unqueriedId), "skipped query uses unqueried group rather than failure group");
        AccountQueryGrouping.Apply(doc, new Dictionary<Guid, AccountQueryDisposition> { [account.Id] = AccountQueryDisposition.Success });
        check(AccountGroups.Ids(doc.Accounts[0]).ToHashSet().SetEquals([first.Id, second.Id]) && doc.Accounts[0].Password == account.Password && doc.Accounts[0].LinkedKey == account.LinkedKey && doc.Accounts[0].Note == account.Note, "successful query clears query labels only and preserves credentials and notes");
        check(untouched.GroupIds.Count == 0 && untouched.Password == "keep", "unprocessed accounts retain original data and memberships");
        AccountQueryGrouping.Apply(doc, new Dictionary<Guid, AccountQueryDisposition> { [account.Id] = AccountQueryDisposition.Failed });
        AccountQueryGrouping.Apply(doc, new Dictionary<Guid, AccountQueryDisposition> { [account.Id] = AccountQueryDisposition.Failed });
        check(doc.Groups.Count(x => x.Name == "失败") == 1 && doc.Accounts[0].GroupIds.Count(id => id == failedId) == 1, "repeated failures reuse existing group and unique membership");
        var calls = 0; using var cts = new CancellationTokenSource();
        var batch = await AccountQueryBatch.RunAsync([null, "success", "success", "failed", "cancel", "untouched"], (key, token) =>
        {
            calls++;
            if (key == "failed") throw new InvalidOperationException();
            if (key == "cancel") { cts.Cancel(); token.ThrowIfCancellationRequested(); }
            return Task.CompletedTask;
        }, cts.Token);
        check(batch.Cancelled && batch.ProcessedCount == 4 && calls == 3, "batch records processed boundary before cancelled and unstarted accounts");
        check(batch.Outcomes.Count == 2 && batch.Outcomes["success"] && !batch.Outcomes["failed"] && !batch.Outcomes.ContainsKey("cancel"), "batch retains per-identity success/failure outcomes for group assignment");
        var batchDoc = new ManagerDocument();
        for (int i = 0; i < 6; i++) batchDoc.Accounts.Add(new() { LoginName = "batch-group-" + i });
        AccountQueryGrouping.ApplyBatch(batchDoc, batchDoc.Accounts.Select(x => x.Id).ToList(), [null, "success", "success", "failed", "cancel", "untouched"], batch);
        check(AccountGroups.Ids(batchDoc.Accounts[0]).Contains(batchDoc.Groups.Single(x => x.Name == "未查询").Id) &&
            AccountGroups.Ids(batchDoc.Accounts[3]).Contains(batchDoc.Groups.Single(x => x.Name == "失败").Id), "batch integration assigns skipped and failed accounts to distinct groups");
        check(batchDoc.Accounts.Where((_, index) => index is 1 or 2 or 4 or 5).All(x => x.GroupIds.Count == 0), "batch integration leaves successful duplicates and cancelled/unstarted records without failure labels");
        await store.UpdateAsync(current => AccountQueryGrouping.Apply(current, new Dictionary<Guid, AccountQueryDisposition> { [account.Id] = AccountQueryDisposition.Failed }));
        check((await store.LoadAsync()).Groups.Any(x => x.Name == "失败") && !AccountGroups.Matches((await store.LoadAsync()).Accounts[0], "default"), "query grouping persists and grouped accounts leave default");
    }
}
