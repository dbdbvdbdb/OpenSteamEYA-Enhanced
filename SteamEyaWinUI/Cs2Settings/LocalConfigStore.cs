using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SteamEyaWinUI.Cs2Settings;

internal sealed record LocalProfile(uint AccountId, string DirectoryPath)
{
    public string SteamId => (76561197960265728UL + AccountId).ToString();
    public string Display { get; set; } = "Steam 账号 " + (76561197960265728UL + AccountId);
    public override string ToString() => Display;
}
internal sealed record ConfigFile(string Path, byte[] Bytes, ConfigText Document);
internal sealed record ConfigSnapshot(LocalProfile Profile, Dictionary<string, ConfigFile> Files);
internal sealed class SettingValue(SettingSpec spec, string file, string key, string original)
{
    public SettingSpec Spec { get; } = spec;
    public string File { get; } = file;
    public string Key { get; } = key;
    public string Original { get; } = original;
    public string Value { get; set; } = spec.Boolean ? original is "1" or "true" ? "true" : "false" : original;
    public bool Dirty { get; set; }
}
internal sealed record ConfigCloudFile(string Name, byte[] Data);
internal sealed record ConfigSaveResult(string BackupDirectory, int FileCount, List<ConfigCloudFile> CloudFiles, string? CloudNote);
internal sealed record ConfigBackupManifest(uint AccountId, Dictionary<string, string> Sha256);

