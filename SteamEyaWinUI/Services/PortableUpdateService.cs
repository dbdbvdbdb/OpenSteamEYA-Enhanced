using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using SteamEyaWinUI.Models;

namespace SteamEyaWinUI.Services;

internal sealed record PortableUpdate(string Version, string StagingDirectory, string ZipPath, string ManifestSha256);

internal sealed class PortableUpdateService
{
    public const string UpdaterFileName = "OSEUpdater.exe";

    private const int DownloadBufferSize = 80 * 1024;
    private const long MaxExtractedBytes = 1024L * 1024 * 1024;
    private const int MaxEntries = 8000;

    private static readonly TimeSpan DownloadIdleTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan ManifestTimeout = TimeSpan.FromSeconds(15);
    private static readonly HttpClient HttpClient = CreateHttpClient();

    public string RootDirectory { get; }

    public string UpdatesDirectory => Path.Combine(RootDirectory, "updates");

    public string BackupDirectory => Path.Combine(RootDirectory, "backup");

    public string LogDirectory => Path.Combine(RootDirectory, "logs");

    public PortableUpdateService()
    {
        RootDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SteamEYA");
    }

    public static string AppDirectory => AppContext.BaseDirectory;

    public static bool SelfUpdateSupported => File.Exists(Path.Combine(AppDirectory, UpdaterFileName));

    public static string ManifestUrl(GitHubUpdateInfo update) =>
        $"https://github.com/{GitHubUpdateService.RepositoryOwner}/{GitHubUpdateService.RepositoryName}/releases/download/{update.LatestTag}/update.json";

