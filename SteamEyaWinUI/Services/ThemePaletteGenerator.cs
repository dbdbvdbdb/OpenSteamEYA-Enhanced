using System.Globalization;
using Windows.UI;

namespace SteamEyaWinUI.Services;

/// <summary>
/// 从用户选择的少量颜色派生出一整套语义色板。
/// 纯函数、确定性：同一组输入永远得到同一组输出，便于测试与截图比对。
/// </summary>
internal static class ThemePaletteGenerator
{
    public const string Accent = "PclAccentColor";
    public const string AccentHover = "PclAccentHoverColor";
    public const string AccentPressed = "PclAccentPressedColor";
    public const string AccentSoft = "PclAccentSoftColor";
    public const string Focus = "PclFocusColor";
    public const string PageBackground = "PclPageBackgroundColor";
    public const string Surface = "PclSurfaceColor";
    public const string SurfaceAlt = "PclSurfaceAltColor";
    public const string SurfaceHover = "PclSurfaceHoverColor";
    public const string Input = "PclInputColor";
    public const string Dialog = "PclDialogColor";
    public const string Border = "PclBorderColor";
    public const string BorderStrong = "PclBorderStrongColor";
    public const string Divider = "PclDividerColor";
    public const string TextPrimary = "PclTextPrimaryColor";
    public const string TextSecondary = "PclTextSecondaryColor";
    public const string TextTertiary = "PclTextTertiaryColor";
    public const string TextDisabled = "PclTextDisabledColor";
    public const string OnAccent = "PclOnAccentColor";
    public const string Success = "PclSuccessColor";
    public const string Warning = "PclWarningColor";
    public const string Error = "PclErrorColor";
    public const string Info = "PclInfoColor";
    public const string SuccessSoft = "PclSuccessSoftColor";
    public const string WarningSoft = "PclWarningSoftColor";
    public const string ErrorSoft = "PclErrorSoftColor";
    public const string InfoSoft = "PclInfoSoftColor";
    public const string ArtworkStart = "PclArtworkStartColor";
    public const string ArtworkEnd = "PclArtworkEndColor";

    public static readonly string[] TokenKeys =
    [
        Accent, AccentHover, AccentPressed, AccentSoft, Focus,
        PageBackground, Surface, SurfaceAlt, SurfaceHover, Input, Dialog,
        Border, BorderStrong, Divider,
        TextPrimary, TextSecondary, TextTertiary, TextDisabled, OnAccent,
        Success, Warning, Error, Info, SuccessSoft, WarningSoft, ErrorSoft, InfoSoft,
        ArtworkStart, ArtworkEnd,
    ];