internal sealed class LocalConfigStore(string steamRoot, string backupRoot, Func<bool>? isRunning = null)
{
    internal const string UserFile = "cs2_user_convars_0_slot0.vcfg";
    internal const string KeysFile = "cs2_user_keys_0_slot0.vcfg";
    internal const string MachineFile = "cs2_machine_convars.vcfg";
    internal const string VideoFile = "cs2_video.txt";
    internal static readonly string[] FileNames = [UserFile, KeysFile, MachineFile, VideoFile];
    private static readonly object WriteGate = new();
    private readonly string _root = Path.GetFullPath(steamRoot);
    private readonly string _backups = Path.GetFullPath(backupRoot);
    private readonly Func<bool> _running = isRunning ?? GameRunning;
    public static bool GameRunning()
    {
        var processes = Process.GetProcessesByName("cs2");
        try { return processes.Length != 0; } finally { foreach (var process in processes) process.Dispose(); }
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static void RejectLinks(string path)
    {
        for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
            if ((System.IO.File.Exists(current) || Directory.Exists(current)) && (System.IO.File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("配置路径包含链接，请选择真实 Steam 目录。");
    }
    private string ConfigDirectory(uint id) => Path.Combine(_root, "userdata", id.ToString(), "730", "local", "cfg");
    private void ValidateProfile(LocalProfile profile)
    {
        if (profile.AccountId == 0 || !string.Equals(Path.GetFullPath(profile.DirectoryPath), ConfigDirectory(profile.AccountId), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("账号配置路径不匹配。");
        RejectLinks(profile.DirectoryPath);
    }
    public List<LocalProfile> GetProfiles()
    {
        var userdata = Path.Combine(_root, "userdata"); RejectLinks(userdata);
        if (!Directory.Exists(userdata)) return [];
        var profiles = new List<LocalProfile>();
        foreach (var dir in Directory.EnumerateDirectories(userdata))
            if (uint.TryParse(Path.GetFileName(dir), out uint id) && id > 0)
            {
                var path = ConfigDirectory(id);
                if (FileNames.Any(name => System.IO.File.Exists(Path.Combine(path, name)))) profiles.Add(new(id, path));
            }
        return profiles.OrderBy(p => p.AccountId).ToList();
    }
    private static ConfigFile ReadFile(string path)
    {
        RejectLinks(path);
        if (new FileInfo(path).Length > 2 * 1024 * 1024) throw new InvalidDataException("配置文件过大。");
        var bytes = System.IO.File.ReadAllBytes(path);
        return new(path, bytes, new ConfigText(bytes));
    }
    public ConfigSnapshot Load(LocalProfile profile)
    {
        ValidateProfile(profile);
        var files = new Dictionary<string, ConfigFile>();
        foreach (var name in FileNames)
        {
            var path = Path.Combine(profile.DirectoryPath, name);
            if (System.IO.File.Exists(path)) files[name] = ReadFile(path);
        }
        if (files.Count == 0) throw new InvalidDataException("没有可编辑的现有配置。请先让游戏为此账号生成配置。");
        return new(profile, files);
    }
    public static List<SettingValue> GetSettings(ConfigSnapshot snapshot)
    {
        var rows = new List<SettingValue>();
        foreach (var (file, data) in snapshot.Files.Where(f => f.Key != KeysFile))
            foreach (var entry in data.Document.Entries.Where(e => e.Section == (file == VideoFile ? "video.cfg" : "config/convars")))
            {
                var spec = SettingCatalog.All.FirstOrDefault(s => s.Key == SettingCatalog.BaseKey(entry.Key));
                if (spec is not null) { data.Document.Find(entry.Section, entry.Key); rows.Add(new(spec, file, entry.Key, entry.Value)); }
            }
        return rows;
    }
    public ConfigSaveResult Save(ConfigSnapshot snapshot, IReadOnlyList<SettingValue> settings, IReadOnlyDictionary<string, string> bindings)
    {
        var changes = new Dictionary<string, Dictionary<string, string>>();
        foreach (var row in settings.Where(r => r.Dirty))
        {
            if (!snapshot.Files.TryGetValue(row.File, out var file) || !SettingCatalog.All.Contains(row.Spec) || SettingCatalog.BaseKey(row.Key) != row.Spec.Key)
                throw new InvalidDataException("设置不属于当前配置。");
            var section = row.File == VideoFile ? "video.cfg" : "config/convars";
            if (file.Document.Find(section, row.Key)?.Value != row.Original) throw new InvalidDataException("设置快照已失效。");
            var value = row.Spec.Validate(row.Value, row.Original);
            if (value == row.Original) continue;
            if (!changes.TryGetValue(row.File, out var values)) changes[row.File] = values = [];
            values.Add(row.Key, value);
        }
        if (bindings.Count > 0)
        {
            if (!snapshot.Files.ContainsKey(KeysFile)) throw new InvalidDataException("此账号没有现有键位文件。");
            foreach (var (key, value) in bindings)
                if (!SettingCatalog.BindKeys.Contains(key, StringComparer.OrdinalIgnoreCase) || !SettingCatalog.BindActions.Any(a => a.Value == value))
                    throw new InvalidDataException("键位或动作不支持。");
            // Reuse the original key casing; never create competing W/w bindings.
            var values = new Dictionary<string, string>();
            foreach (var (key, value) in bindings)
            {
                var existing = snapshot.Files[KeysFile].Document.Entries.Where(e => e.Section == "config/bindings" && e.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (existing.Length > 1) throw new InvalidDataException("键位存在大小写重复，请先在游戏内修复。");
                if (existing.SingleOrDefault()?.Value != value) values.Add(existing.SingleOrDefault()?.Key ?? key, value);
            }
            if (values.Count > 0) changes[KeysFile] = values;
        }
        var replacements = changes.ToDictionary(p => p.Key, p => snapshot.Files[p.Key].Document.Patch(
            p.Key == VideoFile ? "video.cfg" : p.Key == KeysFile ? "config/bindings" : "config/convars", p.Value, p.Key == KeysFile));
        if (replacements.Count == 0) throw new InvalidOperationException("没有需要保存的修改。");
        var cloud = BuildCloud(snapshot, changes);
        var backup = Commit(snapshot, replacements);
        return new(backup, replacements.Count, cloud.Files, cloud.Note);
    }
    private (List<ConfigCloudFile> Files, string? Note) BuildCloud(ConfigSnapshot snapshot, Dictionary<string, Dictionary<string, string>> changes)
    {
        var result = new List<ConfigCloudFile>(); var notes = new List<string>();
        foreach (var (local, remote) in new[] { (UserFile, "cs2_user_convars.vcfg"), (KeysFile, "cs2_user_keys.vcfg") })
        {
            if (!changes.TryGetValue(local, out var values)) continue;
            var path = Path.Combine(_root, "userdata", snapshot.Profile.AccountId.ToString(), "730", "remote", remote);
            try
            {
                if (!System.IO.File.Exists(path)) { notes.Add("缺少已有云镜像，未推送 " + remote); continue; }
                var source = ReadFile(path);
                var cloudValues=values;
                if(local==KeysFile)
                {
                    cloudValues=new Dictionary<string,string>();
                    foreach(var (key,value) in values)
                    {
                        var existing=source.Document.Entries.Where(e=>e.Section=="config/bindings" && e.Key.Equals(key,StringComparison.OrdinalIgnoreCase)).ToArray();
                        if(existing.Length>1)throw new InvalidDataException("云键位存在大小写重复。");
                        cloudValues.Add(existing.SingleOrDefault()?.Key ?? key,value);
                    }
                }
                result.Add(new(remote, source.Document.Patch(local == KeysFile ? "config/bindings" : "config/convars", cloudValues, true)));
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.DecoderFallbackException)
            { notes.Add("云镜像无法安全读取：" + remote); }
        }
        return (result, notes.Count == 0 ? null : string.Join("；", notes));
    }
    private void EnsureUnchanged(ConfigFile file)
    {
        RejectLinks(file.Path);
        if (!System.IO.File.Exists(file.Path) || new FileInfo(file.Path).Length != file.Bytes.Length || Hash(System.IO.File.ReadAllBytes(file.Path)) != Hash(file.Bytes))
            throw new IOException("配置已被游戏或 Steam 修改，请重新读取后再保存。");
    }
    private string Commit(ConfigSnapshot snapshot, Dictionary<string, byte[]> replacements)
    {
        lock (WriteGate)
        {
            ValidateProfile(snapshot.Profile);
            if (_running()) throw new InvalidOperationException("请先关闭 CS2，再保存本地设置。");
            foreach (var name in replacements.Keys)
            {
                if (!FileNames.Contains(name) || !snapshot.Files.ContainsKey(name)) throw new InvalidDataException("恢复包含不支持的文件。");
                EnsureUnchanged(snapshot.Files[name]); _ = new ConfigText(replacements[name]);
                if ((System.IO.File.GetAttributes(snapshot.Files[name].Path) & FileAttributes.ReadOnly) != 0) throw new IOException("配置为只读，请先解除只读再保存。");
            }
            var folder = Path.Combine(_backups, snapshot.Profile.AccountId.ToString(), DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff") + "-" + Guid.NewGuid().ToString("N"));
            RejectLinks(folder); Directory.CreateDirectory(folder);
            var hashes = new Dictionary<string, string>();
            foreach (var name in replacements.Keys)
            {
                var bytes = snapshot.Files[name].Bytes;
                System.IO.File.WriteAllBytes(Path.Combine(folder, name), bytes); hashes[name] = Hash(bytes);
            }
            System.IO.File.WriteAllText(Path.Combine(folder, "manifest.json"), JsonSerializer.Serialize(new ConfigBackupManifest(snapshot.Profile.AccountId, hashes), ConfigBackupJsonContext.Default.ConfigBackupManifest));
            var committed = new List<string>();
            try
            {
                foreach (var (name, bytes) in replacements)
                {
                    if (_running()) throw new InvalidOperationException("检测到 CS2 已启动，取消保存。");
                    EnsureUnchanged(snapshot.Files[name]);
                    AtomicWrite(snapshot.Files[name].Path, bytes); committed.Add(name);
                }
            }
            catch (Exception ex)
            {
                bool restored = true;
                foreach (var name in committed.AsEnumerable().Reverse())
                    try
                    {
                        if (Hash(System.IO.File.ReadAllBytes(snapshot.Files[name].Path)) != Hash(replacements[name])) { restored = false; continue; }
                        AtomicWrite(snapshot.Files[name].Path, snapshot.Files[name].Bytes);
                    }
                    catch { restored = false; }
                throw new IOException((restored ? "保存失败，已恢复本次写入。" : "保存未全部完成，部分文件无法回滚。") + "备份：" + folder + "\n" + ex.Message, ex);
            }
            return folder;
        }
    }
    private static void AtomicWrite(string path, byte[] bytes)
    {
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { stream.Write(bytes); stream.Flush(true); }
            System.IO.File.Move(temp, path, true);
        }
        finally { if (System.IO.File.Exists(temp)) System.IO.File.Delete(temp); }
    }
    public string? LatestBackup(LocalProfile profile)
    {
        ValidateProfile(profile); var folder = Path.Combine(_backups, profile.AccountId.ToString()); RejectLinks(folder);
        return Directory.Exists(folder) ? Directory.EnumerateDirectories(folder).Where(d => System.IO.File.Exists(Path.Combine(d,"manifest.json"))).OrderDescending(StringComparer.Ordinal).FirstOrDefault() : null;
    }
    public string Restore(ConfigSnapshot snapshot, string directory)
    {
        var parent = Path.Combine(_backups, snapshot.Profile.AccountId.ToString());
        if (!string.Equals(Path.GetDirectoryName(Path.GetFullPath(directory)), parent, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("备份不属于此账号。");
        RejectLinks(directory);
        var manifestFile = Path.Combine(directory, "manifest.json"); RejectLinks(manifestFile);
        if (new FileInfo(manifestFile).Length > 65536) throw new InvalidDataException("备份清单过大。");
        var manifest = JsonSerializer.Deserialize(System.IO.File.ReadAllText(manifestFile), ConfigBackupJsonContext.Default.ConfigBackupManifest);
        if (manifest is null || manifest.AccountId != snapshot.Profile.AccountId || manifest.Sha256 is null || manifest.Sha256.Count is < 1 or > 4)
            throw new InvalidDataException("备份清单无效或账号不符。");
        var replacements = new Dictionary<string, byte[]>();
        foreach (var (name, hash) in manifest.Sha256)
        {
            if (!FileNames.Contains(name)) throw new InvalidDataException("备份文件不在允许列表中。");
            var file = ReadFile(Path.Combine(directory, name));
            if (Hash(file.Bytes) != hash) throw new InvalidDataException("备份校验失败。");
            replacements[name] = file.Bytes;
        }
        return Commit(snapshot, replacements);
    }
}

[JsonSerializable(typeof(ConfigBackupManifest))]
internal sealed partial class ConfigBackupJsonContext : JsonSerializerContext;
