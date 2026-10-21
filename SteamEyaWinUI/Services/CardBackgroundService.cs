using System.Collections.Concurrent;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using OpenSteamEya.AccountManagement;
using Windows.Graphics.Imaging;
using Windows.Storage;

namespace SteamEyaWinUI.Services;

// Card artwork no longer derives colors from avatars: the strip uses the theme gradient and is only
// replaced by a real local image or a Steam profile background.
internal static class CardBackgroundService
{
    private static readonly SemaphoreSlim Slots = new(4);
    private static readonly SemaphoreSlim SoftGate = new(1);
    private static readonly ConcurrentDictionary<string, (DateTimeOffset At, string? Path)> ProfileCache = new();
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    /// <summary>Returns the image that should replace the theme gradient, or null to keep the gradient.</summary>
    public static async Task<string?> GetImageAsync(ManagedAccount account, string steamId, ManagerPreferences prefs,
        CancellationToken cancellationToken = default)
    {
        await Slots.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var mode = account.Background.Mode is "default" or "monet" ? prefs.BackgroundMode : account.Background.Mode;
            if (mode == "monet") mode = "default";
            string? image = null;
            if (mode == "local" && account.Background.ImageFile is { } name)
            {
                var path = Path.Combine(ManagedAccounts.Store.ImageDirectory, name);
                if (File.Exists(path)) image = path;
            }
            if (mode == "steam" && steamId.Length == 17) image = await GetProfileBackgroundAsync(steamId, cancellationToken);
            if (image is not null && prefs.Blur > 0) image = await SoftenAsync(image, prefs.Blur, cancellationToken);
            return image;
        }
        catch { return null; } // no credential or parser exception logging in production
        finally { Slots.Release(); }
    }

    public static Brush? ImageBrushFor(string? path, string stretch) =>
        path is null ? null : new ImageBrush
        {
            ImageSource = new BitmapImage(new Uri(path)),
            Stretch = stretch switch { "Uniform" => Stretch.Uniform, "Fill" => Stretch.Fill, _ => Stretch.UniformToFill },
        };

    private static async Task<string?> GetProfileBackgroundAsync(string steamId, CancellationToken cancellationToken)
    {
        if (ProfileCache.TryGetValue(steamId, out var cached) && DateTimeOffset.UtcNow - cached.At < TimeSpan.FromHours(6)) return cached.Path;
        string? result = null;
        try
        {
            var html = await Http.GetStringAsync("https://steamcommunity.com/profiles/" + steamId, cancellationToken);
            var match = await Task.Run(() => Regex.Match(html, @"profile_background[^>]*?url\(\s*['""']?(https://[^\s'""<>\)]+)", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)), cancellationToken);
            if (match.Success) result = await DownloadImageAsync(System.Net.WebUtility.HtmlDecode(match.Groups[1].Value), cancellationToken);
        }
        catch { }
        if (!cancellationToken.IsCancellationRequested) ProfileCache[steamId] = (DateTimeOffset.UtcNow, result);
        return result;
    }

    private static async Task<string?> DownloadImageAsync(string? url, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            !new[] { "steamstatic.com", "steamcommunity.com", "akamaihd.net" }.Any(x => uri.Host == x || uri.Host.EndsWith("." + x, StringComparison.OrdinalIgnoreCase))) return null;
        var directory = Path.Combine(ManagedAccounts.Store.Root, "cache"); Directory.CreateDirectory(directory);
        var name = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(url!)));
        var path = Path.Combine(directory, name + ".img");
        if (File.Exists(path) && DateTime.UtcNow - File.GetLastWriteTimeUtc(path) < TimeSpan.FromHours(6)) return path;
        try
        {
            using var response = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > 20 * 1024 * 1024) return null;
            await using var source = await response.Content.ReadAsStreamAsync(); using var bytes = new MemoryStream();
            var buffer = new byte[8192]; int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
            { if (bytes.Length + read > 20 * 1024 * 1024) return null; bytes.Write(buffer, 0, read); }
            var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { await File.WriteAllBytesAsync(temp, bytes.ToArray()); File.Move(temp, path, true); }
            finally { if (File.Exists(temp)) File.Delete(temp); }
            return path;
        }
        catch { return null; }
    }

    // Downsample before re-encoding: a bounded, asynchronous soft-focus background without Win2D dependencies.
    private static async Task<string> SoftenAsync(string path, double blur, CancellationToken cancellationToken)
    {
        await SoftGate.WaitAsync(cancellationToken);
        try
        {
        var directory = Path.Combine(ManagedAccounts.Store.Root, "cache"); Directory.CreateDirectory(directory);
        var key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(path + File.GetLastWriteTimeUtc(path).Ticks + blur)));
        var output = Path.Combine(directory, key + ".png"); if (File.Exists(output)) return output;
        var file = await StorageFile.GetFileFromPathAsync(path); using var input = await file.OpenReadAsync();
        var decoder = await BitmapDecoder.CreateAsync(input);
        uint width = Math.Min(decoder.PixelWidth, (uint)Math.Clamp(128 - blur * 3.6, 12, 128));
        uint height = Math.Min(decoder.PixelHeight, Math.Max(1, (uint)(width * (double)decoder.PixelHeight / decoder.PixelWidth)));
        height = Math.Min(height, 256);
        var pixels = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
            new BitmapTransform { ScaledWidth = width, ScaledHeight = height, InterpolationMode = BitmapInterpolationMode.Linear },
            ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
        var temp = output + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            // Use the .NET stream adapter: WinRT StorageFile rejects long temporary paths.
            using (var fileStream = new FileStream(temp, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            using (var stream = fileStream.AsRandomAccessStream())
            {
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
                encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, width, height, 96, 96, pixels.DetachPixelData());
                await encoder.FlushAsync();
            }
            File.Move(temp, output, true); return output;
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        finally { SoftGate.Release(); }
    }
}
