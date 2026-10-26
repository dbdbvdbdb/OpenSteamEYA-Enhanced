using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace SteamEyaWinUI.Services;

/// <summary>
/// 把 <see cref="ThemeService"/> 当前的主题写入资源字典，并对外提供派生的画刷。
/// 保留原有静态入口（Build / Apply / ArtworkGradient），供页面与自检代码使用。
/// </summary>
internal static class ThemePaletteService
{
    public const int MaxColors = 5;

    public static IReadOnlyList<Color> Current
    {
        get
        {
            var spec = ThemeService.Current;
            return [ThemePaletteGenerator.Parse(spec.PrimaryHex), ThemePaletteGenerator.Parse(spec.SecondaryHex)];
        }
    }

    public static IReadOnlyList<Color> Parse(IEnumerable<string>? hexColors)
    {
        var spec = ThemeSpec.FromColors(hexColors);
        return [ThemePaletteGenerator.Parse(spec.PrimaryHex), ThemePaletteGenerator.Parse(spec.SecondaryHex)];
    }

    public static bool TryParseHex(string? text, out Color color) => ThemePaletteGenerator.TryParseHex(text, out color);

    public static string ToHex(Color color) => ThemePaletteGenerator.ToHex(color);

    /// <summary>兼容旧入口：把颜色写入当前主题（不改持久化设置）。</summary>
    public static void SetColors(IEnumerable<string>? hexColors) => ThemeService.SetSpec(ThemeSpec.FromColors(hexColors));

    public static IReadOnlyDictionary<string, Color> Build(IReadOnlyList<Color> colors, bool dark) => PaletteFor(SpecFromColors(colors), dark);

    /// <summary>当前主题的调色板；按「主色+辅色+倾向+深浅」缓存，卡片共享同一份结果（计划 T2）。</summary>
    public static IReadOnlyDictionary<string, Color> Palette(bool dark) => PaletteFor(ThemeService.Current, dark);

    private static ThemeSpec SpecFromColors(IReadOnlyList<Color> colors) => colors.Count switch
    {
        0 => ThemeSpec.Default,
        1 => new ThemeSpec(ThemePaletteGenerator.ToHex(colors[0]), ThemePaletteGenerator.DeriveSecondary(colors[0]), ThemeSpec.Default.Tint),
        _ => new ThemeSpec(ThemePaletteGenerator.ToHex(colors[0]), ThemePaletteGenerator.ToHex(colors[1]), ThemeSpec.Default.Tint),
    };

    private static readonly Dictionary<(string Spec, bool Dark), IReadOnlyDictionary<string, Color>> PaletteCache = new();

    private static IReadOnlyDictionary<string, Color> PaletteFor(ThemeSpec spec, bool dark)
    {
        var key = (spec.PrimaryHex + "|" + spec.SecondaryHex + "|" + spec.TintKey, dark);
        lock (PaletteCache)
        {
            if (PaletteCache.TryGetValue(key, out var cached)) return cached;
        }

        var built = ThemePaletteGenerator.Build(spec, dark);
        lock (PaletteCache)
        {
            PaletteCache[key] = built;
        }
        return built;
    }

    /// <summary>主题色变化后清除缓存，避免卡片继续复用旧调色板。</summary>
    internal static void InvalidatePaletteCache()
    {
        lock (PaletteCache)
        {
            PaletteCache.Clear();
            ArtworkGradients.Clear();
        }
    }

    /// <summary>把当前主题写入三个主题字典，并更新共享画刷实例。</summary>
    public static void Apply()
    {
        if (Application.Current?.Resources is not { } app) return;
        var theme = FindThemeDictionary(app);
        if (theme is null) return;

        foreach (var (key, dark) in new[] { ("Default", false), ("Light", false), ("Dark", true) })
        {
            if (!theme.ThemeDictionaries.TryGetValue(key, out var value) || value is not ResourceDictionary dictionary) continue;
            foreach (var (name, color) in PaletteFor(ThemeService.Current, dark)) dictionary[name] = color;
        }

        // Pcl*Brush 实例在字典里只创建一次，改 Color 资源不会重绘它们；直接改共享实例的颜色。
        var isDark = MainWindow.Instance?.Content is FrameworkElement root && root.ActualTheme == ElementTheme.Dark;
        var palette = PaletteFor(ThemeService.Current, isDark);
        foreach (var (brushKey, colorKey) in BrushMap)
        {
            if (theme[brushKey] is SolidColorBrush brush && palette.TryGetValue(colorKey, out var color)) brush.Color = color;
        }

        ApplySystemAccent(app, palette);
    }

