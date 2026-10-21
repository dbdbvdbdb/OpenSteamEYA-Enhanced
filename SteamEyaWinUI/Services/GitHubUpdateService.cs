using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using SteamEyaWinUI.Models;

namespace SteamEyaWinUI.Services;

internal sealed class GitHubUpdateService
{
    public const string RepositoryUrl = "https://github.com/dbdbvdbdb/OpenSteamEYA-Enhanced";
    public const string UpstreamRepositoryUrl = "https://github.com/hvh-software/OpenSteamEYA";
    public const string ReleasesUrl = RepositoryUrl + "/releases";
    // /latest excludes prereleases. This project publishes beta versions, so read the release list.
    internal const string ReleasesApiUrl = "https://api.github.com/repos/dbdbvdbdb/OpenSteamEYA-Enhanced/releases?per_page=100";
    private static readonly HttpClient SharedClient = CreateHttpClient();
    private readonly HttpClient _client;
    private readonly string _currentVersion;
    public static string CurrentVersion { get; } = GetCurrentVersion();
    private string _selectedProxyCode = "direct";

    private static readonly IReadOnlyList<GitHubProxySite> ProxySites =
    [
        new("direct", "Direct", null),
        new("gh-proxy.org", "gh-proxy.org", "https://gh-proxy.org/"),
        new("v4.gh-proxy.org", "v4.gh-proxy.org", "https://v4.gh-proxy.org/"),
        new("v6.gh-proxy.org", "v6.gh-proxy.org", "https://v6.gh-proxy.org/"),
        new("cdn.gh-proxy.org", "cdn.gh-proxy.org", "https://cdn.gh-proxy.org/")
    ];

    public GitHubUpdateService() : this(SharedClient, CurrentVersion) { }
    internal GitHubUpdateService(HttpClient client, string currentVersion)
    { _client = client; _currentVersion = currentVersion; }
    public string SelectedProxyCode => _selectedProxyCode;
    public IReadOnlyList<GitHubProxySite> GetProxySites() => ProxySites;
    public void SetProxySite(string? proxyCode) => _selectedProxyCode = ResolveSite(proxyCode).Code;

    public async Task<GitHubUpdateInfo> CheckLatestAsync(CancellationToken cancellationToken = default)
    {
        // Always query the official public API, without account credentials or third-party proxies.
        using var response = await _client.GetAsync(ReleasesApiUrl, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var releases = await JsonSerializer.DeserializeAsync(stream,
            GitHubUpdateJsonContext.Default.GitHubReleaseDtoArray, cancellationToken)
            ?? throw new InvalidDataException("GitHub Release 响应为空，请稍后重试。");
        var latest = releases.Where(r => r is not null && !r.Draft && r.PublishedAt.HasValue && ReleaseVersion.TryParse(r.TagName, out _))
            .OrderByDescending(r => ReleaseVersion.Parse(r.TagName!)).FirstOrDefault()
            ?? throw new InvalidDataException("尚未找到可识别的已发布版本，请查看 GitHub 发布页。");
        var tag = latest.TagName!;
        var version = ReleaseVersion.Parse(tag);
        var current = ReleaseVersion.Parse(_currentVersion);
        var site = ResolveSite(_selectedProxyCode);
        // Only portable x64 AOT ZIP assets from this exact release may be offered for download.
        var assetName = $"OpenSteamEYA-Enhanced-{tag.TrimStart('v', 'V')}-win-x64-aot.zip";
        var expectedUrl = $"{ReleasesUrl}/download/{Uri.EscapeDataString(tag)}/{Uri.EscapeDataString(assetName)}";
        var asset = latest.Assets?.FirstOrDefault(a => a is not null && a.Name == assetName &&
            a.State == "uploaded" && a.Size > 0 && a.BrowserDownloadUrl == expectedUrl);
        var sha256 = asset?.Digest is { } digest && Regex.IsMatch(digest, "^sha256:[a-fA-F0-9]{64}$") ? digest[7..] : null;
        return new(_currentVersion, version.Text, tag, version.CompareTo(current) > 0,
            $"{ReleasesUrl}/tag/{Uri.EscapeDataString(tag)}", asset?.Name,
            asset is null ? null : BuildUrl(site, expectedUrl), asset?.Size,
            asset is null ? null : "zip-portable", sha256,
            (latest.Body ?? "").Split('\n').Select(s => s.Trim()).Where(s => s.Length > 0).ToArray(), DateTimeOffset.Now);
    }

    public async Task<TimeSpan> ProbeLatencyAsync(string? proxyCode = null, CancellationToken cancellationToken = default)
    {
        // This setting measures the download site; release metadata always uses the official API.
        var stopwatch = Stopwatch.StartNew();
        using var response = await _client.GetAsync(BuildUrl(ResolveSite(proxyCode ?? _selectedProxyCode), ReleasesUrl),
            HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        return stopwatch.Elapsed;
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20), MaxResponseContentBufferSize = 8 * 1024 * 1024 };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("OpenSteamEYA-Enhanced-Updater");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2026-03-10");
        return client;
    }
    private static GitHubProxySite ResolveSite(string? code) => ProxySites.FirstOrDefault(s =>
        string.Equals(s.Code, code, StringComparison.OrdinalIgnoreCase)) ?? ProxySites[0];
    private static string BuildUrl(GitHubProxySite site, string url) => site.UrlPrefix is null ? url : site.UrlPrefix + url;
    private static string GetCurrentVersion()
    {
        var assembly = typeof(GitHubUpdateService).Assembly;
        var value = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return ReleaseVersion.TryParse(value, out var version) ? version!.Text :
            assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    }

