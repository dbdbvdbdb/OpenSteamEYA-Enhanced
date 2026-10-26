using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenSteamEya.AccountManagement;

// Classification only: never stores passwords, tokens, or Steam login files.
public sealed class CategoryIdentity
{
    public string LoginName { get; set; } = "";
    public string SteamId { get; set; } = "";
    public static bool Matches(string name, string id, string otherName, string otherId) =>
        !string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(otherId)
            ? id == otherId
            : name.Equals(otherName, StringComparison.OrdinalIgnoreCase);
}

public sealed class CategoryDocument
{
    public int Version { get; set; } = 1;
    public bool AddCredentialsToWhite { get; set; } = true;
    public List<CategoryIdentity> BlackAccounts { get; set; } = [];
    public bool IsBlack(string name, string id) => BlackAccounts.Any(x => CategoryIdentity.Matches(name, id, x.LoginName, x.SteamId));
    public void Set(string name, string id, bool black)
    {
        BlackAccounts.RemoveAll(x => CategoryIdentity.Matches(name, id, x.LoginName, x.SteamId));
        if (black) BlackAccounts.Add(new() { LoginName = name, SteamId = id });
    }
}

public sealed class CategoryStore(string root)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    public string FilePath => Path.Combine(root, "account-categories.json");
    public async Task<CategoryDocument> LoadAsync()
    {
        await gate.WaitAsync();
        try { return await ReadAsync(); }
        finally { gate.Release(); }
    }
    private async Task<CategoryDocument> ReadAsync()
    {
        if (!File.Exists(FilePath)) return new();
        try
        {
            await using var stream = File.OpenRead(FilePath);
            var doc = await JsonSerializer.DeserializeAsync(stream, CategoryJsonContext.Default.CategoryDocument);
            Validate(doc); return doc!;
        }
        catch (JsonException) { throw new InvalidDataException("账号分类文件损坏，已中止操作，请恢复备份。"); }
    }
    public static void Validate(CategoryDocument? doc)
    {
        if (doc is null || doc.Version != 1 || doc.BlackAccounts is null || doc.BlackAccounts.Count > 100000 ||
            doc.BlackAccounts.Any(x => x is null || string.IsNullOrWhiteSpace(x.LoginName) || x.SteamId is null ||
                (x.SteamId.Length > 0 && (x.SteamId.Length != 17 || !ulong.TryParse(x.SteamId, out _)))))
            throw new InvalidDataException("账号分类文件结构无效。");
    }
    public async Task UpdateAsync(Action<CategoryDocument> update)
    {
        await gate.WaitAsync();
        try
        {
            Directory.CreateDirectory(root);
            // Cross-process writers must read after obtaining the same lease.
            await using var lease = await AcquireLeaseAsync();
            var doc = await ReadAsync(); update(doc); Validate(doc);
            var temp = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(doc, CategoryJsonContext.Default.CategoryDocument));
                if (File.Exists(FilePath)) File.Replace(temp, FilePath, null);
                else File.Move(temp, FilePath);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        finally { gate.Release(); }
    }
    private async Task<FileStream> AcquireLeaseAsync()
    {
        for (int i = 0; ; i++)
        {
            try { return new FileStream(Path.Combine(root, "categories.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (i < 200) { await Task.Delay(25); }
        }
    }
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, WriteIndented = true)]
[JsonSerializable(typeof(CategoryDocument))]
public sealed partial class CategoryJsonContext : JsonSerializerContext;