    public async Task<UpdateManifest> ResolveManifestAsync(
        GitHubUpdateInfo update,
        string proxySite,
        CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ManifestTimeout);
            var url = GitHubUpdateService.ApplyProxy(proxySite, ManifestUrl(update));
            using var response = await HttpClient.GetAsync(url, HttpCompletionOption.ResponseContentRead, timeout.Token);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(timeout.Token);
                var manifest = JsonSerializer.Deserialize(json, UpdateManifestJsonContext.Default.UpdateManifest);
                if (manifest is not null && !string.IsNullOrWhiteSpace(manifest.Zip) && manifest.Size > 0 &&
                    !string.IsNullOrWhiteSpace(manifest.Sha256))
                {
                    return manifest;
                }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or OperationCanceledException)
        {
            AppLog.Error("读取更新清单失败，改用发布信息回退。", ex);
        }

        return new UpdateManifest
        {
            Version = update.LatestVersion,
            Tag = update.LatestTag,
            Zip = update.ArtifactName ?? "",
            Size = update.ArtifactSize ?? 0,
            Sha256 = update.ArtifactSha256 ?? "",
            Notes = string.Join("\n", update.Changelog),
        };
    }

    public async Task<PortableUpdate> PrepareAsync(
        GitHubUpdateInfo update,
        string proxySite,
        IProgress<UpdateDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(update.ArtifactUrl))
        {
            throw new InvalidOperationException("更新下载地址为空。");
        }

        var manifest = await ResolveManifestAsync(update, proxySite, cancellationToken);
        var version = SanitizeVersion(manifest.Version.Length > 0 ? manifest.Version : update.LatestVersion);
        var zipName = SanitizeFileName(string.IsNullOrWhiteSpace(manifest.Zip) ? update.ArtifactName ?? "update.zip" : manifest.Zip);
        var directory = Path.Combine(UpdatesDirectory, version);
        Directory.CreateDirectory(directory);

        var zipPath = Path.Combine(directory, zipName);
        var expectedSha = string.IsNullOrWhiteSpace(manifest.Sha256) ? update.ArtifactSha256 : manifest.Sha256;

        if (!File.Exists(zipPath) || new FileInfo(zipPath).Length != manifest.Size ||
            (!string.IsNullOrWhiteSpace(expectedSha) &&
             !string.Equals(ComputeSha256(zipPath), expectedSha, StringComparison.OrdinalIgnoreCase)))
        {
            await DownloadAsync(update.ArtifactUrl, zipPath, manifest.Size, progress, cancellationToken);
        }

        if (manifest.Size > 0 && new FileInfo(zipPath).Length != manifest.Size)
        {
            throw new InvalidOperationException("更新包大小与清单不一致，请稍后重试。");
        }

        if (!string.IsNullOrWhiteSpace(expectedSha))
        {
            var actual = ComputeSha256(zipPath);
            if (!string.Equals(actual, expectedSha, StringComparison.OrdinalIgnoreCase))
            {
                TryDelete(zipPath);
                throw new InvalidOperationException("更新包校验失败，请稍后重试。");
            }
        }

        var staging = Path.Combine(directory, "staging");
        if (Directory.Exists(staging))
        {
            Directory.Delete(staging, recursive: true);
        }

        Directory.CreateDirectory(staging);
        ExtractSafe(zipPath, staging);
        return new PortableUpdate(version, staging, zipPath, expectedSha ?? "");
    }

    public bool TryLaunchUpdater(PortableUpdate pending, string restartFileName, out string error)
    {
        error = "";
        var updater = Path.Combine(AppDirectory, UpdaterFileName);
        if (!File.Exists(updater))
        {
            error = "缺少更新程序 OSEUpdater.exe，请手动下载完整包。";
            return false;
        }

        // 从安装目录以外启动更新器：更新器自己也在待替换的文件列表里，
        // 正在运行的 exe 无法被覆盖，会以“拒绝访问”失败。把它（以及
        // 它的同目录依赖）复制到 updates\runner 下再启动。
        string runner;
        try
        {
            var runnerDirectory = Path.Combine(UpdatesDirectory, "runner");
            Directory.CreateDirectory(runnerDirectory);
            foreach (var file in Directory.EnumerateFiles(AppDirectory, "OSEUpdater*"))
            {
                File.Copy(file, Path.Combine(runnerDirectory, Path.GetFileName(file)), overwrite: true);
            }

            runner = Path.Combine(runnerDirectory, UpdaterFileName);
        }
        catch (Exception ex)
        {
            error = "准备更新程序失败：" + ex.Message;
            AppLog.Error("准备更新程序失败。", ex);
            return false;
        }

        var backup = Path.Combine(BackupDirectory, pending.Version);
        var log = Path.Combine(LogDirectory, "update-" + pending.Version + ".log");
        var current = Process.GetCurrentProcess();
        var arguments = string.Join(' ', new[]
        {
            "--pid", current.Id.ToString(),
            "--source", Quote(pending.StagingDirectory),
            "--target", Quote(AppDirectory.TrimEnd(Path.DirectorySeparatorChar)),
            "--restart", Quote(restartFileName),
            "--backup", Quote(backup),
            "--log", Quote(log),
            "--version", Quote(pending.Version),
        });

        try
        {
            Directory.CreateDirectory(LogDirectory);
            Process.Start(new ProcessStartInfo
            {
                FileName = runner,
                Arguments = arguments,
                WorkingDirectory = AppDirectory,
                UseShellExecute = false,
            });
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            AppLog.Error("启动更新程序失败。", ex);
            return false;
        }
    }

    private static async Task DownloadAsync(
        string url,
        string zipPath,
        long expectedSize,
        IProgress<UpdateDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var partPath = zipPath + ".part";
        long offset = File.Exists(partPath) ? new FileInfo(partPath).Length : 0;
        if (expectedSize > 0 && offset > expectedSize)
        {
            TryDelete(partPath);
            offset = 0;
        }

        using var idle = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        idle.CancelAfter(DownloadIdleTimeout);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (offset > 0)
            {
                request.Headers.Range = new RangeHeaderValue(offset, null);
            }

            using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, idle.Token);
            if (offset > 0 && response.StatusCode != HttpStatusCode.PartialContent)
            {
                offset = 0;
            }

            response.EnsureSuccessStatusCode();
            var total = expectedSize > 0 ? expectedSize : (offset + response.Content.Headers.ContentLength);

            await using (var source = await response.Content.ReadAsStreamAsync(idle.Token))
            await using (var target = new FileStream(partPath, offset > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[DownloadBufferSize];
                long received = offset;
                while (true)
                {
                    var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), idle.Token);
                    if (read == 0)
                    {
                        break;
                    }

                    idle.CancelAfter(DownloadIdleTimeout);
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    received += read;
                    progress?.Report(new UpdateDownloadProgress(received, total));
                }
            }

            if (File.Exists(zipPath))
            {
                File.Delete(zipPath);
            }

            File.Move(partPath, zipPath);
        }
        catch (OperationCanceledException) when (idle.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("下载超时：长时间无数据，请检查网络或更换更新站点后重试。");
        }
    }

    private static void ExtractSafe(string zipPath, string staging)
    {
        var normalized = Path.GetFullPath(staging).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        long extracted = 0;
        var count = 0;

        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            if (++count > MaxEntries)
            {
                throw new InvalidOperationException("更新包条目过多，已停止解压。");
            }

            var name = entry.FullName.Replace('\\', '/');
            if (name.EndsWith('/'))
            {
                continue;
            }

            if (!IsSafeRelative(name))
            {
                throw new InvalidOperationException("更新包包含非法路径：" + name);
            }

            var destination = Path.GetFullPath(Path.Combine(staging, name));
            if (!destination.StartsWith(normalized, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("更新包路径越界：" + name);
            }

            extracted += entry.Length;
            if (extracted > MaxExtractedBytes)
            {
                throw new InvalidOperationException("更新包解压后体积过大，已停止解压。");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination, overwrite: true);
        }

        if (count == 0)
        {
            throw new InvalidOperationException("更新包为空。");
        }
    }

    internal static bool IsSafeRelative(string relative) =>
        !string.IsNullOrWhiteSpace(relative) &&
        !Path.IsPathRooted(relative) &&
        !relative.Split('/', '\\').Contains("..") &&
        !relative.Contains(':');

    internal static string SanitizeVersion(string version)
    {
        var value = version.Trim().TrimStart('v', 'V');
        var chars = value.Where(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_').ToArray();
        return chars.Length == 0 ? "latest" : new string(chars);
    }

    private static string SanitizeFileName(string name)
    {
        var file = Path.GetFileName(name.Replace('\\', '/'));
        if (string.IsNullOrWhiteSpace(file) || !file.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            return "update.zip";
        }

        return file;
    }

    internal static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("清理更新临时文件失败。", ex);
        }
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SteamEYA-Updater");
        return client;
    }
}