    internal sealed record GitHubProxySite(string Code, string DisplayName, string? UrlPrefix);
    internal sealed record GitHubReleaseDto(string? TagName, bool Draft, DateTimeOffset? PublishedAt,
        string? Body, GitHubAssetDto[]? Assets);
    internal sealed record GitHubAssetDto(string? Name, string? State, string? BrowserDownloadUrl, long Size, string? Digest);
}

// Preserve prerelease precedence: beta.10 > beta.2, stable > beta, build metadata ignored.
internal sealed record ReleaseVersion(Version Core, string[] Prerelease, string Text) : IComparable<ReleaseVersion>
{
    public static ReleaseVersion Parse(string value) => TryParse(value, out var parsed) ? parsed! : throw new InvalidDataException("无效的发布版本号。");
    public static bool TryParse(string? value, out ReleaseVersion? result)
    {
        result = null;
        if (value is null) return false;
        var match = Regex.Match(value.Trim(), @"^[vV]?(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$");
        if (!match.Success || !Version.TryParse($"{match.Groups[1]}.{match.Groups[2]}.{match.Groups[3]}", out var core)) return false;
        var prerelease = match.Groups[4].Success ? match.Groups[4].Value.Split('.') : [];
        if (prerelease.Any(s => s.Length > 1 && s[0] == '0' && IsNumeric(s))) return false;
        result = new(core, prerelease, core + (prerelease.Length == 0 ? "" : "-" + string.Join('.', prerelease)));
        return true;
    }
    private static bool IsNumeric(string value) => value.All(c => c is >= '0' and <= '9');
    public int CompareTo(ReleaseVersion? other)
    {
        if (other is null) return 1;
        var coreOrder = Core.CompareTo(other.Core);
        if (coreOrder != 0) return coreOrder;
        if (Prerelease.Length == 0 || other.Prerelease.Length == 0)
            return (Prerelease.Length == 0 ? 1 : 0).CompareTo(other.Prerelease.Length == 0 ? 1 : 0);
        for (int i = 0; i < Math.Min(Prerelease.Length, other.Prerelease.Length); i++)
        {
            var a = Prerelease[i]; var b = other.Prerelease[i];
            var an = IsNumeric(a); var bn = IsNumeric(b);
            int order = an && bn ? (a.Length != b.Length ? a.Length.CompareTo(b.Length) : string.CompareOrdinal(a, b))
                : an != bn ? (an ? -1 : 1) : string.CompareOrdinal(a, b);
            if (order != 0) return order;
        }
        return Prerelease.Length.CompareTo(other.Prerelease.Length);
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(GitHubUpdateService.GitHubReleaseDto[]))]
internal sealed partial class GitHubUpdateJsonContext : JsonSerializerContext;
