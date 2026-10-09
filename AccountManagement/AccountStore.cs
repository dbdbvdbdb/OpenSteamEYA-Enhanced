using System.Text;
using System.Text.Json;

namespace OpenSteamEya.AccountManagement;

/// <summary>Local plaintext store. Never logs data and never calls a network service.</summary>
public sealed class AccountStore
{
    private readonly SemaphoreSlim gate = new(1, 1);
    public string Root { get; }
    public string FilePath => Path.Combine(Root, "manager.json");
    public string ImageDirectory => Path.Combine(Root, "backgrounds");
    public AccountStore(string? root = null) => Root = root ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenSteamEYA-Enhanced");

    public async Task<ManagerDocument> LoadAsync()
    {
        await gate.WaitAsync();
        try { await using var lease = await AcquireLeaseAsync(); return await ReadAsync(); }
        finally { gate.Release(); }
    }

    public async Task UpdateAsync(Action<ManagerDocument> update)
    {
        await gate.WaitAsync();
        try
        {
            await using var lease = await AcquireLeaseAsync();
            var doc = await ReadAsync();
            update(doc);
            Validate(doc);
            await WriteAsync(doc);
        }
        finally { gate.Release(); }
    }

    // A valid backup can replace even an unreadable manager database. No original credential store is accessed.
    public async Task ReplaceAsync(ManagerDocument replacement)
    {
        var snapshot = Clone(replacement); Validate(snapshot);
        await gate.WaitAsync();
        try { await using var lease = await AcquireLeaseAsync(); await WriteAsync(snapshot); }
        finally { gate.Release(); }
    }

