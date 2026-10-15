using System.IO.Compression;
using OpenSteamEya.AccountManagement;

internal static class CategoryChecks
{
    internal static async Task RunAsync(Action<bool, string> check, string root)
    {
        var store = new AccountStore(Path.Combine(root, "categories"));
        var categories = new CategoryStore(store.Root);
        var original = new ManagedAccount { LoginName = "synthetic-old", SteamId = "76561198000000401", Password = "synthetic-password", Note = "retain", Favorite = true };
        await store.SaveAccountAsync(original);
        var bytes = await File.ReadAllBytesAsync(store.FilePath);
        check((await categories.LoadAsync()).AddCredentialsToWhite, "new checkbox preference defaults checked");
        check(!(await categories.LoadAsync()).IsBlack(original.LoginName, original.SteamId), "legacy records remain white without migration");
        await categories.UpdateAsync(x => { x.Set(original.LoginName, original.SteamId, true); x.AddCredentialsToWhite = false; });
        var restarted = await new CategoryStore(store.Root).LoadAsync();
        check(restarted.IsBlack(original.LoginName, original.SteamId) && !restarted.AddCredentialsToWhite, "black membership and checkbox survive restart");
        var afterClassification = await File.ReadAllBytesAsync(store.FilePath);
        check(Enumerable.SequenceEqual(bytes, afterClassification), "classification leaves password JSON byte-for-byte unchanged");
        check(restarted.IsBlack("changed-login", original.SteamId), "classification uses SteamID before login name");
        check(!restarted.IsBlack(original.LoginName, "76561198000000402"), "known conflicting SteamID cannot match by name");
        await categories.UpdateAsync(x => x.Set(original.LoginName.ToUpperInvariant(), original.SteamId, true));
        check((await categories.LoadAsync()).BlackAccounts.Count == 1, "repeated classification deduplicates stable identity");
        await Task.WhenAll(Enumerable.Range(0, 12).Select(n => new CategoryStore(store.Root).UpdateAsync(x => x.Set("synthetic-" + n, "", true))));
        check((await categories.LoadAsync()).BlackAccounts.Count == 13, "category updates serialize across store instances");
        var zip = Path.Combine(root, "classification-backup.zip");
        await new BackupService(store).CreateAsync(zip);
        var destination = new AccountStore(Path.Combine(root, "classification-restored"));
        var backup = new BackupService(destination);
        await backup.RestoreAsync(await backup.PreviewAsync(zip), RestoreMode.Replace, DuplicatePolicy.Overwrite);
        var destCategories = new CategoryStore(destination.Root);
        check((await destCategories.LoadAsync()).IsBlack(original.LoginName, original.SteamId), "backup restores black classification");
        var restored = (await destination.LoadAsync()).Accounts.Single();
        check(restored.Password == original.Password && restored.Note == original.Note && restored.Favorite, "backup preserves password and user metadata");
        await destCategories.UpdateAsync(x => x.Set(original.LoginName, original.SteamId, false));
        await backup.RestoreAsync(await backup.PreviewAsync(zip), RestoreMode.Merge, DuplicatePolicy.Overwrite);
        check(!(await destCategories.LoadAsync()).IsBlack(original.LoginName, original.SteamId), "merge preserves existing explicit category");
        var legacy = Path.Combine(root, "legacy-classification-backup.zip");
        using (var archive = ZipFile.Open(legacy, ZipArchiveMode.Create)) archive.CreateEntryFromFile(store.FilePath, "manager.json");
        await backup.RestoreAsync(await backup.PreviewAsync(legacy), RestoreMode.Replace, DuplicatePolicy.Overwrite);
        check((await destCategories.LoadAsync()).BlackAccounts.Count == 0, "legacy backup restores records to white");
        await File.WriteAllTextAsync(categories.FilePath, "broken-synthetic");
        bool rejected = false;
        try { await categories.UpdateAsync(x => x.BlackAccounts.Clear()); } catch (InvalidDataException) { rejected = true; }
        check(rejected && await File.ReadAllTextAsync(categories.FilePath) == "broken-synthetic", "corrupt category file cannot be overwritten");
    }
}
