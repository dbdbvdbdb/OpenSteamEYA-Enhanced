using OpenSteamEya.AccountManagement;

internal static class FeatureChecks
{
    internal static async Task RunAsync(Action<bool, string> check, string root)
    {
        var parsed = BatchAccountInput.Parse("\uFEFF user1 ---- password with spaces \r\nuser2----quote\"\\word----tail\n \t\ruser3----");
        check(parsed.Rows.Count == 3 && parsed.Issues.Count == 0, "paste batch accepts LF/CRLF/CR and blank lines without quotes");
        check(parsed.Rows[0].Account.LoginName == "user1" && parsed.Rows[0].Account.Password == " password with spaces ", "paste trims login but preserves password spaces");
        check(parsed.Rows[1].Account.Password == "quote\"\\word----tail", "paste preserves quotes backslashes and later separators");
        check(parsed.Rows[2].Account.Password == "", "paste supports unsaved password records");
        var invalid = BatchAccountInput.Parse("valid----safe\nsecret-password-without-separator\n----secret-value");
        check(invalid.Issues.Select(x => x.Line).SequenceEqual(new[] { 2, 3 }) && invalid.Rows.Count == 1, "paste validation retains source line numbers");
        check(invalid.Issues.All(x => !x.Reason.Contains("secret")), "paste validation never echoes input passwords");
        check(BatchAccountInput.Parse(ImportExport.TxtHeader).Issues.Count == 1, "paste mode does not interpret file format headers");
        var store = new AccountStore(Path.Combine(root, "batch-features"));
        await store.SaveAccountAsync(new() { LoginName = "user1", Password = "keep-original", Note = "keep-note", LinkedKey = "history:synthetic" });
        var before = (await store.LoadAsync()).Accounts[0];
        var result = await store.ImportAsync(parsed, DuplicatePolicy.Skip);
        var doc = await store.LoadAsync();
        check(result.Added == 2 && result.Skipped == 1 && doc.Accounts.Count == 3, "paste reuses atomic import with duplicate skip");
        check(doc.Accounts[0].Id == before.Id && doc.Accounts[0].Password == before.Password && doc.Accounts[0].LinkedKey == before.LinkedKey && doc.Accounts[0].Note == before.Note, "paste duplicate skip preserves password identity mapping and notes");
        check(doc.Accounts.First(x => x.LoginName == "user2").Password == "quote\"\\word----tail", "paste special password persists unchanged");
        var calls = new List<string>(); int active = 0, maximum = 0;
        var batch = await AccountQueryBatch.RunAsync(["first", null, "fail", "first", "last"], async (key, _) =>
        {
            active++; maximum = Math.Max(maximum, active); calls.Add(key);
            try { await Task.Delay(10); if (key == "fail") throw new InvalidOperationException("synthetic-private-token"); }
            finally { active--; }
        });
        check(calls.SequenceEqual(new[] { "first", "fail", "last" }) && maximum == 1, "selected queries are sequential and deduplicate credential identities");
        check(batch.Succeeded == 2 && batch.Failed == 1 && batch.Skipped == 2 && !batch.Cancelled, "selected queries skip missing credentials and continue after failure");
        using var cancellation = new CancellationTokenSource(); int cancellationCalls = 0;
        var cancelled = await AccountQueryBatch.RunAsync(["first", "second"], (_, token) =>
        { cancellationCalls++; cancellation.Cancel(); token.ThrowIfCancellationRequested(); return Task.CompletedTask; }, cancellation.Token);
        check(cancelled.Cancelled && cancellationCalls == 1 && cancelled.Failed == 0, "selected query cancellation stops the remaining queue");
        cancellationCalls = 0;
        var preCancelled = await AccountQueryBatch.RunAsync(["first"], (_, _) => { cancellationCalls++; return Task.CompletedTask; }, cancellation.Token);
        check(preCancelled.Cancelled && cancellationCalls == 0, "pre-cancelled query does not call Steam adapter");
    }
}