    private async Task<FileStream> AcquireLeaseAsync()
    {
        Directory.CreateDirectory(Root);
        var lockPath = Path.Combine(Root, "manager.lock");
        for (int attempt = 0; ; attempt++)
        {
            try { return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.Asynchronous); }
            catch (IOException) when (attempt < 200) { await Task.Delay(25); }
        }
    }

    public Task SaveAccountAsync(ManagedAccount account) => UpdateAsync(doc =>
    {
        account.LoginName = account.LoginName.Trim();
        if (doc.Accounts.Any(x => x.Id != account.Id && x.LoginName.Equals(account.LoginName, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("已存在该登录账号，请编辑已有记录。");
        var index = doc.Accounts.FindIndex(x => x.Id == account.Id);
        if (index >= 0) doc.Accounts[index] = account;
        else { account.Order = doc.Accounts.Count; doc.Accounts.Add(account); }
    });

    public Task DeleteAccountAsync(Guid id) => UpdateAsync(doc => doc.Accounts.RemoveAll(x => x.Id == id));
    public Task DeleteGroupAsync(Guid id) => UpdateAsync(doc =>
    {
        doc.Groups.RemoveAll(x => x.Id == id);
        foreach (var account in doc.Accounts) AccountGroups.Set(account, AccountGroups.Ids(account).Where(group => group != id));
    });

    public async Task<ImportResult> ImportAsync(ImportPreview preview, DuplicatePolicy policy)
    {
        int added = 0, updated = 0, skipped = 0;
        await UpdateAsync(doc =>
        {
            foreach (var row in preview.Rows)
            {
                var incoming = Clone(row.Account);
                var existing = doc.Accounts.FirstOrDefault(x => x.LoginName.Equals(incoming.LoginName, StringComparison.OrdinalIgnoreCase));
                if (existing is null)
                {
                    incoming.Id = Guid.NewGuid(); incoming.Order = doc.Accounts.Count;
                    incoming.LinkedKey = null; AccountGroups.Set(incoming, []);
                    doc.Accounts.Add(incoming); added++; continue;
                }
                if (policy == DuplicatePolicy.Skip || policy == DuplicatePolicy.FillMissing && existing.Password.Length > 0)
                { skipped++; continue; }
                // Imports only update the password: do not silently change an existing account's identity/mapping.
                existing.Password = incoming.Password; updated++;
            }
        });
        return new(added, updated, skipped, preview.Issues.Count);
    }

    private async Task<ManagerDocument> ReadAsync()
    {
        if (!File.Exists(FilePath)) return new();
        try
        {
            await using var stream = File.OpenRead(FilePath);
            var document = await JsonSerializer.DeserializeAsync(stream, ManagerJsonContext.Default.ManagerDocument)
                ?? throw new InvalidDataException();
            Validate(document); return document;
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException)
        { throw new InvalidDataException("密码管理数据格式损坏或版本不支持；已中止写入，请从备份恢复。"); }
    }

    private async Task WriteAsync(ManagerDocument doc)
    {
        Directory.CreateDirectory(Root);
        var temp = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var json = JsonSerializer.Serialize(doc, ManagerJsonContext.Default.ManagerDocument);
            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                var bytes = Encoding.UTF8.GetBytes(json);
                await stream.WriteAsync(bytes); await stream.FlushAsync(); stream.Flush(true);
            }
            if (File.Exists(FilePath)) File.Replace(temp, FilePath, null);
            else File.Move(temp, FilePath);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    public static ManagedAccount Clone(ManagedAccount account) => JsonSerializer.Deserialize(
        JsonSerializer.Serialize(account, ManagerJsonContext.Default.ManagedAccount), ManagerJsonContext.Default.ManagedAccount)!;
    public static ManagerDocument Clone(ManagerDocument doc) => JsonSerializer.Deserialize(
        JsonSerializer.Serialize(doc, ManagerJsonContext.Default.ManagerDocument), ManagerJsonContext.Default.ManagerDocument)!;

    public static void Validate(ManagerDocument doc)
    {
        if (doc.Version != 1 || doc.Accounts is null || doc.Groups is null || doc.Preferences is null || doc.Accounts.Count > 100000)
            throw new InvalidDataException("不支持的数据版本或结构。");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ids = new HashSet<Guid>(); var groupIds = new HashSet<Guid>();
        foreach (var group in doc.Groups)
            if (group is null || group.Id == Guid.Empty || !groupIds.Add(group.Id) || string.IsNullOrWhiteSpace(group.Name))
                throw new InvalidDataException("分组结构无效。");
        foreach (var account in doc.Accounts)
        {
            if (account is null || account.Id == Guid.Empty || !ids.Add(account.Id) ||
                string.IsNullOrWhiteSpace(account.LoginName) || !names.Add(account.LoginName.Trim()) ||
                account.Password is null || account.Nickname is null || account.Note is null || account.SteamId is null || account.Background is null)
                throw new InvalidDataException("账号结构无效或存在重复登录账号。");
            if (account.SteamId.Length > 0 && (account.SteamId.Length != 17 || !ulong.TryParse(account.SteamId, out _)))
                throw new InvalidDataException("SteamID 应为 17 位数字。");
            if (AccountGroups.Ids(account).Any(id => !groupIds.Contains(id)))
                throw new InvalidDataException("账号引用了不存在的分组。");
            AccountGroups.Set(account, AccountGroups.Ids(account));
            if (account.Background.ImageFile is { } image && !IsImageName(image))
                throw new InvalidDataException("背景资源名无效。");
        }
        var prefs = doc.Preferences;
        if (!double.IsFinite(prefs.Brightness) || !double.IsFinite(prefs.Blur) || !double.IsFinite(prefs.MonetStrength))
            throw new InvalidDataException("背景参数无效。");
        prefs.Brightness = Math.Clamp(prefs.Brightness, 0.1, 0.9);
        prefs.Blur = Math.Clamp(prefs.Blur, 0, 32); prefs.MonetStrength = Math.Clamp(prefs.MonetStrength, 0, 1);
    }

    public static bool IsImageName(string name) => name.Length < 120 && name == Path.GetFileName(name) &&
        !name.Contains(':') && !name.Contains('/') && !name.Contains('\\') &&
        new[] { ".png", ".jpg", ".jpeg", ".webp" }.Contains(Path.GetExtension(name).ToLowerInvariant());
}
