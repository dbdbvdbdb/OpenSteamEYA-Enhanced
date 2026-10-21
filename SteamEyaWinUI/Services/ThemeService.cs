namespace SteamEyaWinUI.Services;

/// <summary>
/// 全局主题服务：持有当前主题输入（主色 / 辅助色 / 背景倾向），负责读取配置、预览、
/// 应用与回退。所有界面只依赖派生后的语义画刷，不直接读用户输入。
/// </summary>
internal static class ThemeService
{
    private static ThemeSpec _current = ThemeSpec.Default;
    private static ThemeSpec? _previewBase;

    public static ThemeSpec Current => _current;

    public static bool IsCustom => !_current.IsDefault;

    public static bool IsPreviewing => _previewBase is not null;

    /// <summary>主题真正发生变化后触发，供状态栏或设置页刷新摘要文案。</summary>
    public static event Action? Changed;

    /// <summary>从设置文件恢复主题；配置缺失或非法时回退到默认蓝色主题。</summary>
    public static void LoadFromSettings()
    {
        AppSettings settings;
        try
        {
            settings = AppState.SettingsService.Load();
        }
        catch
        {
            _current = ThemeSpec.Default;
            return;
        }

        _current = ThemeSpec.FromSettings(settings.ThemePrimaryHex, settings.ThemeSecondaryHex, settings.ThemeTint);
    }

    /// <summary>只更新内存中的主题，不写配置、不重绘（供兼容入口与批量设置使用）。</summary>
    public static void SetSpec(ThemeSpec spec) => _current = spec;

    /// <summary>把当前主题写入资源字典；<paramref name="refreshPages"/> 会重建当前页以刷新代码里缓存的画刷。</summary>
    public static void Repaint(bool refreshPages = true)
    {
        ThemePaletteService.Apply();
        if (refreshPages) MainWindow.Instance?.RefreshThemePalette();
        Changed?.Invoke();
    }

    /// <summary>进入编辑预览：记住当前主题，取消时用它回退。</summary>
    public static void BeginPreview() => _previewBase ??= _current;

    /// <summary>取消预览并恢复进入编辑前的主题。</summary>
    public static void CancelPreview()
    {
        if (_previewBase is not { } previous) return;
        _previewBase = null;
        _current = previous;
        Repaint();
    }

    /// <summary>确认应用：写入设置并重绘；失败时保留内存主题不崩溃。</summary>
    public static bool Commit(ThemeSpec spec)
    {
        _current = spec;
        _previewBase = null;
        Repaint();
        try
        {
            var settings = AppState.SettingsService.Load();
            settings.ThemePrimaryHex = spec.PrimaryHex;
            settings.ThemeSecondaryHex = spec.SecondaryHex;
            settings.ThemeTint = spec.TintKey;
            settings.ThemeColors = [.. spec.ToColorList()];
            AppState.SettingsService.Save(settings);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>恢复默认主题（只改主题，不影响深色/浅色模式）。</summary>
    public static bool Reset() => Commit(ThemeSpec.Default);

    public static string Summary() => _current.IsDefault ? "内置蓝色主题" : _current.Describe();
}
