using System.Text;
using System.Text.Json;

namespace SteamEyaWinUI.Cs2Settings;

internal sealed class TemplateSetting(SettingSpec spec, string key, string? original, bool enabled, string? issue)
{
    public SettingSpec Spec { get; } = spec;
    public string Key { get; } = key;
    public string? Original { get; } = original;
    public bool Enabled { get; } = enabled;
    public string? Issue { get; } = issue;
    public string Value { get; set; } = spec.Boolean ? (original ?? spec.DefaultValue) is "1" or "true" ? "true" : "false" : original ?? spec.DefaultValue ?? "";
    public bool Dirty { get; set; }
    public string? Error { get; set; }
    public string Validated() => Spec.Validate(Value, Original ?? Spec.DefaultValue ?? "");
}

/// <summary>Independent template storage. Immutable native-file generations are committed by one atomic manifest replacement.</summary>
internal sealed class UnifiedConfigStore(string root)
{
    public static string DefaultRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenSteamEYA-Enhanced", "cs2-config");
    private static readonly object Gate = new();
    public string Root { get; } = Path.GetFullPath(root);
    public string BackupRoot => Path.Combine(Root, "backups");
    private string ManifestPath => SafeConfigIO.Child(Root, "template.json");

    internal static byte[] EmptyFile(string name) => Encoding.UTF8.GetBytes(name == LocalConfigStore.VideoFile ?
        "\"video.cfg\"\n{\n\t\"Version\" \"16\"\n}\n" :
        name == LocalConfigStore.KeysFile ? "\"config\"\n{\n\t\"bindings\"\n\t{\n\t}\n}\n" :
        "\"config\"\n{\n\t\"convars\"\n\t{\n\t}\n}\n");

