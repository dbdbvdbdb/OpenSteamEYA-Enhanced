using System.IO.Compression;
using System.Text;
using OpenSteamEya.AccountManagement;

int passed = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception("FAILED: " + name); passed++; Console.WriteLine("PASS " + name); }
async Task Reject(Func<Task> operation, string name)
{ bool rejected = false; try { await operation(); } catch (InvalidDataException) { rejected = true; } Check(rejected, name); }
var root = Path.Combine(Path.GetTempPath(), "OpenSteamEya-Tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var store = new AccountStore(Path.Combine(root, "store"));
    var account = new ManagedAccount { LoginName = "unit-user", Password = " p----\\word,\"\r\n中文😀 ", SteamId = "76561198000000001", Note = "note,\"\n多行" };
    await store.SaveAccountAsync(account);
    var loaded = await new AccountStore(store.Root).LoadAsync();
    Check(loaded.Accounts.Single().Password == account.Password, "plaintext and restart persistence");
    Check((await File.ReadAllTextAsync(store.FilePath)).Contains("password"), "separate local JSON store");
    account.Password = "changed"; await store.SaveAccountAsync(account);
    Check((await store.LoadAsync()).Accounts.Single().Password == "changed", "edit password");
    await Reject(() => store.SaveAccountAsync(new() { LoginName = "UNIT-USER" }), "case insensitive duplicate rejection");
    for (int n = 0; n < 100; n++)
        await store.SaveAccountAsync(new() { LoginName = "test-" + n, Password = n == 0 ? "" : " p----\\,\"\n\r中文 " + n, Nickname = "昵称" + n, Note = "备注\n\"文本", SteamId = "" });
    var items = (await store.LoadAsync()).Accounts;
    foreach (var format in new[] { "txt", "csv", "json" })
    {
        var exported = ImportExport.Export(items, format);
        var preview = ImportExport.Parse(exported, format);
        Check(preview.Issues.Count == 0 && preview.Rows.Count == 101, format + " 100 accounts roundtrip");
        Check(preview.Rows.Zip(items).All(pair => pair.First.Account.Password == pair.Second.Password), format + " exact special characters and empty passwords");
        Check(preview.Rows.Zip(items).All(pair => pair.First.Account.Note == pair.Second.Note && pair.First.Account.SteamId == pair.Second.SteamId), format + " metadata roundtrip");
        var destination = new AccountStore(Path.Combine(root, "roundtrip-" + format));
        var result = await destination.ImportAsync(preview, DuplicatePolicy.Skip);
        Check(result.Added == 101, format + " import result");
    }
    var csv = ImportExport.Parse("loginName,password,note\r\nu,\"a,b\"\"c\r\nx\",\"n\"\r\n", "csv");
    Check(csv.Rows.Single().Account.Password == "a,b\"c\r\nx", "CSV RFC4180 multiline and escaped quotes");
    var badCsv = ImportExport.Parse("loginName,password\nu,\"unterminated", "csv");
    Check(badCsv.Rows.Count == 0 && badCsv.Issues.Single().Line == 2, "CSV invalid structure row and safe preview");
    var txt = ImportExport.Parse("a----pw----tail\nmissing\nb----\n", "txt");
    Check(txt.Rows.Count == 2 && txt.Rows[0].Account.Password == "pw----tail" && txt.Issues.Single().Line == 2, "legacy TXT first delimiter and error line");
    Check(ImportExport.Parse("[{\"loginName\":\"x\",\"password\":null}]", "json").Issues.Count == 1, "JSON null password rejection");
    Check(ImportExport.Parse("[{\"loginName\":null,\"password\":\"x\"}]", "json").Issues.Count == 1, "JSON null login rejection");
    Check(ImportExport.Parse("loginName,password,password\nx,a,b", "csv").Rows.Count == 0, "CSV duplicate headers rejected");
    Check(ImportExport.Parse("[{\"loginName\":\"x\",\"password\":7}]", "json").Issues.All(x => !x.Reason.Contains("7")), "parser errors contain no input data");
    var repeat = ImportExport.Parse("test-0----fill\ntest-1----overwrite\nnew----new\nnew----second", "txt");
    var fill = await store.ImportAsync(repeat, DuplicatePolicy.FillMissing);
    Check(fill.Added == 1 && fill.Updated == 1 && fill.Skipped == 2, "duplicate fill missing including file duplicates");
    Check((await store.LoadAsync()).Accounts.Single(x => x.LoginName == "test-0").Password == "fill", "fill empty password");
    var skip = await store.ImportAsync(repeat, DuplicatePolicy.Skip); Check(skip.Skipped == 4, "skip duplicates");
    var overwrite = await store.ImportAsync(repeat, DuplicatePolicy.Overwrite); Check(overwrite.Updated == 4, "overwrite duplicates");

    var identity = new ManagedAccount { LoginName = "original", SteamId = "76561198000000001", Nickname = "same name" };
    ExistingIdentity[] identities = [new("h:1", "original", "76561198000000001"), new("h:2", "different", "76561198000000002")];
    Check(AccountMapping.Resolve(identity, identities).Key == "h:1", "SteamID mapping");
    identity.SteamId = ""; Check(AccountMapping.Resolve(identity, identities).Key == "h:1", "login name mapping");
    identity.LoginName = "unrelated"; Check(AccountMapping.Resolve(identity, identities).Key is null, "never match nickname");
    identity.LoginName = "original"; identity.SteamId = "76561198000000002";
    Check(AccountMapping.Resolve(identity, identities).Conflict && AccountMapping.Resolve(identity, identities).Key is null, "conflicting identity never auto links");
    identity.SteamId = "";
    Check(AccountMapping.Resolve(identity, [new("a", "original", ""), new("b", "original", "")]).Conflict, "multiple candidates require confirmation");
    identity.LinkedKey = "h:2"; Check(AccountMapping.Resolve(identity, identities).Key == "h:2", "manual mapping");

    var group = new ManagedGroup { Name = "分组", Order = 2 };
    await store.UpdateAsync(doc => { doc.Groups.Add(group); doc.Accounts[0].GroupId = group.Id; doc.Accounts[0].Favorite = true;
        doc.Accounts[0].LinkedKey = "history:id:76561198000000001"; doc.Preferences.View = "list"; doc.Preferences.Sort = "nickname"; });
    var groupItems = (await store.LoadAsync()).Accounts.Where(x => x.GroupId == group.Id).ToList();
    Check(ImportExport.Parse(ImportExport.Export(groupItems, "json"), "json").Rows.Count == 1, "group range export");
    var selection = items.Take(3).ToList(); Check(ImportExport.Parse(ImportExport.Export(selection, "csv"), "csv").Rows.Count == 3, "selected range export");
    var portable = ImportExport.Parse(ImportExport.Export(groupItems, "json", false, false), "json").Rows.Single().Account;
    Check(portable.Note == "" && portable.SteamId == "" && portable.LinkedKey is null, "optional metadata and mapping isolation in exports");
    Directory.CreateDirectory(store.ImageDirectory); await File.WriteAllBytesAsync(Path.Combine(store.ImageDirectory, "test.png"), [1, 2, 3]);
    await store.UpdateAsync(doc => doc.Accounts[0].Background = new() { Mode = "local", ImageFile = "test.png" });
    var backup = new BackupService(store); var path = Path.Combine(root, "backup.zip"); await backup.CreateAsync(path);
    using (var archive = ZipFile.OpenRead(path))
        Check(archive.Entries.Count == 2 && !archive.Entries.Any(x => x.FullName.Contains("cached-login") || x.FullName.Contains("token")), "backup only manager and custom resources");
    var previewBackup = await backup.PreviewAsync(path);
    Check(previewBackup.Duplicates == 102 && previewBackup.Images.Count == 1, "backup preview counts");
    var restored = new AccountStore(Path.Combine(root, "restored")); var restorer = new BackupService(restored);
    await restorer.RestoreAsync(previewBackup, RestoreMode.Replace, DuplicatePolicy.Overwrite);
    var restoredDoc = await restored.LoadAsync();
    Check(restoredDoc.Accounts.Count == 102 && restoredDoc.Groups.Count == 1 && restoredDoc.Preferences.View == "list", "replace restore all metadata and settings");
    Check(restoredDoc.Accounts[0].Favorite && restoredDoc.Accounts[0].LinkedKey == "history:id:76561198000000001" &&
        File.Exists(Path.Combine(restored.ImageDirectory, restoredDoc.Accounts[0].Background.ImageFile!)), "restore favorite mapping and renamed image");
    await restorer.RestoreAsync(previewBackup, RestoreMode.Merge, DuplicatePolicy.Skip);
    Check((await restored.LoadAsync()).Accounts.Count == 102, "merge restore deduplicates by login");
    await store.DeleteGroupAsync(group.Id);
    Check((await store.LoadAsync()).Accounts.Count == 102 && (await store.LoadAsync()).Accounts[0].GroupId is null, "delete group retains accounts in default");
    await store.DeleteAccountAsync(account.Id); Check((await store.LoadAsync()).Accounts.Count == 101, "delete manager record only");

    var malicious = Path.Combine(root, "malicious.zip");
    using (var archive = ZipFile.Open(malicious, ZipArchiveMode.Create))
    {
        archive.CreateEntryFromFile(restored.FilePath, "manager.json");
        using var output = archive.CreateEntry("backgrounds/../../outside.png").Open(); output.WriteByte(1);
    }
    var before = await File.ReadAllBytesAsync(restored.FilePath);
    await Reject(async () => { await restorer.PreviewAsync(malicious); }, "ZIP path traversal rejected");
    var afterFailedRestore = await File.ReadAllBytesAsync(restored.FilePath);
    Check(Enumerable.SequenceEqual(before, afterFailedRestore), "failed restore leaves database intact");
    await File.WriteAllTextAsync(restored.FilePath, "broken secret password");
    await Reject(() => restored.SaveAccountAsync(new() { LoginName = "x" }), "corrupt database cannot be overwritten");
    Check(await File.ReadAllTextAsync(restored.FilePath) == "broken secret password", "corrupt file preserved");
    var corruptPreview = await restorer.PreviewAsync(path);
    Check(corruptPreview.CurrentUnreadable, "backup preview detects corrupt current database");
    await restorer.RestoreAsync(corruptPreview, RestoreMode.Replace, DuplicatePolicy.Overwrite);
    Check((await restored.LoadAsync()).Accounts.Count == 102, "valid backup repairs corrupt manager store");
    await File.WriteAllBytesAsync(restored.FilePath, before);
    await Reject(() => restored.UpdateAsync(doc => doc.Version = 999), "unknown version rejected");
    var afterFailedTransaction = await File.ReadAllBytesAsync(restored.FilePath);
    Check(Enumerable.SequenceEqual(before, afterFailedTransaction), "invalid transaction atomic rollback");
    await Task.WhenAll(Enumerable.Range(0, 25).Select(n => new AccountStore(store.Root).SaveAccountAsync(new() { LoginName = "parallel-" + n })));
    Check((await store.LoadAsync()).Accounts.Count == 126, "concurrent read modify write serialized");
    await FeatureChecks.RunAsync(Check, root);
    await GroupStatusChecks.RunAsync(Check, root);
    Console.WriteLine($"All {passed} checks passed. Only synthetic test accounts used.");
}
finally
{
    var fullRoot = Path.GetFullPath(root); var tempRoot = Path.GetFullPath(Path.GetTempPath());
    if (fullRoot.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(fullRoot).StartsWith("OpenSteamEya-Tests-"))
        Directory.Delete(fullRoot, true);
}
