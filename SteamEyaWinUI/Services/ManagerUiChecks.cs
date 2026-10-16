#if MANAGER_UI_CHECKS
using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using OpenSteamEya.AccountManagement;
using SteamEyaWinUI.Pages;
using Windows.Graphics;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace SteamEyaWinUI.Services;

// Compiled only into an explicit QA build. Never reads credential stores, writes user data or logs passwords.
internal static partial class ManagerUiChecks
{
    public static bool Enabled { get; private set; }
    private static bool Cs2Only { get; set; }
    public static string OutputRoot { get; } = Path.Combine(AppContext.BaseDirectory, "ui-check-results", "run-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6]);
    public static string DataRoot => Path.Combine(OutputRoot, "synthetic-data");
    public static string AvatarPath => Path.Combine(OutputRoot, "fixture-avatar.png");
    private static readonly List<string> Results = [];
    private static AccountManagerPage? CheckPage;
    internal static readonly Dictionary<Guid, Models.SteamAccountHistoryItem> StatusFixtures = [];
    public static void Configure(string[] args)
    { Cs2Only=args.Contains("--cs2-settings-ui-check");Enabled=Cs2Only || args.Contains("--manager-ui-check"); }
    public static void Check(bool value, string name)
    { if (!value) throw new InvalidOperationException("UI check failed: " + name); Results.Add("PASS " + name); }
    public static void Info(string value) => Results.Add("INFO " + value);
    public static async Task RunAsync(MainWindow window, AccountManagerPage page)
    {
        CheckPage = page;
        try
        {
            Directory.CreateDirectory(OutputRoot);
            await File.WriteAllTextAsync(Path.Combine(AppContext.BaseDirectory, "ui-check-results", "latest-run.txt"), OutputRoot);
            var previousFailure = Path.Combine(OutputRoot, "failure.txt");
            if (File.Exists(previousFailure)) File.Delete(previousFailure);
            if(Cs2Only)
            {
                await CheckCs2SettingsAsync(window);
                await File.WriteAllLinesAsync(Path.Combine(OutputRoot,"results.txt"),Results);
                return;
            }
            CheckOriginalServicesOffline();
            await WriteFixtureAsync();
            var palette = await CardBackgroundService.GetAsync(new(), AvatarPath, null, "", new(), true);
            Check(palette.Primary != palette.Secondary, "avatar multicolor extraction");
            var cachedPalette = await CardBackgroundService.GetAsync(new(), AvatarPath, null, "", new(), true);
            Check(palette == cachedPalette, "avatar palette cache");
            var missingImage = await CardBackgroundService.GetAsync(new() { Background = new() { Mode = "local", ImageFile = "missing.png" } }, AvatarPath, null, "", new(), true);
            Check(missingImage.ImagePath is null && missingImage.Primary == palette.Primary, "missing custom image falls back to avatar palette");
            var missingAvatar = await CardBackgroundService.GetAsync(new(), Path.Combine(OutputRoot, "nonexistent-avatar.png"), null, "", new(), true);
            Check(missingAvatar.ImagePath is null, "missing avatar falls back to default gradient");
            Directory.CreateDirectory(ManagedAccounts.Store.ImageDirectory);
            File.Copy(AvatarPath, Path.Combine(ManagedAccounts.Store.ImageDirectory, "fixture-background.png"));
            var softImage = await CardBackgroundService.GetAsync(new() { Background = new() { Mode = "local", ImageFile = "fixture-background.png" } }, AvatarPath, null, "", new(), true);
            Check(softImage.ImagePath is not null && File.Exists(softImage.ImagePath), "local image asynchronous softening");
            foreach (int count in new[] { 10, 50, 100 })
            {
                await page.PrepareUiCheckAsync(count);
                window.ApplyTheme(ElementTheme.Dark);
                Resize(window, page, 1280, 860);
                await page.WaitForVisibleCardAsync(); await CaptureAsync(window, $"cards-{count}-dark");
                await page.VerifyUiCheckAsync("cards", false);
                await page.SwitchUiCheckAsync("list", false);
                await page.WaitForVisibleCardAsync(); await CaptureAsync(window, $"list-{count}-dark");
                await page.VerifyUiCheckAsync("list", false);
            }
            await page.SwitchUiCheckAsync("cards", true);
            await page.WaitForVisibleCardAsync(); await CaptureAsync(window, "privacy-100");
            await page.VerifyUiCheckAsync("cards", true);
            await page.SwitchUiCheckAsync("cards", false);
            window.ApplyTheme(ElementTheme.Light); Resize(window, page, 1180, 780);
            await page.WaitForVisibleCardAsync(); await CaptureAsync(window, "cards-100-light-minimum");
            await page.VerifyUiCheckAsync("cards", false);
            Resize(window, page, 1680, 980);
            await page.WaitForVisibleCardAsync(); await CaptureAsync(window, "cards-100-light-wide");
            await page.CheckFilterSelectionAsync();
            await CheckClipboardAsync(page);
            await page.CheckNewSelectionActionsAsync();
            await page.CheckMultipleGroupsAsync();
            await page.CheckNativeBatchAddAsync();
            await page.PrepareUiCheckAsync(4); window.ApplyTheme(ElementTheme.Dark);
            foreach (var info in FindElements((FrameworkElement)window.Content).OfType<Microsoft.UI.Xaml.Controls.InfoBar>()) info.IsOpen = false;
            Resize(window, page, 1440, 1080);
            await Task.Delay(1000);
            await page.CheckCardStatesAsync(name => CaptureAsync(window, name));
            await page.CheckCooldownStatusAsync(name => CaptureAsync(window, name));
            foreach (double scale in new[] { 1.0, 1.25, 1.5, 2.0 }) await CaptureAsync(window, "render-scale-" + (int)(scale * 100), scale);
            await CheckClassificationsAsync(window, page);
            await CheckUpdatesAsync(window);
            await CheckCs2SettingsAsync(window);
            Results.Add("INFO actual rasterization scale " + page.XamlRoot.RasterizationScale);
            await File.WriteAllLinesAsync(Path.Combine(OutputRoot, "results.txt"), Results);
        }
        catch (Exception ex) { await File.WriteAllLinesAsync(Path.Combine(OutputRoot, "partial-results.txt"), Results); await File.WriteAllTextAsync(Path.Combine(OutputRoot, "failure.txt"), ex.ToString()); }
        finally { window.Close(); }
    }
    private static IEnumerable<DependencyObject> FindElements(DependencyObject root)
    {
        yield return root;
        for (int i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in FindElements(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private static void Resize(MainWindow window, AccountManagerPage page, int width, int height)
    {
        double scale = page.XamlRoot.RasterizationScale;
        window.AppWindow.Resize(new SizeInt32((int)(width * scale), (int)(height * scale)));
    }
    private static void CheckOriginalServicesOffline()
    {
        // Synthetic inputs exercise original service code without logging in or touching credential files.
        var payload = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("{\"iss\":\"steam\",\"aud\":[\"client\"],\"sub\":\"76561198000000001\",\"exp\":4102444800}"))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var jwt = new JwtTokenService();
        Check(jwt.Validate("synthetic." + payload + ".synthetic").SteamId == "76561198000000001", "original JWT inspection parses synthetic fields in AOT");
        Check(!jwt.Inspect("invalid-synthetic-token").IsValid, "original JWT inspection rejects malformed input");
        var encrypted = new SteamCryptoService().EncryptToHex("synthetic-token-never-used-to-login", "qa-account");
        Check(encrypted.Length > 64 && encrypted.All(Uri.IsHexDigit), "original DPAPI encryptor works with synthetic input in AOT");
        var bytes = SteamProtoWriter.Build(writer =>
        {
            writer.WriteUInt32(1, 40); writer.WriteFixed32(2, 0xabcdef);
            writer.WriteFixed64(3, 76561198000000001); writer.WriteBool(4, true);
            writer.WriteString(5, "synthetic-CS2-中文"); writer.WriteBytes(6, [1, 2, 3]);
        });
        var reader = new SteamProtoReader(bytes);
        Check(reader.TryReadTag(out var field, out var wire) && field == 1 && reader.ReadVarint(wire) == 40, "Steam CS2 protobuf varint");
        Check(reader.TryReadTag(out field, out wire) && field == 2 && reader.ReadFixed32(wire) == 0xabcdef, "Steam CS2 protobuf fixed32");
        Check(reader.TryReadTag(out field, out wire) && field == 3 && reader.ReadFixed64(wire) == 76561198000000001, "Steam CS2 protobuf fixed64");
        Check(reader.TryReadTag(out field, out wire) && field == 4 && reader.ReadBool(wire), "Steam CS2 protobuf boolean");
        Check(reader.TryReadTag(out field, out wire) && field == 5 && reader.ReadString(wire) == "synthetic-CS2-中文", "Steam CS2 protobuf UTF8");
        Check(reader.TryReadTag(out field, out wire) && field == 6 && reader.ReadLengthDelimited(wire).SequenceEqual(new byte[] { 1, 2, 3 }) && !reader.TryReadTag(out _, out _), "Steam CS2 protobuf bytes and end");
        bool rejected = false;
        try { new SteamProtoReader([0x80]).TryReadTag(out _, out _); }
        catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "Steam CS2 protobuf rejects truncated payload");
    }
    private static async Task CaptureAsync(MainWindow window, string name, double? scale = null)
    {
        var root = (FrameworkElement)window.Content;
        // RenderTargetBitmap omits the OS Mica backdrop. Supply its neutral equivalent only in QA captures.
        if (root is Microsoft.UI.Xaml.Controls.Grid grid) grid.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
            root.ActualTheme == ElementTheme.Light ? Windows.UI.Color.FromArgb(255, 243, 243, 243) : Windows.UI.Color.FromArgb(255, 32, 32, 32));
        root.UpdateLayout();
        await Task.Delay(400); // let native layout/implicit opacity transitions finish before pixel capture
        var image = new RenderTargetBitmap();
        if (scale is { } requestedScale)
        {
            // RenderAsync's requested dimensions are DIPs and WinUI applies the monitor scale again.
            double monitorScale = root.XamlRoot.RasterizationScale;
            await image.RenderAsync(root, (int)(root.ActualWidth * requestedScale / monitorScale), (int)(root.ActualHeight * requestedScale / monitorScale));
            Check(Math.Abs(image.PixelWidth - root.ActualWidth * requestedScale) < 3 && Math.Abs(image.PixelHeight - root.ActualHeight * requestedScale) < 3, "export pixel dimensions " + name);
        }
        else await image.RenderAsync(root);
        var buffer = await image.GetPixelsAsync(); var bytes = new byte[buffer.Length];
        using (var reader = DataReader.FromBuffer(buffer)) reader.ReadBytes(bytes);
        if (!name.StartsWith("classification-") && !name.StartsWith("update-") && !name.StartsWith("cs2-settings-")) CheckPage!.VerifyCapturedCardPixels(root, image.PixelWidth, image.PixelHeight, bytes);
        Check(image.PixelWidth > 500 && image.PixelHeight > 500 && bytes.Any(x => x != 0), "render " + name);
        var path = Path.Combine(OutputRoot, name + ".png"); await File.WriteAllBytesAsync(path, []);
        var file = await StorageFile.GetFileFromPathAsync(path); using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)image.PixelWidth, (uint)image.PixelHeight, 96, 96, bytes);
        await encoder.FlushAsync();
    }
    private static async Task WriteFixtureAsync()
    {
        await File.WriteAllBytesAsync(AvatarPath, []);
        var file = await StorageFile.GetFileFromPathAsync(AvatarPath); using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        var pixels = new byte[64 * 64 * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        { pixels[i] = (byte)(i / 4 % 64 < 32 ? 200 : 70); pixels[i + 1] = 110; pixels[i + 2] = (byte)(i / 4 % 64 < 32 ? 45 : 180); pixels[i + 3] = 255; }
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, 64, 64, 96, 96, pixels); await encoder.FlushAsync();
    }
    private static async Task CheckClipboardAsync(AccountManagerPage page)
    {
        var old = Windows.ApplicationModel.DataTransfer.Clipboard.GetContent();
        var format = Windows.ApplicationModel.DataTransfer.StandardDataFormats.Text;
        var snapshot = new Windows.ApplicationModel.DataTransfer.DataPackage { RequestedOperation = old.RequestedOperation };
        try
        {
            foreach (var available in old.AvailableFormats)
                snapshot.SetData(available, await old.GetDataAsync(available));
        }
        catch { Results.Add("INFO clipboard checks skipped because original formats could not be preserved"); return; }
        try
        {
            ClipboardService.Copy("qa-copy-account");
            Check(await Windows.ApplicationModel.DataTransfer.Clipboard.GetContent().GetTextAsync() == "qa-copy-account", "copy login account");
            ClipboardService.Copy("synthetic-copy----\\\"中文");
            Check(await Windows.ApplicationModel.DataTransfer.Clipboard.GetContent().GetTextAsync() == "synthetic-copy----\\\"中文", "copy password special characters");
            await page.CheckCardClipboardAsync();
        }
        finally
        {
            if (old.AvailableFormats.Count > 0) Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(snapshot);
            else Windows.ApplicationModel.DataTransfer.Clipboard.Clear();
        }
    }
}
#endif
