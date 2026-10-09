using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenSteamEya.AccountManagement;

public sealed class ManagedAccount
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string LoginName { get; set; } = "";
    public string Password { get; set; } = "";
    public string SteamId { get; set; } = "";
    public string Nickname { get; set; } = "";
    public string Note { get; set; } = "";
    public Guid? GroupId { get; set; }
    // GroupId retains the first membership for older v1 readers; GroupIds holds all memberships.
    public List<Guid> GroupIds { get; set; } = [];
    public int Order { get; set; }
    public bool Favorite { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }
    public string? LinkedKey { get; set; }
    public BackgroundOptions Background { get; set; } = new();
}

public sealed class BackgroundOptions
{
    // default, monet, steam, local
    public string Mode { get; set; } = "default";
    public string? ImageFile { get; set; }
    public string Stretch { get; set; } = "UniformToFill";
}

public sealed class ManagedGroup
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public int Order { get; set; }
}

public sealed class ManagerPreferences
{
    public string View { get; set; } = "cards";
    public string Sort { get; set; } = "custom";
    public bool FavoritesFirst { get; set; }
    public string GroupDisplay { get; set; } = "filter";
    public string BackgroundMode { get; set; } = "monet";
    public double MonetStrength { get; set; } = 0.6;
    public double Brightness { get; set; } = 0.45;
    public double Blur { get; set; } = 12;
    public bool FullSteamId { get; set; } = true;
    public bool HidePasswords { get; set; } = true;
    public bool Privacy { get; set; }
}

public sealed class ManagerDocument
{
    public int Version { get; set; } = 1;
    public List<ManagedAccount> Accounts { get; set; } = [];
    public List<ManagedGroup> Groups { get; set; } = [];
    public ManagerPreferences Preferences { get; set; } = new();
}

// Contains no password or credential. The UI adapter projects existing records into this type.
public sealed record ExistingIdentity(string Key, string LoginName, string SteamId);
public sealed record MappingResult(string? Key, IReadOnlyList<ExistingIdentity> Candidates, bool Conflict);
public enum DuplicatePolicy { Skip, Overwrite, FillMissing }
public enum RestoreMode { Merge, Replace }
public sealed record ImportIssue(int Line, string Reason);
public sealed record ImportRow(int Line, ManagedAccount Account);
public sealed record ImportPreview(IReadOnlyList<ImportRow> Rows, IReadOnlyList<ImportIssue> Issues);
public sealed record ImportResult(int Added, int Updated, int Skipped, int Failed);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, WriteIndented = true)]
[JsonSerializable(typeof(ManagerDocument))]
[JsonSerializable(typeof(List<ManagedAccount>))]
[JsonSerializable(typeof(ManagedAccount))]
public sealed partial class ManagerJsonContext : JsonSerializerContext;