    /// <summary>内置蓝色主题的派生结果，供“恢复默认”与回归测试使用。</summary>
    public static IReadOnlyDictionary<string, Color> Build(ThemeSpec spec, bool dark)
    {
        var primary = Parse(spec.PrimaryHex);
        var secondary = Parse(spec.SecondaryHex);

        var (ph, ps, pl) = ToHsl(primary);
        var (sh, _, _) = ToHsl(secondary);

        // 强调色：保证足够的饱和度与对比，避免浅黄/浅灰之类的颜色让按钮文字不可读。
        var accent = FromHsl(ph, Math.Max(ps, 0.52), dark ? Plane(pl, 0.58, 0.74) : Plane(pl, 0.38, 0.56));
        var accentHover = FromHsl(ph, Math.Max(ps, 0.52), dark ? Plane(pl, 0.63, 0.80) : Plane(pl, 0.44, 0.62));
        var accentPressed = FromHsl(ph, Math.Max(ps, 0.52), dark ? Plane(pl, 0.50, 0.66) : Plane(pl, 0.30, 0.48));
        accent = SafeAccent(accent, dark);
        var accentSoft = FromHsl(ph, Math.Max(ps, 0.45), dark ? 0.205 : 0.935);

        // 背景倾向决定中性面的色相：冷 / 中性 / 暖。
        var tintHue = spec.Tint switch
        {
            ThemeTint.Cool => 216.0,
            ThemeTint.Warm => 32.0,
            _ => ph,
        };
        var tintSat = spec.Tint == ThemeTint.Neutral ? 0.16 : 0.22;

        var pageBackground = FromHsl(tintHue, dark ? tintSat * 0.85 : tintSat * 0.55, dark ? 0.114 : 0.972);
        var surface = FromHsl(tintHue, dark ? tintSat * 0.55 : tintSat * 0.18, dark ? 0.164 : 0.999);
        var surfaceAlt = FromHsl(tintHue, dark ? tintSat * 0.60 : tintSat * 0.26, dark ? 0.196 : 0.966);
        var surfaceHover = FromHsl(tintHue, dark ? tintSat * 0.65 : tintSat * 0.30, dark ? 0.238 : 0.941);
        var input = FromHsl(tintHue, dark ? tintSat * 0.50 : tintSat * 0.14, dark ? 0.188 : 0.988);
        var dialog = FromHsl(tintHue, dark ? tintSat * 0.55 : tintSat * 0.16, dark ? 0.193 : 1.0);

        var border = FromHsl(tintHue, dark ? tintSat * 0.70 : tintSat * 0.80, dark ? 0.298 : 0.884);
        var borderStrong = FromHsl(tintHue, dark ? tintSat * 0.75 : tintSat * 0.75, dark ? 0.382 : 0.798);
        var divider = FromHsl(tintHue, dark ? tintSat * 0.60 : tintSat * 0.55, dark ? 0.256 : 0.916);

        var textPrimary = FromHsl(ph, dark ? 0.06 : 0.16, dark ? 0.945 : 0.142);
        var textSecondary = FromHsl(ph, dark ? 0.06 : 0.10, dark ? 0.720 : 0.452);
        var textTertiary = FromHsl(ph, dark ? 0.05 : 0.08, dark ? 0.548 : 0.612);
        var textDisabled = FromHsl(ph, dark ? 0.04 : 0.06, dark ? 0.400 : 0.762);

        // 正文/次要文字必须可读：按 WCAG 对比度逐步把文字推离底面。
        textPrimary = EnsureContrast(textPrimary, surface, 7.0);
        textPrimary = EnsureContrast(textPrimary, pageBackground, 7.0);
        textSecondary = EnsureContrast(textSecondary, surface, 4.5);
        textTertiary = EnsureContrast(textTertiary, surface, 2.6);
        textDisabled = EnsureContrast(textDisabled, surface, 1.9);

        var onAccent = OnColorFor(accent);

        var focus = FromHsl(ph, Math.Max(ps, 0.60), dark ? 0.70 : 0.46);

        var success = StatusColor(146, dark);
        var warning = StatusColor(38, dark);
        var error = StatusColor(5, dark);
        var info = StatusColor(212, dark);
        var successSoft = StatusSoft(146, dark);
        var warningSoft = StatusSoft(38, dark);
        var errorSoft = StatusSoft(5, dark);
        var infoSoft = StatusSoft(212, dark);

        var artworkStart = FromHsl(ph, Math.Max(ps, 0.50), dark ? 0.185 : 0.938);
        var artworkEnd = FromHsl(sh, Math.Max(ps, 0.45), dark ? 0.245 : 0.900);

        return new Dictionary<string, Color>(StringComparer.Ordinal)
        {
            [Accent] = accent,
            [AccentHover] = accentHover,
            [AccentPressed] = accentPressed,
            [AccentSoft] = accentSoft,
            [Focus] = focus,
            [PageBackground] = pageBackground,
            [Surface] = surface,
            [SurfaceAlt] = surfaceAlt,
            [SurfaceHover] = surfaceHover,
            [Input] = input,
            [Dialog] = dialog,
            [Border] = border,
            [BorderStrong] = borderStrong,
            [Divider] = divider,
            [TextPrimary] = textPrimary,
            [TextSecondary] = textSecondary,
            [TextTertiary] = textTertiary,
            [TextDisabled] = textDisabled,
            [OnAccent] = onAccent,
            [Success] = success,
            [Warning] = warning,
            [Error] = error,
            [Info] = info,
            [SuccessSoft] = successSoft,
            [WarningSoft] = warningSoft,
            [ErrorSoft] = errorSoft,
            [InfoSoft] = infoSoft,
            [ArtworkStart] = artworkStart,
            [ArtworkEnd] = artworkEnd,
        };
    }

    public static string DeriveSecondary(Color primary)
    {
        var (h, s, l) = ToHsl(primary);
        return ToHex(FromHsl((h + 42) % 360, Math.Max(s, 0.45), Plane(l, 0.42, 0.62)));
    }

    // ---------- 颜色工具 ----------

    public static Color Parse(string hex)
    {
        if (TryParseHex(hex, out var color)) return color;
        return Color.FromArgb(255, 0x13, 0x70, 0xF3);
    }

