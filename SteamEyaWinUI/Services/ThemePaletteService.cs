using System.Globalization;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace SteamEyaWinUI.Services;

/// <summary>
/// Builds the launcher palette from one or more user-picked colors and writes it into the merged
/// theme dictionary. Derived surfaces stay close to the built-in neutrals so contrast never breaks;
/// only accents, tints and borders follow the picked colors.
/// </summary>
internal static class ThemePaletteService
{
    public const int MaxColors = 5;
    private static readonly Color DefaultAccent = Color.FromArgb(255, 0x13, 0x70, 0xF3);

    public static IReadOnlyList<Color> Current { get; private set; } = [DefaultAccent];

    public static IReadOnlyList<Color> Parse(IEnumerable<string>? hexColors)
    {
        if (hexColors is null) return [DefaultAccent];
        var list = new List<Color>();
        foreach (var text in hexColors)
        {
            if (TryParseHex(text, out var color) && !list.Contains(color)) list.Add(color);
            if (list.Count == MaxColors) break;
        }
        return list.Count == 0 ? [DefaultAccent] : list;
    }

    public static bool TryParseHex(string? text, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var value = text.Trim();
        if (value.StartsWith('#')) value = value[1..];
        if (value.Length == 8) value = value[2..];
        if (value.Length != 6 || !uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb)) return false;
        color = Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        return true;
    }

    public static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    /// <summary>Sets the active colors; call <see cref="Apply"/> afterwards to push them into the theme.</summary>
    public static void SetColors(IEnumerable<string>? hexColors) => Current = Parse(hexColors);

    public static IReadOnlyDictionary<string, Color> Build(IReadOnlyList<Color> colors, bool dark)
    {
        var accent = colors.Count > 0 ? colors[0] : DefaultAccent;
        if (dark) accent = Blend(accent, Colors.White, 0.18);
        var secondary = colors.Count > 1 ? colors[1] : accent;
        var tertiary = colors.Count > 2 ? colors[2] : secondary;

        var surface = dark ? Blend(Rgb(0x25, 0x26, 0x29), accent, 0.035) : Blend(Rgb(0xFF, 0xFF, 0xFF), accent, 0.015);
        var surfaceAlt = dark ? Blend(Rgb(0x2B, 0x2D, 0x31), accent, 0.05) : Blend(Rgb(0xFA, 0xFB, 0xFC), accent, 0.02);

        return new Dictionary<string, Color>
        {
            ["PclAccentColor"] = accent,
            ["PclAccentHoverColor"] = dark ? Blend(accent, Colors.White, 0.12) : Blend(accent, Colors.White, 0.10),
            ["PclAccentPressedColor"] = dark ? Blend(accent, Colors.Black, 0.10) : Blend(accent, Colors.Black, 0.16),
            ["PclAccentSoftColor"] = dark ? Blend(surface, accent, 0.28) : Blend(accent, Colors.White, 0.88),
            ["PclPageBackgroundColor"] = dark ? Blend(Rgb(0x1B, 0x1C, 0x1F), accent, 0.045) : Blend(Rgb(0xF3, 0xF4, 0xF6), accent, 0.03),
            ["PclSurfaceColor"] = surface,
            ["PclSurfaceAltColor"] = surfaceAlt,
            ["PclSurfaceHoverColor"] = dark ? Blend(Rgb(0x32, 0x34, 0x3A), accent, 0.06) : Blend(Rgb(0xF1, 0xF3, 0xF7), accent, 0.035),
            ["PclBorderColor"] = dark ? Blend(Rgb(0x3A, 0x3D, 0x44), accent, 0.12) : Blend(Rgb(0xE3, 0xE6, 0xEB), accent, 0.08),
            ["PclBorderStrongColor"] = dark ? Blend(Rgb(0x4A, 0x4E, 0x57), accent, 0.14) : Blend(Rgb(0xD3, 0xD8, 0xE0), accent, 0.10),
            ["PclTextPrimaryColor"] = dark ? Rgb(0xF2, 0xF3, 0xF5) : Rgb(0x1F, 0x23, 0x29),
            ["PclTextSecondaryColor"] = dark ? Rgb(0xA8, 0xAD, 0xB8) : Rgb(0x6B, 0x72, 0x80),
            ["PclTextTertiaryColor"] = dark ? Rgb(0x7C, 0x82, 0x8D) : Rgb(0x9A, 0xA1, 0xAC),
            ["PclArtworkStartColor"] = Blend(surface, accent, dark ? 0.22 : 0.12),
            ["PclArtworkEndColor"] = Blend(surfaceAlt, Blend(secondary, tertiary, 0.5), dark ? 0.30 : 0.18),
        };
    }

    /// <summary>Writes the palette derived from <see cref="Current"/> into the merged PCL theme dictionary.</summary>
    public static void Apply()
    {
        if (Application.Current?.Resources is not { } app) return;
        var theme = app.MergedDictionaries.FirstOrDefault(d =>
            d.Source?.OriginalString.Contains("Themes/PclTheme.xaml", StringComparison.OrdinalIgnoreCase) == true);
        if (theme is null) return;
        foreach (var (key, dark) in new[] { ("Default", false), ("Light", false), ("Dark", true) })
        {
            if (!theme.ThemeDictionaries.TryGetValue(key, out var value) || value is not ResourceDictionary dictionary) continue;
            foreach (var (name, color) in Build(Current, dark)) dictionary[name] = color;
        }

        // The Pcl*Brush instances are created once from ThemeResource colors, so changing a Color entry
        // afterwards does not repaint them. Mutate the shared brush instances instead: every control that
        // took a StaticResource/ThemeResource reference keeps pointing at the same object.
        var isDark = MainWindow.Instance?.Content is FrameworkElement root && root.ActualTheme == ElementTheme.Dark;
        var palette = Build(Current, isDark);
        foreach (var (brushKey, colorKey) in BrushMap)
        {
            if (theme[brushKey] is SolidColorBrush brush && palette.TryGetValue(colorKey, out var color)) brush.Color = color;
        }

        ApplySystemAccent(app, palette);
    }

    /// <summary>
    /// Legacy pages still use WinUI's AccentButtonStyle/NavigationView indicator, which read the system accent.
    /// Overriding those entries keeps the picked color visible outside the restyled pages.
    /// </summary>
    private static void ApplySystemAccent(ResourceDictionary app, IReadOnlyDictionary<string, Color> palette)
    {
        var accent = palette["PclAccentColor"];
        foreach (var key in new[]
                 {
                     "SystemAccentColor", "SystemAccentColorLight1", "SystemAccentColorLight2", "SystemAccentColorLight3",
                     "SystemAccentColorDark1", "SystemAccentColorDark2", "SystemAccentColorDark3",
                 })
        {
            app[key] = accent;
        }

        // WinUI derives its accent brushes from SystemAccentColor*, so overriding the colors is enough and
        // keeps AccentButtonStyle/NavigationViewIndicator consistent without replacing control templates.
    }

    private static readonly (string Brush, string Color)[] BrushMap =
    [
        ("PclAccentBrush", "PclAccentColor"),
        ("PclAccentHoverBrush", "PclAccentHoverColor"),
        ("PclAccentPressedBrush", "PclAccentPressedColor"),
        ("PclAccentSoftBrush", "PclAccentSoftColor"),
        ("PclPageBackgroundBrush", "PclPageBackgroundColor"),
        ("PclSurfaceBrush", "PclSurfaceColor"),
        ("PclSurfaceAltBrush", "PclSurfaceAltColor"),
        ("PclSurfaceHoverBrush", "PclSurfaceHoverColor"),
        ("PclBorderBrush", "PclBorderColor"),
        ("PclBorderStrongBrush", "PclBorderStrongColor"),
        ("PclTextPrimaryBrush", "PclTextPrimaryColor"),
        ("PclTextSecondaryBrush", "PclTextSecondaryColor"),
        ("PclTextTertiaryBrush", "PclTextTertiaryColor"),
    ];

    /// <summary>Card artwork gradient; replaced at runtime when the account has a real background image.</summary>
    public static LinearGradientBrush ArtworkGradient(bool dark)
    {
        var palette = Build(Current, dark);
        var secondary = Current.Count > 1 ? Current[1] : Current[0];
        return new LinearGradientBrush
        {
            StartPoint = new(0, 0),
            EndPoint = new(1, 1),
            GradientStops =
            {
                new() { Offset = 0, Color = palette["PclArtworkStartColor"] },
                new() { Offset = 0.55, Color = Blend(palette["PclArtworkStartColor"], palette["PclArtworkEndColor"], 0.6) },
                new() { Offset = 1, Color = Blend(palette["PclArtworkEndColor"], secondary, 0.25) },
            },
        };
    }

    private static Color Rgb(byte r, byte g, byte b) => Color.FromArgb(255, r, g, b);

    private static Color Blend(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromArgb(255,
            (byte)Math.Round(a.R + (b.R - a.R) * t),
            (byte)Math.Round(a.G + (b.G - a.G) * t),
            (byte)Math.Round(a.B + (b.B - a.B) * t));
    }
}
