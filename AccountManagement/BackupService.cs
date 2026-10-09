using System.IO.Compression;
using System.Text.Json;

namespace OpenSteamEya.AccountManagement;

public sealed record BackupPreview(ManagerDocument Document, IReadOnlyDictionary<string, byte[]> Images, int Duplicates, bool CurrentUnreadable = false);

public sealed class BackupService(AccountStore store)
{
    private const long MaxBytes = 256 * 1024 * 1024;
    public async Task CreateAsync(string path)
    {
        var doc = await store.LoadAsync();
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var archive = ZipFile.Open(temp, ZipArchiveMode.Create))
            {
                await using (var stream = archive.CreateEntry("manager.json").Open())
                    await JsonSerializer.SerializeAsync(stream, doc, ManagerJsonContext.Default.ManagerDocument);
                foreach (var image in doc.Accounts.Select(x => x.Background.ImageFile).OfType<string>().Distinct())
                {
                    var source = Path.Combine(store.ImageDirectory, image);
                    if (File.Exists(source)) archive.CreateEntryFromFile(source, "backgrounds/" + image);
                }
            }
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    public async Task<BackupPreview> PreviewAsync(string path)
    {
        try
        {
            using var archive = ZipFile.OpenRead(path);
            if (archive.Entries.Count > 10001 || archive.Entries.Sum(x => x.Length) > MaxBytes ||
                archive.Entries.GroupBy(x => x.FullName, StringComparer.OrdinalIgnoreCase).Any(x => x.Count() > 1))
                throw new InvalidDataException();
            var entry = archive.GetEntry("manager.json") ?? throw new InvalidDataException();
            if (entry.Length > 32 * 1024 * 1024) throw new InvalidDataException();
            ManagerDocument doc;
            await using (var stream = entry.Open())
                doc = await JsonSerializer.DeserializeAsync(stream, ManagerJsonContext.Default.ManagerDocument) ?? throw new InvalidDataException();
            AccountStore.Validate(doc);
            var images = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var resource in archive.Entries.Where(x => x.FullName != "manager.json"))
            {
                if (!resource.FullName.StartsWith("backgrounds/", StringComparison.Ordinal) ||
                    !AccountStore.IsImageName(resource.FullName[12..]) || resource.Length > 20 * 1024 * 1024)
                    throw new InvalidDataException();
                using var bytes = new MemoryStream(); await using var source = resource.Open(); await source.CopyToAsync(bytes);
                images.Add(resource.FullName[12..], bytes.ToArray());
            }
            foreach (var account in doc.Accounts)
                if (account.Background.ImageFile is { } name && !images.ContainsKey(name))
                    account.Background.ImageFile = null; // missing images fall back to Monet
            ManagerDocument current;
            try { current = await store.LoadAsync(); }
            catch (InvalidDataException) { return new(doc, images, 0, true); }
            return new(doc, images, doc.Accounts.Count(x => current.Accounts.Any(y => y.LoginName.Equals(x.LoginName, StringComparison.OrdinalIgnoreCase))));
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException)
        { throw new InvalidDataException("备份版本、结构或资源无效，未修改任何数据。"); }
    }

    public async Task RestoreAsync(BackupPreview preview, RestoreMode mode, DuplicatePolicy policy)
    {
        // Revalidate caller-held preview and stage immutable resource names. Database commit happens last.
        var incoming = AccountStore.Clone(preview.Document); AccountStore.Validate(incoming);
        var staged = new List<string>(); var renames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Directory.CreateDirectory(store.ImageDirectory);
        bool committed = false;
        try
        {
            foreach (var (name, bytes) in preview.Images)
            {
                if (!AccountStore.IsImageName(name) || bytes.Length > 20 * 1024 * 1024) throw new InvalidDataException("背景资源无效。");
                var newName = Guid.NewGuid().ToString("N") + Path.GetExtension(name).ToLowerInvariant();
                var destination = Path.Combine(store.ImageDirectory, newName); staged.Add(destination);
                await File.WriteAllBytesAsync(destination, bytes); renames[name] = newName;
            }
            foreach (var account in incoming.Accounts)
                if (account.Background.ImageFile is { } name)
                    account.Background.ImageFile = renames.GetValueOrDefault(name);
            if (mode == RestoreMode.Replace)
            {
                await store.ReplaceAsync(incoming);
                committed = true; return;
            }
            await store.UpdateAsync(doc =>
            {
                var groupMap = new Dictionary<Guid, Guid>();
                foreach (var group in incoming.Groups)
                {
                    var same = doc.Groups.FirstOrDefault(x => x.Name.Equals(group.Name, StringComparison.OrdinalIgnoreCase));
                    if (same is null)
                    { same = new() { Name = group.Name, Order = doc.Groups.Count }; doc.Groups.Add(same); }
                    groupMap[group.Id] = same.Id;
                }
                foreach (var account in incoming.Accounts)
                {
                    AccountGroups.Set(account, AccountGroups.Ids(account).Select(id => groupMap[id]));
                    var same = doc.Accounts.FirstOrDefault(x => x.LoginName.Equals(account.LoginName, StringComparison.OrdinalIgnoreCase));
                    if (same is null)
                    { account.Id = Guid.NewGuid(); account.Order = doc.Accounts.Count; doc.Accounts.Add(account); }
                    else if (policy == DuplicatePolicy.Overwrite)
                    { account.Id = same.Id; doc.Accounts[doc.Accounts.IndexOf(same)] = account; }
                    else if (policy == DuplicatePolicy.FillMissing && same.Password.Length == 0) same.Password = account.Password;
                }
            });
            committed = true;
        }
        finally
        {
            if (!committed) foreach (var path in staged) if (File.Exists(path)) File.Delete(path);
        }
    }
}
