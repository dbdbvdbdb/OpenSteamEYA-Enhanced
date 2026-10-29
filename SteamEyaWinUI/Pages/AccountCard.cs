using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using OpenSteamEya.AccountManagement;
using SteamEyaWinUI.Models;
using SteamEyaWinUI.Services;
using Windows.UI;

namespace SteamEyaWinUI.Pages;

/// <summary>
/// 账号卡片。视觉语言参考 PCL 风格的扁平卡片：浅色/深色主题自适应、细边框、圆角、蓝色强调，
/// 不使用任何 PCL 的界面素材或代码。
/// 卡片由 GridView 的 ItemTemplate 按需创建：构造时只有空壳，<see cref="Item"/> 绑定后才构建整棵子树；
/// 条目被回收/换绑时先 <see cref="Stop"/>，不会把旧条目的事件与后台任务带进新卡片。
/// </summary>
internal sealed partial class AccountCard : UserControl
{
    internal const double CardRadius = 10;

    public static readonly DependencyProperty ItemProperty = DependencyProperty.Register(
        nameof(Item), typeof(ManagedAccountCardItem), typeof(AccountCard),
        new PropertyMetadata(null, (d, _) => ((AccountCard)d).Rebuild()));

    /// <summary>当前绑定的轻量条目。控件树由它构建，换值即重建。</summary>
    public ManagedAccountCardItem? Item
    {
        get => (ManagedAccountCardItem?)GetValue(ItemProperty);
        set => SetValue(ItemProperty, value);
    }

    public ManagedAccount Account => Item!.Account;
    private AccountManagerPage owner = null!;
    private string? key;
    private Border surface = null!;
    private Border emphasis = null!;
    private Border selectedBadge = null!;
    private Grid artwork = null!;
    private Grid scrim = null!;
    private TextBlock password = null!;
    private Button revealButton = null!;
    private bool compact;
    private bool stopped, selected, hovered;
    private CancellationTokenSource backgroundCancellation = new();
    private ManagedAccountCardItem? boundItem;
    private int buildGeneration;
    internal readonly Dictionary<string, Button> Actions = [];

    // 主题调色板（与 Themes/PclTheme.xaml 中的令牌保持一致）。
    private bool dark;
    private Brush textBrush = null!;
    private Brush mutedBrush = null!;
    private Brush surfaceBrush = null!;
    private Brush borderBrush = null!;
    private Brush altBrush = null!;
    private Brush accentBrush = null!;
    private Brush accentSoftBrush = null!;

    public AccountCard()
    {
        // 事件只在构造时订阅一次：换绑条目不会叠加处理器。
        ActualThemeChanged += (_, _) => { if (Item is not null) _ = ApplyBackgroundAsync(); };
        PointerEntered += (_, _) => SetHovered(true);
        PointerExited += (_, _) => SetHovered(false);
        DoubleTapped += async (_, args) =>
        {
            if (Item is null || !IsCardDoubleTapSource(args.OriginalSource)) return;
            args.Handled = true; await OpenEditorAsync();
        };
        Unloaded += (_, _) => Stop();
        // 容器回收后可能带着同一份条目重新出现，而 DP 值没变、不会触发 Rebuild：
        // 这里补一次重建，保证"被回收过的卡片再次可见时控件树是完整的"。
        Loaded += (_, _) =>
        {
            if (Item is not null && stopped) Rebuild();
        };
    }