    internal static ResourceDictionary? FindThemeDictionary(ResourceDictionary app) =>
        app.MergedDictionaries.FirstOrDefault(d =>
            d.Source?.OriginalString.Contains("Themes/PclTheme.xaml", StringComparison.OrdinalIgnoreCase) == true);

    private static void ApplySystemAccent(ResourceDictionary app, IReadOnlyDictionary<string, Color> palette)
    {
        var accent = palette[ThemePaletteGenerator.Accent];
        foreach (var key in new[]
                 {
                     "SystemAccentColor", "SystemAccentColorLight1", "SystemAccentColorLight2", "SystemAccentColorLight3",
                     "SystemAccentColorDark1", "SystemAccentColorDark2", "SystemAccentColorDark3",
                 })
        {
            app[key] = accent;
        }
    }

    private static readonly (string Brush, string Color)[] BrushMap =
    [
        ("PclAccentBrush", ThemePaletteGenerator.Accent),
        ("PclAccentHoverBrush", ThemePaletteGenerator.AccentHover),
        ("PclAccentPressedBrush", ThemePaletteGenerator.AccentPressed),
        ("PclAccentSoftBrush", ThemePaletteGenerator.AccentSoft),
        ("PclFocusBrush", ThemePaletteGenerator.Focus),
        ("PclPageBackgroundBrush", ThemePaletteGenerator.PageBackground),
        ("PclSurfaceBrush", ThemePaletteGenerator.Surface),
        ("PclSurfaceAltBrush", ThemePaletteGenerator.SurfaceAlt),
        ("PclSurfaceHoverBrush", ThemePaletteGenerator.SurfaceHover),
        ("PclInputBrush", ThemePaletteGenerator.Input),
        ("PclDialogBrush", ThemePaletteGenerator.Dialog),
        ("PclBorderBrush", ThemePaletteGenerator.Border),
        ("PclBorderStrongBrush", ThemePaletteGenerator.BorderStrong),
        ("PclDividerBrush", ThemePaletteGenerator.Divider),
        ("PclTextPrimaryBrush", ThemePaletteGenerator.TextPrimary),
        ("PclTextSecondaryBrush", ThemePaletteGenerator.TextSecondary),
        ("PclTextTertiaryBrush", ThemePaletteGenerator.TextTertiary),
        ("PclTextDisabledBrush", ThemePaletteGenerator.TextDisabled),
        ("PclOnAccentBrush", ThemePaletteGenerator.OnAccent),
        ("PclSuccessBrush", ThemePaletteGenerator.Success),
        ("PclWarningBrush", ThemePaletteGenerator.Warning),
        ("PclErrorBrush", ThemePaletteGenerator.Error),
        ("PclInfoBrush", ThemePaletteGenerator.Info),
        ("PclSuccessSoftBrush", ThemePaletteGenerator.SuccessSoft),
        ("PclWarningSoftBrush", ThemePaletteGenerator.WarningSoft),
        ("PclErrorSoftBrush", ThemePaletteGenerator.ErrorSoft),
        ("PclInfoSoftBrush", ThemePaletteGenerator.InfoSoft),
    ];

    /// <summary>卡片顶部渐变；账号设置了真实背景图时会被 ImageBrush 覆盖。</summary>
    private static readonly Dictionary<(string Spec, bool Dark), LinearGradientBrush> ArtworkGradients = new();

    public static LinearGradientBrush ArtworkGradient(bool dark)
    {
        var spec = ThemeService.Current;
        var key = (spec.PrimaryHex + "|" + spec.SecondaryHex + "|" + spec.TintKey, dark);
        lock (PaletteCache)
        {
            if (ArtworkGradients.TryGetValue(key, out var cached)) return cached;
        }

        var palette = PaletteFor(spec, dark);
        var brush = new LinearGradientBrush
        {
            StartPoint = new(0, 0),
            EndPoint = new(1, 1),
            GradientStops =
            {
                new() { Offset = 0, Color = palette[ThemePaletteGenerator.ArtworkStart] },
                new() { Offset = 0.55, Color = Blend(palette[ThemePaletteGenerator.ArtworkStart], palette[ThemePaletteGenerator.ArtworkEnd], 0.6) },
                new()
                {
                    Offset = 1,
                    Color = Blend(palette[ThemePaletteGenerator.ArtworkEnd], ThemePaletteGenerator.Parse(ThemeService.Current.SecondaryHex), 0.25),
                },
            },
        };

        lock (PaletteCache)
        {
            ArtworkGradients[key] = brush;
        }
        return brush;
    }

    private static Color Blend(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromArgb(255,
            (byte)Math.Round(a.R + (b.R - a.R) * t),
            (byte)Math.Round(a.G + (b.G - a.G) * t),
            (byte)Math.Round(a.B + (b.B - a.B) * t));
    }
}
