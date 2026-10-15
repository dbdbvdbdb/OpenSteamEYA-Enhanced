using System.Net;
using SteamEyaWinUI.Services;

internal static class UpdateChecks
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        foreach (var (later, earlier) in new[] {
            ("v0.2.6-beta", "0.2.5-beta"), ("0.2.6", "0.2.6-beta"),
            ("0.2.6-beta.10", "0.2.6-beta.2"), ("0.2.6-beta.1", "0.2.6-beta"),
            ("0.2.6-rc.1", "0.2.6-beta.99"), ("0.10.0", "0.9.9"),
            ("0.2.6-beta.a", "0.2.6-beta.99999999999999999999") })
            check(ReleaseVersion.Parse(later).CompareTo(ReleaseVersion.Parse(earlier)) > 0 &&
                ReleaseVersion.Parse(earlier).CompareTo(ReleaseVersion.Parse(later)) < 0, $"update version precedence {later} > {earlier}");
        check(ReleaseVersion.Parse("v0.2.6-beta+abc").CompareTo(ReleaseVersion.Parse("0.2.6-beta+def")) == 0, "update ignores build metadata");
        foreach (var invalid in new[] { "latest", "0.2", "0.2.6.1", "01.2.6", "0.2.6-beta.01", "0.2.6-", "0.2.6/evil", "999999999999.2.6" })
            check(!ReleaseVersion.TryParse(invalid, out _), "invalid update version rejected: " + invalid);

        const string release = """
        {"tag_name":"v0.2.6-beta","published_at":"2026-10-16T00:00:00Z","draft":false,"prerelease":true,
        "html_url":"https://untrusted.example/ignored","body":"New beta\n\nDetails","assets":[
          {"name":"installer.exe","state":"uploaded","size":1,"browser_download_url":"https://untrusted.example/installer.exe"},
          {"name":"OpenSteamEYA-Enhanced-0.2.6-beta-win-x64-aot.zip","state":"uploaded","size":123,
           "browser_download_url":"https://github.com/dbdbvdbdb/OpenSteamEYA-Enhanced/releases/download/v0.2.6-beta/OpenSteamEYA-Enhanced-0.2.6-beta-win-x64-aot.zip",
           "digest":"sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}]}
        """;
        var handler = new StubHandler();
        using var client = new HttpClient(handler);
        var service = new GitHubUpdateService(client, "0.2.5-beta");
        handler.Json = "[" + release + "," + release.Replace("v0.2.6-beta", "v99.0.0").Replace("\"draft\":false", "\"draft\":true") + "," +
            release.Replace("v0.2.6-beta", "v0.2.4-beta") + "]";
        var update = await service.CheckLatestAsync();
        check(update.IsUpdateAvailable && update.LatestTag == "v0.2.6-beta", "official releases list includes beta and ignores drafts and sort order");
        check(update.ArtifactType == "zip-portable" && update.ArtifactSize == 123 && update.ArtifactSha256?.Length == 64, "portable ZIP and GitHub digest parsed with AOT JSON metadata");
        check(update.ReleaseUrl == GitHubUpdateService.ReleasesUrl + "/tag/v0.2.6-beta" && update.Changelog.Count == 2, "release link stays in enhanced repository and notes are parsed");
        check(handler.LastUrl == GitHubUpdateService.ReleasesApiUrl && !handler.HadAuthorization, "update request contains no account or authorization data");
        check(!(await new GitHubUpdateService(client, "0.2.6-beta").CheckLatestAsync()).IsUpdateAvailable, "same beta not offered again");
        check(!(await new GitHubUpdateService(client, "0.2.7-beta").CheckLatestAsync()).IsUpdateAvailable, "older release never offered as downgrade");
        handler.Json = "[" + release.Replace("0.2.6-beta", "0.2.6") + "]";
        check((await new GitHubUpdateService(client, "0.2.6-beta").CheckLatestAsync()).IsUpdateAvailable, "stable release upgrades same-number beta");
        handler.Json = "[" + release + "]";
        service.SetProxySite("gh-proxy.org");
        update = await service.CheckLatestAsync();
        check(handler.LastUrl == GitHubUpdateService.ReleasesApiUrl && update.ArtifactUrl!.StartsWith("https://gh-proxy.org/https://github.com/dbdbvdbdb/"), "proxy affects only download link, metadata stays official");
        handler.Json = "[" + release.Replace("https://github.com/dbdbvdbdb/OpenSteamEYA-Enhanced/releases/download/", "https://untrusted.example/") + "]";
        update = await service.CheckLatestAsync();
        check(update.ArtifactUrl is null && update.IsUpdateAvailable && update.ReleaseUrl.StartsWith(GitHubUpdateService.ReleasesUrl), "invalid asset URL falls back to official release page");
        handler.Json = "[" + release.Replace("\"state\":\"uploaded\"", "\"state\":\"new\"") + "]";
        check((await service.CheckLatestAsync()).ArtifactUrl is null, "incomplete upload not offered");

        async Task Reject(Func<Task> action, string name)
        { bool rejected = false; try { await action(); } catch { rejected = true; } check(rejected, name); }
        handler.Json = "[]";
        await Reject(() => service.CheckLatestAsync(), "empty release list is not reported as up to date");
        handler.Json = "{";
        await Reject(() => service.CheckLatestAsync(), "malformed API response fails cleanly");
        handler.Json = "[null]";
        await Reject(() => service.CheckLatestAsync(), "null API entry fails cleanly");
        handler.Json = "[" + release.Replace("\"published_at\":\"2026-10-16T00:00:00Z\"", "\"published_at\":null") + "]";
        await Reject(() => service.CheckLatestAsync(), "unpublished release ignored");
        foreach (var status in new[] { HttpStatusCode.Forbidden, HttpStatusCode.NotFound, HttpStatusCode.TooManyRequests, HttpStatusCode.InternalServerError })
        {
            handler.Status = status;
            await Reject(() => service.CheckLatestAsync(), "API failure is not success: " + status);
        }
        handler.Status = HttpStatusCode.OK;
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Reject(() => service.CheckLatestAsync(cts.Token), "cancelled update check exits without a result");
        handler.ThrowNetwork = true;
        await Reject(() => service.CheckLatestAsync(), "offline update check surfaces a recoverable failure");
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        internal string Json = "[]";
        internal string? LastUrl;
        internal bool HadAuthorization;
        internal bool ThrowNetwork;
        internal HttpStatusCode Status = HttpStatusCode.OK;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            LastUrl = request.RequestUri?.AbsoluteUri;
            HadAuthorization = request.Headers.Authorization is not null;
            if (ThrowNetwork) throw new HttpRequestException("Synthetic offline failure");
            return Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent(Json) });
        }
    }
}
