using System.Text.Json.Serialization;

namespace SteamEyaWinUI.Cs2Settings;

internal sealed record DisplaySelection(string Device, int Width, int Height);

internal sealed record TemplateManifest
{
    public int Version { get; set; } = 1;
    public string Name { get; set; } = "我的统一配置";
    public string Revision { get; set; } = "";
    public string CatalogRevision { get; set; } = SettingCatalog.SourceRevision;
    public DateTime SavedUtc { get; set; }
    public DisplaySelection? Display { get; set; }
    public string Scope { get; set; } = "CS2 local slot0; no Steam Cloud";
    public Dictionary<string, string> Files { get; set; } = [];
}
internal sealed record TemplateSnapshot(TemplateManifest Manifest, Dictionary<string, byte[]> Files, string? ManifestHash);
internal sealed class AccountBackup
{
    public int Version { get; set; } = 1;
    public uint AccountId { get; set; }
    public Dictionary<string, string?> Before { get; set; } = [];
    public Dictionary<string, string?> After { get; set; } = [];
}
internal sealed record TemplateBackupInfo(string Directory, string Name);
internal sealed record AccountApplyPlan(uint AccountId, string Directory, Dictionary<string, byte[]?> Before,
    Dictionary<string, byte[]?> After, List<string> Notes, string? Error);
internal sealed record ApplyPreview(string SteamRoot, string TemplateRevision, IReadOnlyList<AccountApplyPlan> Accounts);
internal sealed record AccountApplyResult(uint AccountId, string Status, int Files, string? Backup, string? Error, IReadOnlyList<string> Notes);
internal sealed record BatchApplyResult(IReadOnlyList<AccountApplyResult> Accounts, string? LogError = null)
{
    public int Succeeded => Accounts.Count(a => a.Status == "成功");
    public int Unchanged => Accounts.Count(a => a.Status == "无需更改");
    public int Failed => Accounts.Count(a => a.Status == "失败");
    public int Skipped => Accounts.Count(a => a.Status == "跳过");
}

[JsonSerializable(typeof(TemplateManifest))]
[JsonSerializable(typeof(AccountBackup))]
[JsonSerializable(typeof(BatchApplyResult))]
internal sealed partial class TemplateJsonContext : JsonSerializerContext;