    public static bool TryParseHex(string? hex, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(hex)) return false;
        var text = hex.Trim();
        if (text.StartsWith('#')) text = text[1..];
        if (text.Length == 3)
        {
            text = string.Concat(text.Select(c => new string(c, 2)));
        }
        if (text.Length == 8) text = text[2..];
        if (text.Length != 6 || !uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value)) return false;
        color = Color.FromArgb(255, (byte)((value >> 16) & 0xFF), (byte)((value >> 8) & 0xFF), (byte)(value & 0xFF));
        return true;
    }

    public static string ToHex(Color color) =>
        string.Create(CultureInfo.InvariantCulture, $"#{color.R:X2}{color.G:X2}{color.B:X2}");

    public static (double H, double S, double L) ToHsl(Color color)
    {
        double r = color.R / 255.0, g = color.G / 255.0, b = color.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var l = (max + min) / 2.0;
        if (Math.Abs(max - min) < 0.00001) return (0, 0, l);

        var d = max - min;
        var s = l > 0.5 ? d / (2.0 - max - min) : d / (max + min);
        double h;
        if (max == r) h = (g - b) / d + (g < b ? 6 : 0);
        else if (max == g) h = (b - r) / d + 2;
        else h = (r - g) / d + 4;
        return (h * 60.0, s, l);
    }

    public static Color FromHsl(double h, double s, double l)
    {
        h = ((h % 360) + 360) % 360;
        s = Math.Clamp(s, 0, 1);
        l = Math.Clamp(l, 0, 1);
        var c = (1 - Math.Abs(2 * l - 1)) * s;
        var x = c * (1 - Math.Abs((h / 60.0 % 2) - 1));
        var m = l - c / 2;
        var (r1, g1, b1) = h switch
        {
            < 60 => (c, x, 0.0),
            < 120 => (x, c, 0.0),
            < 180 => (0.0, c, x),
            < 240 => (0.0, x, c),
            < 300 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };
        return Color.FromArgb(255, Channel(r1 + m), Channel(g1 + m), Channel(b1 + m));
    }

    public static double RelativeLuminance(Color color)
    {
        static double Channel(double value)
        {
            var v = value / 255.0;
            return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
    }

    public static double Contrast(Color a, Color b)
    {
        var la = RelativeLuminance(a);
        var lb = RelativeLuminance(b);
        var light = Math.Max(la, lb);
        var dark = Math.Min(la, lb);
        return (light + 0.05) / (dark + 0.05);
    }

    /// <summary>把正文色朝“远离背景”的方向推，直到达到目标对比度。</summary>
    public static Color EnsureContrast(Color foreground, Color background, double target)
    {
        if (Contrast(foreground, background) >= target) return foreground;
        var (h, s, l) = ToHsl(foreground);
        var backgroundIsLight = RelativeLuminance(background) > 0.5;
        var step = backgroundIsLight ? -0.02 : 0.02;
        for (var i = 0; i < 50; i++)
        {
            l = Math.Clamp(l + step, 0.02, 0.98);
            var candidate = FromHsl(h, s, l);
            if (Contrast(candidate, background) >= target) return candidate;
        }
        return FromHsl(h, s, backgroundIsLight ? 0.05 : 0.97);
    }

    /// <summary>把强调色挪到与自身前景色对比度足够的亮度上（按钮文字必须可读）。</summary>
    private static Color SafeAccent(Color accent, bool dark)
    {
        if (Contrast(OnColorFor(accent), accent) >= 4.5) return accent;
        var (h, s, l) = ToHsl(accent);
        for (int i = 0; i < 24; i++)
        {
            l = Math.Clamp(dark ? l + 0.02 : l - 0.02, 0.12, 0.94);
            var candidate = FromHsl(h, s, l);
            if (Contrast(OnColorFor(candidate), candidate) >= 4.5) return candidate;
        }
        return FromHsl(h, s, l);
    }
    private static Color OnColorFor(Color accent)
    {
        var white = Color.FromArgb(255, 255, 255, 255);
        var dark = Color.FromArgb(255, 0x1B, 0x1E, 0x24);
        var whiteContrast = Contrast(white, accent);
        var darkContrast = Contrast(dark, accent);
        if (whiteContrast >= 4.5) return white;
        if (darkContrast >= 4.5) return dark;
        return whiteContrast >= darkContrast ? white : dark;
    }

    private static Color StatusColor(double hue, bool dark) =>
        FromHsl(hue, 0.62, dark ? 0.700 : 0.400);

    private static Color StatusSoft(double hue, bool dark) =>
        FromHsl(hue, dark ? 0.42 : 0.50, dark ? 0.215 : 0.938);

    private static double Plane(double value, double min, double max) => Math.Clamp(value, min, max);

    private static byte Channel(double value) => (byte)Math.Clamp(Math.Round(value * 255.0), 0, 255);
}
