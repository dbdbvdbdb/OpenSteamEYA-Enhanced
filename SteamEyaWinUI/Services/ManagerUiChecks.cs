#if MANAGER_UI_CHECKS
using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
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
            Check(await CardBackgroundService.GetImageAsync(new(), "", new(), CancellationToken.None) is null, "default background uses the theme gradient");
            var missingImage = await CardBackgroundService.GetImageAsync(new() { Background = new() { Mode = "local", ImageFile = "missing.png" } }, "", new(), CancellationToken.None);
            Check(missingImage is null, "missing custom image falls back to the theme gradient");
            Directory.CreateDirectory(ManagedAccounts.Store.ImageDirectory);
            File.Copy(AvatarPath, Path.Combine(ManagedAccounts.Store.ImageDirectory, "fixture-background.png"), true);
            var localImage = await CardBackgroundService.GetImageAsync(new() { Background = new() { Mode = "local", ImageFile = "fixture-background.png" } }, "", new(), CancellationToken.None);
            Check(localImage is not null && File.Exists(localImage), "local image available for the card background");
            Check(CardBackgroundService.ImageBrushFor(localImage, "Uniform") is ImageBrush, "local image builds an image brush");
            Check(CardBackgroundService.ImageBrushFor(null, "Fill") is null, "without an image the theme gradient stays");
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
            await CheckThemeAsync(window);
            await CheckUpdaterAsync();
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
    private static async Task CheckThemeAsync(MainWindow window)
    {
        var before = ThemeService.Current;
        var previousPage = window.MainFrame.Content?.GetType();
        var spec = new ThemeSpec("#D6456B", "#E8A13A", ThemeTint.Warm);

        var first = ThemePaletteGenerator.Build(spec, false);
        var second = ThemePaletteGenerator.Build(spec, false);
        Check(first.Count >= 25, "theme generator emits the full token set");
        Check(first.All(pair => second[pair.Key] == pair.Value), "theme generator is deterministic");
        Check(first[ThemePaletteGenerator.Accent] != ThemePaletteGenerator.Build(ThemeSpec.Default, false)[ThemePaletteGenerator.Accent], "custom theme changes the accent token");

        foreach (bool dark in new[] { false, true })
        {
            var palette = ThemePaletteGenerator.Build(spec, dark);
            string label = dark ? "dark" : "light";
            Check(ThemePaletteGenerator.Contrast(palette[ThemePaletteGenerator.TextPrimary], palette[ThemePaletteGenerator.Surface]) >= 4.5, "primary text contrast on surfaces (" + label + ")");
            Check(ThemePaletteGenerator.Contrast(palette[ThemePaletteGenerator.TextPrimary], palette[ThemePaletteGenerator.PageBackground]) >= 4.5, "primary text contrast on page background (" + label + ")");
            Check(ThemePaletteGenerator.Contrast(palette[ThemePaletteGenerator.TextSecondary], palette[ThemePaletteGenerator.Surface]) >= 3.0, "secondary text contrast (" + label + ")");
            Check(ThemePaletteGenerator.Contrast(palette[ThemePaletteGenerator.OnAccent], palette[ThemePaletteGenerator.Accent]) >= 4.5, "text on accent contrast (" + label + ")");
        }

        window.ApplyTheme(ElementTheme.Dark);
        window.MainFrame.Navigate(typeof(ThemeEditorPage));
        await Task.Delay(900);
        var root = (FrameworkElement)window.Content;
        var buttons = FindElements(root).OfType<Microsoft.UI.Xaml.Controls.Button>().Select(x => x.Name).ToHashSet();
        Check(buttons.Contains("ApplyButton") && buttons.Contains("CancelButton") && buttons.Contains("ResetButton"), "theme editor exposes apply, cancel and reset");
        var previews = FindElements(root).OfType<Microsoft.UI.Xaml.Controls.StackPanel>().Where(x => x.Name is "LightPreviewContent" or "DarkPreviewContent").ToList();
        Check(previews.Count == 2 && previews.All(x => x.Children.Count >= 5), "theme editor previews both light and dark");
        await CaptureAsync(window, "theme-editor-dark");

        ThemeService.BeginPreview();
        ThemeService.SetSpec(spec);
        ThemeService.Repaint(refreshPages: false);
        await Task.Delay(400);
        var expected = ThemePaletteGenerator.Build(spec, true)[ThemePaletteGenerator.Accent];
        Check(((SolidColorBrush)Application.Current.Resources["PclAccentBrush"]).Color == expected, "live preview repaints the shared accent brush");
        await CaptureAsync(window, "theme-editor-preview");

        ThemeService.CancelPreview();
        await Task.Delay(300);
        Check(ThemeService.Current.PrimaryHex == before.PrimaryHex, "cancelling the editor restores the previous theme");

        ThemeService.Commit(spec);
        var saved = AppState.SettingsService.Load();
        Check(saved.ThemePrimaryHex == spec.PrimaryHex && saved.ThemeTint == "warm", "theme settings persist the custom palette");
        ThemeService.Commit(before);
        Check(AppState.SettingsService.Load().ThemePrimaryHex == before.PrimaryHex, "theme settings restore the previous palette");

        if (previousPage is not null) window.MainFrame.Navigate(previousPage);
        await Task.Delay(600);
    }
    private static async Task CheckUpdaterAsync()
    {
        Check(PortableUpdateService.IsSafeRelative("app/sub/file.txt"), "updater accepts safe relative path");
        Check(!PortableUpdateService.IsSafeRelative("../evil.txt"), "updater rejects parent traversal");
        Check(!PortableUpdateService.IsSafeRelative("C:\\evil.txt"), "updater rejects rooted path");
        Check(PortableUpdateService.SanitizeVersion("v0.4.5") == "0.4.5", "updater sanitizes version tag");

        var exe = Path.Combine(AppContext.BaseDirectory, PortableUpdateService.UpdaterFileName);
        Check(File.Exists(exe), "update helper is shipped with the app");
        if (!File.Exists(exe))
        {
            return;
        }

        var log = Path.Combine(OutputRoot, "updater-self-test.log");
        var startInfo = new System.Diagnostics.ProcessStartInfo(exe, "--self-test --log \"" + log + "\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = System.Diagnostics.Process.Start(startInfo)!;
        await process.WaitForExitAsync();
        var output = File.Exists(log) ? await File.ReadAllTextAsync(log) : "";
        var summary = output.Split('\n').FirstOrDefault(l => l.Contains("self-test summary"))?.Trim() ?? "<missing>";
        Info("updater " + summary);
        Check(process.ExitCode == 0, "update helper self-test exits cleanly");
        Check(output.Contains("PASS=") && output.Contains("FAIL=0"), "update helper self-test reports no failures");
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
        var activePage = FindElements(root).OfType<Microsoft.UI.Xaml.Controls.Frame>()
            .FirstOrDefault(frame => frame.Name == "ContentFrame")?.Content;
        // Cached pages still exist after navigation. Validate the page actually being
        // captured, rather than assuming every new screenshot name contains cards.
        if (activePage is AccountManagerPage manager && !name.StartsWith("classification-"))
            manager.VerifyCapturedCardPixels(root, image.PixelWidth, image.PixelHeight, bytes);
        Check(image.PixelWidth > 500 && image.PixelHeight > 500 && bytes.Any(x => x != 0), "render " + name);
        var path = Path.Combine(OutputRoot, name + ".png"); await File.WriteAllBytesAsync(path, []);
        var file = await StorageFile.GetFileFromPathAsync(path); using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)image.PixelWidth, (uint)image.PixelHeight, 96, 96, bytes);
        await encoder.FlushAsync();
    }
    /// <summary>Saves a rendered bitmap into the QA run folder so the result can be reviewed later.</summary>
    internal static async Task SaveBitmapAsync(RenderTargetBitmap image, string name)
    {
        var buffer = await image.GetPixelsAsync();
        var bytes = new byte[buffer.Length];
        using (var reader = DataReader.FromBuffer(buffer)) reader.ReadBytes(bytes);
        var path = Path.Combine(OutputRoot, name + ".png");
        await File.WriteAllBytesAsync(path, []);
        var file = await StorageFile.GetFileFromPathAsync(path);
        using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
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
            await CheckClipboardTextAsync(() => ClipboardService.Copy("qa-copy-account"), "qa-copy-account", "copy login account");
            await CheckClipboardTextAsync(() => ClipboardService.Copy("synthetic-copy----\\\"中文"), "synthetic-copy----\\\"中文", "copy password special characters");
            await page.CheckCardClipboardAsync();
        }
        finally
        {
            for (int attempt=0; ; attempt++)
                try
                {
                    if (old.AvailableFormats.Count > 0) Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(snapshot);
                    else Windows.ApplicationModel.DataTransfer.Clipboard.Clear();
                    break;
                }
                catch (System.Runtime.InteropServices.COMException) when (attempt<9) { await Task.Delay(100); }
        }
    }
    internal static async Task CheckClipboardTextAsync(Action copy, string expected, string name)
    {
        for(int attempt=0;attempt<10;attempt++)
        {
            try
            {
                copy(); await Task.Delay(100);
                var view=Windows.ApplicationModel.DataTransfer.Clipboard.GetContent();
                if(view.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Text) && await view.GetTextAsync()==expected)
                { Check(true,name);return; }
            }
            catch(System.Runtime.InteropServices.COMException) when(attempt<9) { }
            await Task.Delay(100);
        }
        Check(false,name+" (clipboard unavailable or unexpected content after bounded retry)");
    }
}
#endif