    public TemplateSnapshot LoadOrCreate()
    {
        lock (Gate)
        {
            using var lease = Lease();
            if (File.Exists(ManifestPath)) return Load();
            return SaveCore(DefaultTemplate(), null);
        }
    }
    private FileStream Lease()
    {
        SafeConfigIO.RejectLinks(Root); Directory.CreateDirectory(Root); SafeConfigIO.RejectLinks(Root);
        return new FileStream(SafeConfigIO.Child(Root, ".template.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }
    public TemplateSnapshot Load()
    {
        var manifestBytes = SafeConfigIO.Read(ManifestPath, 65536);
        var manifest = JsonSerializer.Deserialize(manifestBytes, TemplateJsonContext.Default.TemplateManifest)
            ?? throw new InvalidDataException("模板清单为空。");
        ValidateManifest(manifest);
        var folder = SafeConfigIO.Child(SafeConfigIO.Child(Root, "template-files"), manifest.Revision);
        var files = new Dictionary<string, byte[]>();
        foreach (var (name, hash) in manifest.Files)
        {
            var bytes = SafeConfigIO.Read(SafeConfigIO.Child(folder, name));
            if (SafeConfigIO.Hash(bytes) != hash) throw new InvalidDataException("模板文件被外部修改，校验不符：" + name);
            SafeConfigIO.ValidateFile(name, bytes); files.Add(name, bytes);
        }
        return new(manifest, files, SafeConfigIO.Hash(manifestBytes));
    }
    private static void ValidateManifest(TemplateManifest manifest)
    {
        if (manifest.Version != 1 || manifest.Revision.Length != 32 || !manifest.Revision.All(Uri.IsHexDigit) ||
            string.IsNullOrWhiteSpace(manifest.Name) || manifest.Name.Length > 100 || manifest.Files is null ||
            manifest.Files.Count is < 1 or > 4 || manifest.Files.Keys.Any(n => !LocalConfigStore.FileNames.Contains(n)))
            throw new InvalidDataException("模板版本、文件清单或名称无效。");
    }
    public static TemplateSnapshot DefaultTemplate()
    {
        var files = LocalConfigStore.FileNames.ToDictionary(n => n, EmptyFile);
        foreach (var spec in SettingCatalog.Editable)
        {
            if (spec.DefaultValue is null || !spec.AllowInsert) continue;
            var value = spec.Boolean ? spec.DefaultValue is "1" or "true" ? "true" : "false" : spec.DefaultValue;
            string validated = spec.Validate(value, spec.DefaultValue);
            files[spec.FileName] = new ConfigText(files[spec.FileName]).Patch(spec.Section, new Dictionary<string, string> { [spec.Key] = validated }, true);
        }
        return new(new(), files, null);
    }
    public TemplateSnapshot ReadFolder(string folder)
    {
        SafeConfigIO.RejectLinks(folder);
        // A flat export folder or native local/cfg folder is accepted. No recursive search or unknown-file import.
        var files = new Dictionary<string, byte[]>();
        foreach (string name in LocalConfigStore.FileNames)
        {
            string path = SafeConfigIO.Child(folder, name);
            if (!File.Exists(path)) continue;
            var bytes = SafeConfigIO.Read(path); SafeConfigIO.ValidateFile(name, bytes); files.Add(name, bytes);
        }
        if (files.Count == 0) throw new InvalidDataException("所选文件夹中没有受支持的原生 CS2 配置文件。");
        var manifestPath = SafeConfigIO.Child(folder, "template.json");
        if (File.Exists(manifestPath))
        {
            var manifest = JsonSerializer.Deserialize(SafeConfigIO.Read(manifestPath, 65536), TemplateJsonContext.Default.TemplateManifest)
                ?? throw new InvalidDataException("导出模板清单无效。");
            ValidateManifest(manifest);
            if (files.Count != manifest.Files.Count || files.Any(p => !manifest.Files.TryGetValue(p.Key, out var hash) || SafeConfigIO.Hash(p.Value) != hash))
                throw new InvalidDataException("导出模板文件不完整或校验不符。");
            return new(manifest, files, null);
        }
        return new(new(), files, null);
    }
    public TemplateSnapshot FromProfile(LocalConfigStore store, LocalProfile profile)
    {
        var snapshot = store.Load(profile);
        foreach (var file in snapshot.Files) SafeConfigIO.ValidateFile(file.Key, file.Value.Bytes);
        return new(new(), snapshot.Files.ToDictionary(p => p.Key, p => p.Value.Bytes.ToArray()), null);
    }
    public TemplateSnapshot Save(TemplateSnapshot basis, IEnumerable<TemplateSetting> rows, IReadOnlyDictionary<string, string> bindings, string name)
    {
        var files = basis.Files.ToDictionary(p => p.Key, p => p.Value.ToArray());
        foreach (var group in rows.Where(r => r.Dirty).GroupBy(r => r.Spec.FileName))
        {
            foreach (var row in group)
                if (!row.Enabled || !SettingCatalog.Editable.Any(s => s.Key == row.Spec.Key && s.FileName == row.Spec.FileName))
                    throw new InvalidDataException("参数未验证或不满足适用条件。");
            byte[] original = files.GetValueOrDefault(group.Key) ?? EmptyFile(group.Key);
            var document = new ConfigText(original);
            var changes = group.ToDictionary(r => r.Key, r => r.Validated());
            foreach (var row in group)
                if (document.Find(row.Spec.Section, row.Key)?.Value != row.Original || row.Original is null && !row.Spec.AllowInsert)
                    throw new InvalidDataException("模板设置快照已失效或不允许插入。");
            files[group.Key] = document.Patch(group.First().Spec.Section, changes, true);
        }
        if (bindings.Count > 0)
        {
            var doc = new ConfigText(files.GetValueOrDefault(LocalConfigStore.KeysFile) ?? EmptyFile(LocalConfigStore.KeysFile));
            var changes = new Dictionary<string, string>();
            foreach (var (key, value) in bindings)
            {
                if (!SettingCatalog.BindKeys.Contains(key, StringComparer.OrdinalIgnoreCase) || !SettingCatalog.VerifiedBindActions.Any(a => a.Value == value))
                    throw new InvalidDataException("键位或动作未验证。");
                var existing = doc.Entries.Where(e => e.Section == "config/bindings" && e.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (existing.Length > 1) throw new InvalidDataException("重复键位。");
                changes.Add(existing.SingleOrDefault()?.Key ?? key, value);
            }
            files[LocalConfigStore.KeysFile] = doc.Patch("config/bindings", changes, true);
        }
        return SaveSnapshot(basis with { Files = files }, name, basis.ManifestHash);
    }
    public TemplateSnapshot Replace(TemplateSnapshot current, TemplateSnapshot imported, string name) => SaveSnapshot(imported, name, current.ManifestHash);
    private TemplateSnapshot SaveSnapshot(TemplateSnapshot snapshot, string name, string? expected)
    {
        lock (Gate)
        {
            using var lease = Lease();
            return SaveCore(snapshot with { Manifest = snapshot.Manifest with { Name = name } }, expected);
        }
    }
    private TemplateSnapshot SaveCore(TemplateSnapshot snapshot, string? expected)
    {
        var current = SafeConfigIO.ReadOptional(ManifestPath);
        if ((current is null ? null : SafeConfigIO.Hash(current)) != expected)
            throw new IOException("模板已被另一个进程或外部编辑修改，请重新读取后重试。");
        if (current is not null) _ = Load(); // verify the current immutable generation before replacing it
        if (string.IsNullOrWhiteSpace(snapshot.Manifest.Name) || snapshot.Manifest.Name.Length > 100 || snapshot.Manifest.Name.Any(char.IsControl))
            throw new InvalidDataException("模板名称需要 1–100 个字符。");
        if (snapshot.Files.Count is < 1 or > 4) throw new InvalidDataException("模板文件数量无效。");
        foreach (var (name, bytes) in snapshot.Files) SafeConfigIO.ValidateFile(name, bytes);
        ValidateVideoCombination(snapshot.Files);
        string revision = Guid.NewGuid().ToString("N");
        string folder = SafeConfigIO.Child(SafeConfigIO.Child(Root, "template-files"), revision);
        Directory.CreateDirectory(folder);
        foreach (var (name, bytes) in snapshot.Files) SafeConfigIO.AtomicWrite(SafeConfigIO.Child(folder, name), bytes);
        var manifest = new TemplateManifest { Revision = revision, Name = snapshot.Manifest.Name, SavedUtc = DateTime.UtcNow,
            Display = snapshot.Manifest.Display, Files = snapshot.Files.ToDictionary(p => p.Key, p => SafeConfigIO.Hash(p.Value)) };
        if (current is not null)
        {
            string backup = SafeConfigIO.Child(SafeConfigIO.Child(BackupRoot, "templates"), Guid.NewGuid().ToString("N"));
            // The backed-up manifest references an immutable generation, which is retained.
            SafeConfigIO.AtomicWrite(SafeConfigIO.Child(backup, "template.json"), current);
        }
        // Last operation commits the entire generation; earlier failures leave the previous template usable.
        SafeConfigIO.AtomicWrite(ManifestPath, JsonSerializer.SerializeToUtf8Bytes(manifest, TemplateJsonContext.Default.TemplateManifest));
        return Load();
    }
    internal static void ValidateVideoCombination(IReadOnlyDictionary<string, byte[]> files)
    {
        if (!files.TryGetValue(LocalConfigStore.VideoFile, out var bytes)) return;
        var doc = new ConfigText(bytes);
        if (doc.Find("video.cfg", "Version")?.Value != SettingCatalog.VideoVersion) return;
        string? fsr = doc.Find("video.cfg", "setting.videocfg_fsr_detail")?.Value;
        if (fsr is not null && fsr != "0" && (doc.Find("video.cfg", "setting.msaa_samples")?.Value != "0" || doc.Find("video.cfg", "setting.r_csgo_cmaa_enable")?.Value != "0"))
            throw new InvalidDataException("保守兼容策略：启用 FSR 前请明确关闭 MSAA 和 CMAA；不推测游戏原生控件的联动结果。");
    }
    public static List<TemplateSetting> GetSettings(TemplateSnapshot snapshot)
    {
        var docs = snapshot.Files.ToDictionary(p => p.Key, p => new ConfigText(p.Value));
        var rows = new List<TemplateSetting>();
        foreach (var spec in SettingCatalog.Visible)
        {
            docs.TryGetValue(spec.FileName, out var doc);
            var matches = doc?.Entries.Where(e => e.Section == spec.Section && SettingCatalog.BaseKey(e.Key) == spec.Key).ToArray() ?? [];
            bool videoCompatible = spec.FileName != LocalConfigStore.VideoFile || doc is not null && doc.Find("video.cfg", "Version")?.Value == SettingCatalog.VideoVersion;
            string? issue = matches.Length > 1 ? "同一参数存在多个版本键，保留原值。" : !videoCompatible ? "视频格式不是已验证的版本 16，请先从当前游戏导入配置。" :
                spec.Availability != SettingAvailability.Verified ? spec.Description : null;
            if (doc is null && spec.FileName != LocalConfigStore.VideoFile) videoCompatible = true;
            var match = matches.FirstOrDefault();
            rows.Add(new(spec, match?.Key ?? spec.Key, match?.Value,
                issue is null && videoCompatible && (match is not null || spec.AllowInsert), issue));
        }
        return rows;
    }
    public void Export(TemplateSnapshot snapshot, string destination)
    {
        SafeConfigIO.RejectLinks(destination);
        if (string.Equals(Path.GetFullPath(destination), Root, StringComparison.OrdinalIgnoreCase) || Path.GetFullPath(destination).StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("导出目录不能位于模板存储内部。");
        Directory.CreateDirectory(destination);
        if (Directory.EnumerateFileSystemEntries(destination).Any()) throw new IOException("请选择一个空文件夹作为导出位置。");
        foreach (var (name, bytes) in snapshot.Files) { SafeConfigIO.ValidateFile(name, bytes); SafeConfigIO.AtomicWrite(SafeConfigIO.Child(destination, name), bytes); }
        SafeConfigIO.AtomicWrite(SafeConfigIO.Child(destination, "template.json"), JsonSerializer.SerializeToUtf8Bytes(snapshot.Manifest, TemplateJsonContext.Default.TemplateManifest));
    }
    public IReadOnlyList<TemplateBackupInfo> TemplateBackups()
    {
        string root = SafeConfigIO.Child(BackupRoot, "templates");
        if (!Directory.Exists(root)) return [];
        var backups = new List<TemplateBackupInfo>();
        foreach (var directory in Directory.GetDirectories(root).OrderByDescending(Directory.GetLastWriteTimeUtc))
        {
            SafeConfigIO.RejectLinks(directory);
            var manifest = JsonSerializer.Deserialize(SafeConfigIO.Read(SafeConfigIO.Child(directory, "template.json"), 65536), TemplateJsonContext.Default.TemplateManifest)
                ?? throw new InvalidDataException("模板备份清单无效。");
            backups.Add(new(directory, $"{manifest.SavedUtc.ToLocalTime():g} · {manifest.Name}"));
        }
        return backups;
    }
    public TemplateSnapshot RestoreTemplate(TemplateSnapshot current, string directory)
    {
        if (!string.Equals(Path.GetDirectoryName(Path.GetFullPath(directory)), Path.Combine(BackupRoot, "templates"), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("备份路径不属于模板备份目录。");
        var manifest = JsonSerializer.Deserialize(SafeConfigIO.Read(SafeConfigIO.Child(directory, "template.json"), 65536), TemplateJsonContext.Default.TemplateManifest)
            ?? throw new InvalidDataException("备份清单无效。");
        ValidateManifest(manifest);
        string generation = SafeConfigIO.Child(SafeConfigIO.Child(Root, "template-files"), manifest.Revision);
        var files = manifest.Files.ToDictionary(p => p.Key, p => SafeConfigIO.Read(SafeConfigIO.Child(generation, p.Key)));
        if (files.Any(p => SafeConfigIO.Hash(p.Value) != manifest.Files[p.Key])) throw new InvalidDataException("备份校验失败。");
        return Replace(current, new(manifest, files, null), manifest.Name);
    }
}
