using System.Globalization;

namespace SteamEyaWinUI.Services;

/// <summary>背景倾向：影响页面底色与卡片底色的色相，不影响强调色。</summary>
internal enum ThemeTint
{
    Cool,
    Neutral,
    Warm,
}

/// <summary>
/// 用户自定义主题的输入：主色、辅助色、背景倾向。
/// 只存输入和用户设置，所有实际颜色由 <see cref="ThemePaletteGenerator"/> 派生。
/// </summary>
internal sealed record ThemeSpec(string PrimaryHex, string SecondaryHex, ThemeTint Tint)
{
    public static ThemeSpec Default { get; } = new("#1370F3", "#6B5CF0", ThemeTint.Neutral);

    public bool IsDefault =>
        string.Equals(PrimaryHex, Default.PrimaryHex, StringComparison.OrdinalIgnoreCase)
        && string.Equals(SecondaryHex, Default.SecondaryHex, StringComparison.OrdinalIgnoreCase)
        && Tint == Default.Tint;

    public string TintKey => Tint switch
    {
        ThemeTint.Cool => "cool",
        ThemeTint.Warm => "warm",
        _ => "neutral",
    };

    public static ThemeTint ParseTint(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "cool" => ThemeTint.Cool,
        "warm" => ThemeTint.Warm,
        _ => ThemeTint.Neutral,
    };

    /// <summary>读取用户配置；任一项缺失或格式非法时回退到默认主题。</summary>
    public static ThemeSpec FromSettings(string? primary, string? secondary, string? tint)
    {
        var fallbackPrimary = ThemePaletteGenerator.TryParseHex(primary, out var p) ? ThemePaletteGenerator.ToHex(p) : Default.PrimaryHex;
        var fallbackSecondary = ThemePaletteGenerator.TryParseHex(secondary, out var s) ? ThemePaletteGenerator.ToHex(s) : Default.SecondaryHex;
        return new ThemeSpec(fallbackPrimary, fallbackSecondary, ParseTint(tint));
    }

    public static ThemeSpec FromColors(IEnumerable<string>? colors)
    {
        var list = new List<string>();
        foreach (var item in colors ?? [])
        {
            if (ThemePaletteGenerator.TryParseHex(item, out var color)) list.Add(ThemePaletteGenerator.ToHex(color));
            if (list.Count == 2) break;
        }

        return list.Count switch
        {
            0 => Default,
            1 => new ThemeSpec(list[0], ThemePaletteGenerator.DeriveSecondary(ThemePaletteGenerator.Parse(list[0])), Default.Tint),
            _ => new ThemeSpec(list[0], list[1], Default.Tint),
        };
    }

    public IReadOnlyList<string> ToColorList() => [PrimaryHex, SecondaryHex];

    public string Describe()
    {
        var tint = Tint switch
        {
            ThemeTint.Cool => "冷色背景",
            ThemeTint.Warm => "暖色背景",
            _ => "中性背景",
        };
        return string.Create(CultureInfo.InvariantCulture, $"{PrimaryHex} / {SecondaryHex} · {tint}");
    }
}
