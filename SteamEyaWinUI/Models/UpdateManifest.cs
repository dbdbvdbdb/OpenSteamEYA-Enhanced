using System.Text.Json;
using System.Text.Json.Serialization;

namespace SteamEyaWinUI.Models;

internal sealed class UpdateManifest
{
    public string Version { get; set; } = "";

    public string Tag { get; set; } = "";

    public string Notes { get; set; } = "";

    public string Zip { get; set; } = "";

    public long Size { get; set; }

    public string Sha256 { get; set; } = "";

    public string? SevenZip { get; set; }

    public string? SevenZipSha256 { get; set; }

    public string? PublishedAt { get; set; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(UpdateManifest))]
internal sealed partial class UpdateManifestJsonContext : JsonSerializerContext;
