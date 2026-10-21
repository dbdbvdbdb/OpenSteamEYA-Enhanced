using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Navigation;
using SteamEyaWinUI.Services;
using Windows.UI;

namespace SteamEyaWinUI.Pages;

/// <summary>主题编辑器：选择主色与辅助色，实时预览派生结果，应用或取消。</summary>
public sealed partial class ThemeEditorPage : Page
{
    private static readonly (string Name, string Hex)[] Presets =
    [
        ("默认蓝", "#1370F3"),
        ("靛蓝", "#5B5BD6"),
        ("青色", "#0E9AA7"),
        ("绿色", "#2E9E5B"),
        ("橙色", "#E07B39"),
        ("玫红", "#D6456B"),
    ];

    private bool _syncing;

    public ThemeEditorPage()
    {
        InitializeComponent();
        BuildPresets();
        LoadSpec(ThemeService.Current);
        ThemeService.BeginPreview();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        if (ThemeService.IsPreviewing) ThemeService.CancelPreview();
    }

    private void BuildPresets()
    {
        foreach (var (name, hex) in Presets)
        {
            if (!ThemePaletteService.TryParseHex(hex, out var color)) continue;
            var button = new Button
            {
                Width = 36,
                Height = 36,
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(18),
                BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(color),
                BorderBrush = (Brush)Application.Current.Resources["PclBorderBrush"],
                Tag = hex,
            };
            ToolTipService.SetToolTip(button, name);
            button.Click += Preset_Click;
            PresetPanel.Children.Add(button);
        }
    }

    private void LoadSpec(ThemeSpec spec)
    {
        _syncing = true;
        PrimaryHexBox.Text = spec.PrimaryHex;
        SecondaryHexBox.Text = spec.SecondaryHex;
        var tintRadio = spec.Tint switch
        {
            ThemeTint.Cool => TintCool,
            ThemeTint.Warm => TintWarm,
            _ => TintNeutral,
        };
        tintRadio.IsChecked = true;
        _syncing = false;
        UpdateSwatches(spec);
        RenderPreview(spec);
    }

    private void UpdateSwatches(ThemeSpec spec)
    {
        if (ThemePaletteService.TryParseHex(spec.PrimaryHex, out var primary))
            PrimarySwatch.Background = new SolidColorBrush(primary);
        if (ThemePaletteService.TryParseHex(spec.SecondaryHex, out var secondary))
            SecondarySwatch.Background = new SolidColorBrush(secondary);
        else
            SecondarySwatch.Background = (Brush)Application.Current.Resources["PclSurfaceAltBrush"];
    }

    private ThemeSpec? ReadSpec()
    {
        if (!ThemePaletteService.TryParseHex(PrimaryHexBox.Text?.Trim(), out var primary)) return null;
        var secondaryText = SecondaryHexBox.Text?.Trim() ?? string.Empty;
        if (secondaryText.Length > 0 && !ThemePaletteService.TryParseHex(secondaryText, out _)) return null;
        var tint = TintCool.IsChecked == true ? ThemeTint.Cool
            : TintWarm.IsChecked == true ? ThemeTint.Warm
            : ThemeTint.Neutral;
        var secondary = string.Empty;
        if (secondaryText.Length > 0 && ThemePaletteService.TryParseHex(secondaryText, out var parsed))
            secondary = ThemePaletteService.ToHex(parsed);
        return new ThemeSpec(ThemePaletteService.ToHex(primary), secondary, tint);
    }

    /// <summary>把当前输入临时应用到全局主题（不写配置），用于实时预览。</summary>
    private void Preview()
    {
        if (_syncing) return;
        var spec = ReadSpec();
        if (spec is null)
        {
            StatusText.Text = "颜色格式需要是 #RRGGBB";
            return;
        }

        StatusText.Text = "预览中…";
        UpdateSwatches(spec);
        ThemeService.SetSpec(spec);
        ThemeService.Repaint(refreshPages: false);
    }

    private void PrimaryHexBox_TextChanged(object sender, TextChangedEventArgs e) => Preview();

    private void SecondaryHexBox_TextChanged(object sender, TextChangedEventArgs e) => Preview();

    private void Tint_Checked(object sender, RoutedEventArgs e) => Preview();

