using System.Collections.Concurrent;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using OpenSteamEya.AccountManagement;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.UI;

namespace SteamEyaWinUI.Services;

// Card artwork: the whole card is painted with a palette derived from the Steam avatar (monet style),
// or with a real image when the account asks for the Steam profile background / a local picture.
internal sealed record CardPalette(Color Primary, Color Secondary, Color Accent, string? ImagePath);

internal static class CardBackgroundService
{
    private static readonly SemaphoreSlim Slots = new(4);
    private static readonly SemaphoreSlim SoftGate = new(1);
    private static readonly ConcurrentDictionary<string, CardPalette> PaletteCache = new();
    private static readonly ConcurrentDictionary<string, (DateTimeOffset At, string? Path)> ProfileCache = new();
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };
    private static readonly CardPalette Fallback = new(Color.FromArgb(255, 40, 58, 82), Color.FromArgb(255, 65, 55, 90), Color.FromArgb(255, 37, 75, 76), null);

    /// <summary>Resolves the palette (avatar colors) and, when configured, the background image for one account.</summary>
    public static async Task<CardPalette> GetAsync(ManagedAccount account, string? avatarPath, string? avatarUrl,
        string steamId, ManagerPreferences prefs, bool dark, CancellationToken cancellationToken = default)
    {
        await Slots.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var mode = account.Background.Mode is "default" or "" ? prefs.BackgroundMode : account.Background.Mode;
            string? image = null;
            if (mode == "local" && account.Background.ImageFile is { } name)
            {
                var path = Path.Combine(ManagedAccounts.Store.ImageDirectory, name);
                if (File.Exists(path)) image = path;
            }
            if (mode == "steam" && steamId.Length == 17) image = await GetProfileBackgroundAsync(steamId, cancellationToken);
            if (string.IsNullOrEmpty(avatarPath) || !File.Exists(avatarPath)) avatarPath = await DownloadImageAsync(avatarUrl, cancellationToken);
            var key = (avatarPath ?? "none") + "|" + (avatarPath is not null && File.Exists(avatarPath) ? File.GetLastWriteTimeUtc(avatarPath).Ticks : 0) + "|" + prefs.MonetStrength + "|" + dark;
            if (!PaletteCache.TryGetValue(key, out var palette))
            {
                palette = avatarPath is null ? Fallback : await ExtractAsync(avatarPath, prefs.MonetStrength, dark);
                PaletteCache[key] = palette;
            }
            if (image is not null && prefs.Blur > 0) image = await SoftenAsync(image, prefs.Blur, cancellationToken);
            return palette with { ImagePath = image };
        }
        catch
        {
            return Fallback;
        }
        finally { Slots.Release(); }
    }

    private static async Task<CardPalette> ExtractAsync(string path, double strength, bool dark)
    {
        // 用普通文件流打开：StorageFile 在受限目录下会抛 UnauthorizedAccessException。
        using var fileStream = File.OpenRead(path);
        using var stream = fileStream.AsRandomAccessStream();
        var decoder = await BitmapDecoder.CreateAsync(stream);
        var pixels = await decoder.GetPixelDataAsync(BitmapPixelFormat.Rgba8, BitmapAlphaMode.Straight,
            new BitmapTransform { ScaledWidth = 32, ScaledHeight = 32 }, ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
        var bytes = pixels.DetachPixelData();
        return await Task.Run(() =>
        {
            var buckets = new Dictionary<int, (int R, int G, int B, int Count)>();
            for (int i = 0; i + 3 < bytes.Length; i += 4)
            {
                int r = bytes[i], g = bytes[i + 1], b = bytes[i + 2];
                if (bytes[i + 3] < 128 || Math.Max(r, Math.Max(g, b)) < 24 || Math.Min(r, Math.Min(g, b)) > 235) continue;
                int key = (r / 32 << 6) | (g / 32 << 3) | b / 32;
                var old = buckets.GetValueOrDefault(key); buckets[key] = (old.R + r, old.G + g, old.B + b, old.Count + 1);
            }
            var top = buckets.Values.OrderByDescending(x => x.Count).Take(3).ToList();
            if (top.Count == 0) return Fallback;
            Color Make(int index)
            {
                var x = top[index % top.Count]; double mix = 0.56 + strength * 0.34;
                double neutral = dark ? 45 : 185;
                return Color.FromArgb(255, (byte)(x.R / x.Count * mix + neutral * (1 - mix)),
                    (byte)(x.G / x.Count * mix + neutral * (1 - mix)), (byte)(x.B / x.Count * mix + neutral * (1 - mix)));
            }
            return new CardPalette(Make(0), Make(1), Make(2), null);
        });
    }

    private const int BackgroundCacheLimit = 24;
    private static readonly Dictionary<string, BitmapImage> BackgroundCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Queue<string> BackgroundOrder = new();

    /// <summary>
    /// Decodes a background at 720 logical pixels wide and shares the decoded bitmap between cards.
    /// The same file is never decoded twice, and stale entries are evicted oldest-first.
    /// </summary>
    public static Brush? ImageBrushFor(string? path, string stretch)
    {
        if (path is null) return null;
        if (!BackgroundCache.TryGetValue(path, out var image))
        {
            try
            {
                image = new BitmapImage
                {
                    UriSource = new Uri(path),
                    DecodePixelWidth = 720,
                    DecodePixelType = DecodePixelType.Logical,
                };
            }
            catch { return null; }
            BackgroundCache[path] = image;
            BackgroundOrder.Enqueue(path);
            while (BackgroundOrder.Count > BackgroundCacheLimit && BackgroundOrder.TryDequeue(out var oldest))
            {
                if (!string.Equals(oldest, path, StringComparison.OrdinalIgnoreCase)) BackgroundCache.Remove(oldest);
            }
        }
        return new ImageBrush
        {
            ImageSource = image,
            Stretch = stretch switch { "Uniform" => Stretch.Uniform, "Fill" => Stretch.Fill, _ => Stretch.UniformToFill },
        };
    }

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
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken); using var bytes = new MemoryStream();
            var buffer = new byte[8192]; int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
            { if (bytes.Length + read > 20 * 1024 * 1024) return null; bytes.Write(buffer, 0, read); }
            var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { await File.WriteAllBytesAsync(temp, bytes.ToArray(), cancellationToken); File.Move(temp, path, true); }
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
            using var sourceStream = File.OpenRead(path);
            using var input = sourceStream.AsRandomAccessStream();
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

    /// <summary>Gradient brush for the avatar palette; the card uses it as a translucent wash.</summary>
    public static Brush Brush(CardPalette palette)
    {
        if (palette.ImagePath is { } path && ImageBrushFor(path, "UniformToFill") is { } image) return image;
        return new LinearGradientBrush
        {
            StartPoint = new(0, 0), EndPoint = new(1, 1), GradientStops =
            {
                new() { Color = palette.Primary, Offset = 0 },
                new() { Color = palette.Secondary, Offset = 0.5 },
                new() { Color = palette.Accent, Offset = 1 },
            },
        };
    }
}