    /// <summary>丢弃已构建的控件树与后台任务（容器回收或页面离页时调用）。</summary>
    public void Stop()
    {
        stopped = true;
        backgroundCancellation.Cancel();
        if (boundItem is not null) boundItem.PropertyChanged -= OnItemPropertyChanged;
        boundItem = null;
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ManagedAccountCardItem.CooldownText)) RefreshCooldown();
    }

    /// <summary>按当前 <see cref="Item"/> 重新构建整张卡片。</summary>
    internal void Rebuild()
    {
        // 换绑前先收尾上一轮：取消后台取图与条目事件，避免旧条目的回调改写新卡片。
        if (boundItem is not null) Stop();

        stopped = false;
        selected = false; hovered = false;
        Actions.Clear();
        buildGeneration++;
        backgroundCancellation = new CancellationTokenSource();
        if (Item is not { } item)
        {
            Content = null;
            return;
        }

        boundItem = item; item.PropertyChanged += OnItemPropertyChanged;
        owner = item.Owner; key = item.MappingKey; compact = item.Compact;
#if MANAGER_UI_CHECKS
        Services.UiCheckPerf.CardCreated();
#endif
        dark = ActualTheme != ElementTheme.Light;
        var themePalette = ThemePaletteService.Palette(dark);
#if MANAGER_UI_CHECKS
        Services.UiCheckPerf.PaletteBuilt();
#endif
        Brush FromPalette(string paletteKey) => new SolidColorBrush(themePalette[paletteKey]);
        textBrush = FromPalette("PclTextPrimaryColor");
        mutedBrush = FromPalette("PclTextSecondaryColor");
        surfaceBrush = FromPalette("PclSurfaceColor");
        borderBrush = FromPalette("PclBorderColor");
        altBrush = FromPalette("PclSurfaceAltColor");
        accentBrush = FromPalette("PclAccentColor");
        accentSoftBrush = FromPalette("PclAccentSoftColor");

        bool privacy = item.IsPrivate;
        var account = item.Account;
        var history = item.History; var cached = owner.Snapshot.CachedFor(key);
        string steamId = history?.SteamId ?? cached?.SteamId ?? account.SteamId;
        string? avatarPath = history?.AvatarPath ?? cached?.AvatarPath;
        string? avatarUrl = history?.AvatarUrl ?? cached?.AvatarUrl;
#if MANAGER_UI_CHECKS
        if (ManagerUiChecks.Enabled && !account.LoginName.Contains("missing-avatar")) avatarPath = ManagerUiChecks.AvatarPath;
#endif

        var root = new Grid { UseLayoutRounding = true };
        var layout = new Grid { CornerRadius = new CornerRadius(CardRadius - 1) };

        surface = new Border
        {
            CornerRadius = new CornerRadius(CardRadius),
            BorderThickness = new Thickness(1),
            BorderBrush = borderBrush,
            Background = surfaceBrush,
            Child = layout,
        };
        root.Children.Add(surface);

        emphasis = new Border
        {
            CornerRadius = new CornerRadius(CardRadius),
            BorderThickness = new Thickness(2),
            BorderBrush = accentBrush,
            Opacity = 0,
            IsHitTestVisible = false,
            OpacityTransition = new ScalarTransition { Duration = TimeSpan.FromMilliseconds(140) },
        };
        root.Children.Add(emphasis);

        selectedBadge = new Border
        {
            Width = 20, Height = 20, CornerRadius = new CornerRadius(10),
            Background = accentBrush, Child = Icon("\uE73E", 11, Solid(0xFFFFFFFF)),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(8), IsHitTestVisible = false, Visibility = Visibility.Collapsed,
        };
        root.Children.Add(selectedBadge);

        // 头像取色背景铺满整张卡片：底色由头像调色板派生的渐变，或本地/Steam 背景图。
        artwork = new Grid
        {
            CornerRadius = new CornerRadius(CardRadius - 1),
            Background = ThemePaletteService.ArtworkGradient(dark),
            Opacity = dark ? 0.55 : 0.38,
            IsHitTestVisible = false,
        };
        layout.Children.Add(artwork);

        scrim = new Grid { CornerRadius = new CornerRadius(CardRadius - 1), IsHitTestVisible = false, Background = Solid(0x00000000) };
        layout.Children.Add(scrim);

        var profile = new Grid
        {
            ColumnSpacing = 12,
            Margin = new Thickness(compact ? 14 : 16, 0, compact ? 14 : 16, compact ? 0 : 10),
            VerticalAlignment = VerticalAlignment.Center,
        };
        profile.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        profile.ColumnDefinitions.Add(new ColumnDefinition());
        var avatar = new Border
        {
            Width = compact ? 38 : 46, Height = compact ? 38 : 46,
            CornerRadius = new CornerRadius(compact ? 19 : 23), Padding = new Thickness(2),
            BorderThickness = new Thickness(1.5), BorderBrush = accentSoftBrush,
            Background = dark ? Solid(0xFF2B2D31) : Solid(0xFFFFFFFF),
        };
        var avatarContent = new Grid { CornerRadius = new CornerRadius(21), Background = altBrush };
        var avatarPlaceholder = Icon("\uE77B", compact ? 18 : 22, mutedBrush);
        avatarContent.Children.Add(avatarPlaceholder);
        if (!privacy)
        {
            var uri = avatarPath is not null && System.IO.File.Exists(avatarPath) ? avatarPath : avatarUrl;
            if (Uri.TryCreate(uri, UriKind.Absolute, out var imageUri))
            {
                var bitmap = new BitmapImage(imageUri) { DecodePixelWidth = 120 };
                bitmap.ImageOpened += (_, _) => avatarPlaceholder.Visibility = Visibility.Collapsed;
                bitmap.ImageFailed += (_, _) => avatarPlaceholder.Visibility = Visibility.Visible;
                avatarContent.Background = new ImageBrush { ImageSource = bitmap, Stretch = Stretch.UniformToFill };
            }
        }
        avatar.Child = avatarContent; profile.Children.Add(avatar);

        var identity = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        var nickname = Label(privacy ? "隐私账号" : owner.DisplayCardName(account), compact ? 15 : 17, true);
        ToolTipService.SetToolTip(nickname, privacy ? "隐私账号" : owner.DisplayCardName(account));
        identity.Children.Add(nickname);

        var idRow = new Grid { ColumnSpacing = 2, HorizontalAlignment = HorizontalAlignment.Left, MaxWidth = 230 };
        idRow.ColumnDefinitions.Add(new ColumnDefinition());
        idRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        string idText = steamId.Length > 0 ? steamId : "未获取";
        if (!owner.Preferences.FullSteamId && steamId.Length > 8) idText = steamId[..4] + "…" + steamId[^4..];
        idRow.Children.Add(Label(privacy ? "身份信息已隐藏" : "SteamID: " + idText, compact ? 10 : 11, false, mutedBrush));
        var copyId = IconButton("copy-id", "\uE8C8", "复制 SteamID", () => CopyAsync(steamId));
        copyId.IsEnabled = !privacy && steamId.Length > 0; copyId.Width = 22; copyId.Height = 20; copyId.Padding = new Thickness(2);
        copyId.Background = Solid(0x00000000); copyId.BorderThickness = new Thickness(0);
        Grid.SetColumn(copyId, 1); idRow.Children.Add(copyId); identity.Children.Add(idRow);
        Grid.SetColumn(identity, 1); profile.Children.Add(identity);

        var information = new Border
        {
            CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), BorderBrush = borderBrush,
            Background = altBrush, Padding = new Thickness(10, 6, 8, 6),
        };
        var infoGrid = new Grid { RowSpacing = 2, ColumnSpacing = 6 };
        infoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(compact ? 52 : 58) });
        infoGrid.ColumnDefinitions.Add(new ColumnDefinition());
        infoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        infoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        infoGrid.RowDefinitions.Add(new RowDefinition()); infoGrid.RowDefinitions.Add(new RowDefinition());
        infoGrid.Children.Add(Label("登录账号", compact ? 10 : 11, false, mutedBrush));
        var accountText = Label(privacy ? "••••••" : account.LoginName, compact ? 12 : 13); Grid.SetColumn(accountText, 1); infoGrid.Children.Add(accountText);
        Grid.SetColumnSpan(accountText, 2);
        var copyAccount = ActionButton("copy-account", "复制", "复制登录账号", () => CopyAsync(account.LoginName));
        copyAccount.Padding = new Thickness(6, 0, 6, 0); copyAccount.IsEnabled = !privacy;
        Grid.SetColumn(copyAccount, 3); infoGrid.Children.Add(copyAccount);
        var passLabel = Label("登录密码", compact ? 10 : 11, false, mutedBrush); Grid.SetRow(passLabel, 1); infoGrid.Children.Add(passLabel);
        password = Label(PasswordText(), compact ? 12 : 13); Grid.SetRow(password, 1); Grid.SetColumn(password, 1); infoGrid.Children.Add(password);
        revealButton = IconButton("reveal", "\uE890", item.PasswordRevealed ? "隐藏密码" : "显示密码", () => { TogglePassword(); return Task.CompletedTask; });
        revealButton.IsEnabled = item.HasPassword && !privacy;
        revealButton.Background = Solid(0x00000000); revealButton.BorderThickness = new Thickness(0);
        Grid.SetRow(revealButton, 1); Grid.SetColumn(revealButton, 2); infoGrid.Children.Add(revealButton);
        var copyPassword = ActionButton("copy-password", "复制", "复制登录密码", () => CopyAsync(account.Password));
        copyPassword.Padding = new Thickness(6, 0, 6, 0);
        copyPassword.IsEnabled = item.HasPassword && !privacy; Grid.SetRow(copyPassword, 1); Grid.SetColumn(copyPassword, 3); infoGrid.Children.Add(copyPassword);
        information.Child = infoGrid;

        var statusGrid = new Grid { ColumnSpacing = 6 };
        for (int i = 0; i < 3; i++) statusGrid.ColumnDefinitions.Add(new ColumnDefinition());
        var levelTile = StatusTile("CS2 等级", history?.CsPlayerLevel is { } level ? "Lv. " + level : "未知", "\uE7FC");
        ToolTipService.SetToolTip(levelTile, history?.PremierScore is { } score ? "优先分：" + score : "优先分未获取"); statusGrid.Children.Add(levelTile);
        var primeTile = StatusTile("优先状态", "未知", "\uE83D"); Grid.SetColumn(primeTile, 1); statusGrid.Children.Add(primeTile);
        if (!compact)
        {
            var cooldownTile = StatusTile("冷却状态", CreateCooldownStatus(), "\uE823");
            Grid.SetColumn(cooldownTile, 2); statusGrid.Children.Add(cooldownTile);
        }

        var actions = new Grid { ColumnSpacing = 8, Height = compact ? 32 : 36 };
        actions.ColumnDefinitions.Add(new ColumnDefinition()); actions.ColumnDefinitions.Add(new ColumnDefinition());
        var login = ActionButton("login", ButtonLabel("\uE768", "一键登录"), "一键登录", () => owner.LoginAsync(Account, key));
        login.Style = (Style)Application.Current.Resources["PclPrimaryButtonStyle"];
        login.IsEnabled = key is not null && !AppState.IsBusy; login.HorizontalAlignment = HorizontalAlignment.Stretch;
        ToolTipService.SetToolTip(login, key is null ? "请先在原登录页添加凭证并关联账号" : "使用已关联的原有登录凭证"); actions.Children.Add(login);
        var details = ActionButton("details", ButtonLabel("\uE8A5", "查看详情"), "查看详情", () => owner.DetailsAsync(Account, key));
        details.Style = (Style)Application.Current.Resources["PclSecondaryButtonStyle"];
        details.HorizontalAlignment = HorizontalAlignment.Stretch; Grid.SetColumn(details, 1); actions.Children.Add(details);

        var body = new StackPanel { Spacing = 8, Padding = new Thickness(compact ? 12 : 14) };
        body.Children.Add(profile);
        body.Children.Add(information);
        if (compact)
        {
            body.Children.Add(CreateCooldownStatus(
                (history?.CsPlayerLevel is { } lv ? "CS2 Lv. " + lv : "CS2 等级未知") + " · 优先状态未知 · "));
        }
        else
        {
            body.Children.Add(statusGrid);
        }
        body.Children.Add(actions);
        layout.Children.Add(body);

        Content = root;
        Width = item.CardWidth > 0 ? item.CardWidth : double.NaN;
        selected = item.IsSelected;
        UpdateEmphasis();
        ToolTipService.SetToolTip(this, "双击编辑账号信息；右键打开账号菜单");
        _ = ApplyBackgroundAsync();
    }

    internal bool IsCardDoubleTapSource(object source)
    {
        if (Item is null || owner.IsPrivate) return false;
        for (var element = source as DependencyObject; element is not null; element = VisualTreeHelper.GetParent(element))
        {
            if (element is ButtonBase || element is TextBox || element is PasswordBox) return false;
            if (element == this) return true;
        }
        return false;
    }

    internal Task OpenEditorAsync() => Item is null
        ? Task.CompletedTask
        : owner.SafeAsync(() => owner.EditAsync(Account));

    private static SolidColorBrush Solid(uint argb) => new(Color.FromArgb(
        (byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));

    // 供 AccountCardStatus.cs 复用的调色板/工厂。
    private static SolidColorBrush Brush(byte a, byte r, byte g, byte b) => new(Color.FromArgb(a, r, g, b));
    private Brush Muted => mutedBrush;
    private Brush White => Solid(0xFFFFFFFF);
    private Brush PanelBrush => altBrush;

    private FontIcon Icon(string glyph, double size, Brush foreground) => new()
    {
        Glyph = glyph, FontSize = size, Foreground = foreground, IsHitTestVisible = false,
        FontFamily = new FontFamily(glyph == "\u2744" ? "Segoe UI Symbol" : "Segoe MDL2 Assets"),
    };

    private TextBlock Label(string text, double size, bool bold = false, Brush? foreground = null) => new()
    {
        Text = text, FontSize = size,
        FontWeight = bold ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
        Foreground = foreground ?? textBrush, TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private StackPanel ButtonLabel(string glyph, string text)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        panel.Children.Add(Icon(glyph, 13, Solid(0xFFFFFFFF)));
        panel.Children.Add(new TextBlock
        {
            Text = text, FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center, Foreground = Solid(0xFFFFFFFF),
        });
        return panel;
    }

    private Border StatusTile(string title, string value, string glyph) => StatusTile(title, Label(value, 12, true), glyph);

    private Border StatusTile(string title, UIElement value, string glyph)
    {
        var row = new Grid { ColumnSpacing = 6 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.Children.Add(new Border
        {
            Width = 22, Height = 22, CornerRadius = new CornerRadius(6), Background = accentSoftBrush,
            Child = Icon(glyph, 13, accentBrush), VerticalAlignment = VerticalAlignment.Center,
        });
        var texts = new StackPanel { Spacing = 0, VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(Label(title, 10, false, mutedBrush));
        texts.Children.Add(value);
        Grid.SetColumn(texts, 1); row.Children.Add(texts);
        return new Border
        {
            Padding = new Thickness(7, 4, 6, 4), CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1), BorderBrush = borderBrush, Background = altBrush, Child = row,
        };
    }

    private string PasswordText()
    {
        var item = Item;
        if (item is null) return "";
        if (item.IsPrivate) return "密码 ••••••";
        if (!item.HasPassword) return "未保存密码";
        return item.PasswordRevealed ? item.PasswordPlain ?? "" : item.PasswordMasked;
    }

    internal void TogglePassword()
    {
        var item = Item;
        if (item is null || item.IsPrivate || !item.HasPassword) return;
        item.PasswordRevealed = !item.PasswordRevealed;
        RefreshPassword();
    }

    /// <summary>按条目状态重写密码文本与揭示按钮的提示（不重建控件）。</summary>
    private void RefreshPassword()
    {
        if (Item is not { } item) return;
        password.Text = PasswordText();
        AutomationProperties.SetName(revealButton, item.PasswordRevealed ? "隐藏密码" : "显示密码");
        ToolTipService.SetToolTip(revealButton, item.PasswordRevealed ? "隐藏密码" : "显示密码");
    }

    internal string VisiblePassword => password.Text;
    internal void SetSelected(bool value) { selected = value; UpdateEmphasis(); }
    internal void SetHovered(bool value) { hovered = value; UpdateEmphasis(); }
    private void UpdateEmphasis()
    {
        if (surface is null) return;
        emphasis.Opacity = selected ? 1 : hovered ? 0.6 : 0;
        selectedBadge.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
        surface.Background = hovered && !selected ? (dark ? Solid(0xFF2B2D31) : Solid(0xFFFAFBFC)) : surfaceBrush;
    }

    internal bool IsSelectedCard => selected;
    internal double EmphasisOpacity => emphasis.Opacity;
    internal CornerRadius OutlineRadius => emphasis.CornerRadius;
    internal Grid HeaderArtwork => artwork;

    private Button ActionButton(string name, object content, string accessibleName, Func<Task> action)
    {
        var button = new Button
        {
            Content = content, CornerRadius = new CornerRadius(7), Padding = new Thickness(8, 2, 8, 2), FontSize = 12,
            Foreground = mutedBrush, Background = Solid(0x00000000), BorderThickness = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        AutomationProperties.SetName(button, accessibleName); ToolTipService.SetToolTip(button, accessibleName);
        button.Click += async (_, _) => await owner.SafeAsync(action); Actions[name] = button; return button;
    }

    private Button IconButton(string name, string glyph, string accessibleName, Func<Task> action) => ActionButton(name, Icon(glyph, 12, mutedBrush), accessibleName, action);

    private Task CopyAsync(string text)
    {
        if (owner.IsPrivate) return Task.CompletedTask;
        ClipboardService.Copy(text); AppState.ShowStatus("已复制。", InfoBarSeverity.Success); return Task.CompletedTask;
    }

    /// <summary>
    /// 构建这张卡片的右键菜单（14 项，保持原有文案与顺序）。菜单不再挂在卡片上：
    /// 由页面在 ContextRequested 时按被点中的条目即时创建，避免 100 张卡片各持一个 MenuFlyout。
    /// </summary>
    internal MenuFlyout CreateMenu()
    {
        if (Item is not { } item) return new MenuFlyout();
        var account = item.Account;
        var steamId = item.History?.SteamId ?? item.Owner.Snapshot.CachedFor(key)?.SteamId ?? account.SteamId;
        var menu = new MenuFlyout();
        void Add(string title, Func<Task> action, bool enabled = true)
        { var entry = new MenuFlyoutItem { Text = title, IsEnabled = enabled }; entry.Click += async (_, _) => await owner.SafeAsync(action); menu.Items.Add(entry); }
        Add("复制登录账号", () => CopyAsync(account.LoginName), !owner.IsPrivate);
        Add("复制登录密码", () => CopyAsync(account.Password), !owner.IsPrivate && account.Password.Length > 0);
        Add("复制 SteamID", () => CopyAsync(steamId), !owner.IsPrivate && steamId.Length > 0);
        menu.Items.Add(new MenuFlyoutSeparator());
        Add("一键登录", () => owner.LoginAsync(account, key), key is not null && !AppState.IsBusy);
        Add("查询 CS2 状态", () => owner.QueryAsync(key), ManagedAccounts.History(key) is not null && !AppState.IsBusy);
        Add("查看账号详情", () => owner.DetailsAsync(account, key));
        menu.Items.Add(new MenuFlyoutSeparator());
        Add("编辑账号信息", () => owner.EditAsync(account), !owner.IsPrivate);
        Add("修改账号密码", () => owner.EditAsync(account), !owner.IsPrivate);
        Add("设置账号背景", () => owner.BackgroundAsync(account), !owner.IsPrivate);
        Add("设置账号分组", () => owner.MoveGroupAsync(account));
        Add(account.Favorite ? "从收藏中移除" : "加入收藏", () => owner.ChangeAsync(account, x => x.Favorite = !x.Favorite));
        Add("手动关联原账号", () => owner.LinkAsync(account), !owner.IsPrivate);
        menu.Items.Add(new MenuFlyoutSeparator());
        Add("转到黑号管理", () => owner.MoveToBlackAsync(account), !owner.IsPrivate);
        Add("删除账号", () => owner.DeleteAsync(account)); return menu;
    }

    private async Task ApplyBackgroundAsync()
    {
        if (Item is not { } item) return;
        var generation = buildGeneration;
        try
        {
            if (owner.IsPrivate) return;
            var steamId = item.History?.SteamId ?? owner.Snapshot.CachedFor(key)?.SteamId ?? item.Account.SteamId;
            var avatarPath = item.History?.AvatarPath ?? owner.Snapshot.CachedFor(key)?.AvatarPath;
            var avatarUrl = item.History?.AvatarUrl ?? owner.Snapshot.CachedFor(key)?.AvatarUrl;
            var palette = await CardBackgroundService.GetAsync(item.Account, avatarPath, avatarUrl, steamId,
                owner.Preferences, dark, backgroundCancellation.Token);
            if (stopped || generation != buildGeneration)
            {
#if MANAGER_UI_CHECKS
                // 容器被复用/回收后，旧条目的取图结果必须丢掉；自检据此证明"过期异步不上屏"。
                if (generation != buildGeneration) Services.UiCheckPerf.StaleBackgroundDropped();
#endif
                return;
            }
            var hasImage = palette.ImagePath is not null;
            artwork.Background = CardBackgroundService.Brush(palette);
            artwork.Opacity = hasImage ? 1 : dark ? 0.55 : 0.34;
            scrim.Background = hasImage
                ? Solid(dark ? 0xA62A2C31 : 0xA6FFFFFF)
                : Solid(0x00000000);
        }
        catch { } // 取消或不可用图片时保留主题渐变底
    }
}