    private void Preset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string hex }) return;
        _syncing = true;
        PrimaryHexBox.Text = hex;
        SecondaryHexBox.Text = string.Empty;
        _syncing = false;
        Preview();
    }

    private void PrimaryPickButton_Click(object sender, RoutedEventArgs e) => ShowPicker(PrimaryHexBox);

    private void SecondaryPickButton_Click(object sender, RoutedEventArgs e) => ShowPicker(SecondaryHexBox);

    private void ShowPicker(TextBox box)
    {
        if (!ThemePaletteService.TryParseHex(box.Text, out var current))
            current = Color.FromArgb(255, 0x13, 0x70, 0xF3);
        var picker = new ColorPicker
        {
            Color = current,
            IsAlphaEnabled = false,
            IsColorChannelTextInputVisible = true,
            IsColorPreviewVisible = true,
            IsColorSliderVisible = true,
        };
        picker.ColorChanged += (_, args) =>
        {
            _syncing = true;
            box.Text = ThemePaletteService.ToHex(args.NewColor);
            _syncing = false;
            Preview();
        };
        new Flyout { Content = picker }.ShowAt(box);
    }

    private void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        var spec = ReadSpec();
        if (spec is null)
        {
            StatusText.Text = "颜色格式需要是 #RRGGBB";
            return;
        }

        if (!ThemeService.Commit(spec)) StatusText.Text = "主题已应用，但写入设置失败；重启后会恢复原样。";
        NavigateBack();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        ThemeService.CancelPreview();
        NavigateBack();
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        LoadSpec(ThemeSpec.Default);
        ThemeService.SetSpec(ThemeSpec.Default);
        ThemeService.Repaint(refreshPages: false);
        StatusText.Text = "已恢复内置蓝色主题（尚未保存）";
    }

    private void RenderPreview(ThemeSpec spec)
    {
        RenderPreviewColumn(LightPreviewCard, LightPreviewContent, spec, false, "浅色");
        RenderPreviewColumn(DarkPreviewCard, DarkPreviewContent, spec, true, "深色");
    }

    /// <summary>预览面板用生成出的色值直接绘制，这样浅色/深色两套可以同时展示。</summary>
    private static void RenderPreviewColumn(Border card, StackPanel host, ThemeSpec spec, bool dark, string caption)
    {
        var palette = ThemePaletteGenerator.Build(spec, dark);
        SolidColorBrush Brush(string key) => new(palette[key]);
        card.Background = Brush(ThemePaletteGenerator.PageBackground);
        card.BorderBrush = Brush(ThemePaletteGenerator.Border);
        host.Children.Clear();
        host.Children.Add(Caption(caption, Brush(ThemePaletteGenerator.TextTertiary)));
        host.Children.Add(Line("一级标题文字", 18, Brush(ThemePaletteGenerator.TextPrimary), true));
        host.Children.Add(Line("二级说明文字", 13, Brush(ThemePaletteGenerator.TextSecondary), false));
        host.Children.Add(Line("三级提示文字", 12, Brush(ThemePaletteGenerator.TextTertiary), false));

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(Pill("主按钮", Brush(ThemePaletteGenerator.Accent), Brush(ThemePaletteGenerator.OnAccent)));
        buttons.Children.Add(Pill("次按钮", Brush(ThemePaletteGenerator.SurfaceAlt), Brush(ThemePaletteGenerator.TextPrimary), Brush(ThemePaletteGenerator.Border)));
        host.Children.Add(buttons);

        host.Children.Add(new Border
        {
            Background = Brush(ThemePaletteGenerator.Surface),
            BorderBrush = Brush(ThemePaletteGenerator.Border),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 8, 10, 8),
            Child = Line("卡片内容", 13, Brush(ThemePaletteGenerator.TextPrimary), false),
        });

        var chips = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        chips.Children.Add(Chip("成功", palette[ThemePaletteGenerator.Success], palette[ThemePaletteGenerator.SuccessSoft]));
        chips.Children.Add(Chip("警告", palette[ThemePaletteGenerator.Warning], palette[ThemePaletteGenerator.WarningSoft]));
        chips.Children.Add(Chip("错误", palette[ThemePaletteGenerator.Error], palette[ThemePaletteGenerator.ErrorSoft]));
        host.Children.Add(chips);
    }

    private static TextBlock Caption(string text, Brush foreground) => new() { Text = text, FontSize = 12, Foreground = foreground };

    private static TextBlock Line(string text, double size, Brush foreground, bool bold) => new()
    {
        Text = text,
        FontSize = size,
        Foreground = foreground,
        FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
    };

    private static Border Pill(string text, Brush background, Brush foreground, Brush? border = null) => new()
    {
        Background = background,
        BorderBrush = border,
        BorderThickness = new Thickness(border is null ? 0 : 1),
        CornerRadius = new CornerRadius(8),
        Padding = new Thickness(14, 5, 14, 5),
        Child = new TextBlock { Text = text, Foreground = foreground, FontSize = 13 },
    };

    private static Border Chip(string text, Color foreground, Color background) => new()
    {
        Background = new SolidColorBrush(background),
        CornerRadius = new CornerRadius(6),
        Padding = new Thickness(8, 3, 8, 3),
        Child = new TextBlock { Text = text, Foreground = new SolidColorBrush(foreground), FontSize = 11 },
    };
    private void NavigateBack()
    {
        if (Frame.CanGoBack) Frame.GoBack();
        else Frame.Navigate(typeof(SettingsPage));
    }
}
