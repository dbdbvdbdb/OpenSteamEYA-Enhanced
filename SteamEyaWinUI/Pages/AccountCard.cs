using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using OpenSteamEya.AccountManagement;
using SteamEyaWinUI.Services;
using Windows.UI;

namespace SteamEyaWinUI.Pages;

internal sealed partial class AccountCard : UserControl
{
    internal const double CardRadius = 14;
    public ManagedAccount Account { get; }
    private readonly AccountManagerPage owner;
    private readonly string? key;
    private readonly Border surface;
    private readonly Border emphasis;
    private readonly Border selectedBadge;
    private readonly Grid artwork;
    private readonly TextBlock password;
    private readonly Button revealButton;
    private readonly bool compact;
    private bool stopped, revealed, selected, hovered;
    private readonly CancellationTokenSource backgroundCancellation = new();
    internal readonly Dictionary<string, Button> Actions = [];

    public void Stop() { stopped = true; backgroundCancellation.Cancel(); cooldownTimer?.Stop(); }

    public AccountCard(AccountManagerPage page, ManagedAccount account, string? mappingKey, bool compact)
    {
        Account = account; owner = page; key = mappingKey; this.compact = compact;
        RequestedTheme = ElementTheme.Dark;
        bool privacy = owner.IsPrivate;
        var history = ManagedAccounts.History(key); var cached = ManagedAccounts.Cached(key);
        string steamId = history?.SteamId ?? cached?.SteamId ?? account.SteamId;
        string? avatarPath = history?.AvatarPath ?? cached?.AvatarPath;
        string? avatarUrl = history?.AvatarUrl ?? cached?.AvatarUrl;
#if MANAGER_UI_CHECKS
        if (ManagerUiChecks.Enabled) history = ManagerUiChecks.StatusFixtures.GetValueOrDefault(account.Id) ?? history;
        if (ManagerUiChecks.Enabled && !account.LoginName.Contains("missing-avatar")) avatarPath = ManagerUiChecks.AvatarPath;
#endif
        revealed = !owner.Preferences.HidePasswords && !privacy;
        var root = new Grid { UseLayoutRounding = true };
        var layout = new Grid { CornerRadius = new CornerRadius(CardRadius - 1) };
        surface = new Border { CornerRadius = new CornerRadius(CardRadius), BorderThickness = new Thickness(1),
            BorderBrush = Brush(70, 113, 132, 157), Background = PanelBrush, Child = layout,
            Translation = new System.Numerics.Vector3(0, 0, 8), Shadow = new ThemeShadow() };
        root.Children.Add(surface);
        emphasis = new Border { CornerRadius = new CornerRadius(CardRadius), BorderThickness = new Thickness(2),
            BorderBrush = Brush(255, 74, 151, 255), Opacity = 0, IsHitTestVisible = false,
            OpacityTransition = new ScalarTransition { Duration = TimeSpan.FromMilliseconds(140) } };
        root.Children.Add(emphasis);
        selectedBadge = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(11),
            Background = Brush(255, 42, 121, 255), Child = Icon("\uE73E", 12, White),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(10), IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        root.Children.Add(selectedBadge); Content = root;

        // Artwork is a background brush on the rounded header, never a rectangular overlay on the item.
        artwork = new Grid { Height = compact ? double.NaN : 138,
            CornerRadius = compact ? new CornerRadius(CardRadius - 1) : new CornerRadius(CardRadius - 1, CardRadius - 1, 0, 0),
            Background = CardBackgroundService.Brush(new(Color.FromArgb(255, 35, 56, 78), Color.FromArgb(255, 70, 54, 86), Color.FromArgb(255, 36, 71, 87), null)) };
        artwork.Children.Add(new Border { CornerRadius = artwork.CornerRadius, Background = new LinearGradientBrush
        {
            StartPoint = new(0, 0), EndPoint = new(0, 1), GradientStops = {
                new() { Offset = 0, Color = Color.FromArgb((byte)Math.Clamp((1 - owner.Preferences.Brightness) * 110, 20, 170), 8, 15, 26) },
                new() { Offset = 0.5, Color = Color.FromArgb(compact ? (byte)200 : (byte)90, 10, 18, 30) },
                new() { Offset = 1, Color = Color.FromArgb(255, 12, 19, 29) }
            }
        } });
        layout.Children.Add(artwork);

        var profile = new Grid { ColumnSpacing = 16, Margin = new Thickness(compact ? 16 : 24, 0, 24, compact ? 0 : 14),
            VerticalAlignment = VerticalAlignment.Bottom };
        profile.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        profile.ColumnDefinitions.Add(new ColumnDefinition());
        var avatar = new Border { Width = compact ? 46 : 92, Height = compact ? 46 : 92,
            CornerRadius = new CornerRadius(48), Padding = new Thickness(3), BorderThickness = new Thickness(2),
            BorderBrush = Brush(255, 114, 188, 244), Background = Brush(255, 23, 36, 57) };
        var avatarContent = new Grid { CornerRadius = new CornerRadius(44), Background = Brush(255, 35, 50, 72) };
        var avatarPlaceholder = Icon("\uE77B", compact ? 22 : 32, Muted);
        avatarContent.Children.Add(avatarPlaceholder);
        if (!privacy)
        {
            var uri = avatarPath is not null && System.IO.File.Exists(avatarPath) ? avatarPath : avatarUrl;
            if (Uri.TryCreate(uri, UriKind.Absolute, out var imageUri))
            {
                var bitmap = new BitmapImage(imageUri) { DecodePixelWidth = 160 };
                bitmap.ImageOpened += (_, _) => avatarPlaceholder.Visibility = Visibility.Collapsed;
                bitmap.ImageFailed += (_, _) => avatarPlaceholder.Visibility = Visibility.Visible;
                avatarContent.Background = new ImageBrush { ImageSource = bitmap, Stretch = Stretch.UniformToFill };
            }
        }
        avatar.Child = avatarContent; profile.Children.Add(avatar);
        var identity = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        var nickname = Label(privacy ? "隐私账号" : owner.DisplayCardName(account), compact ? 17 : 22, true);
        ToolTipService.SetToolTip(nickname, privacy ? "隐私账号" : owner.DisplayCardName(account)); identity.Children.Add(nickname);
        if (!compact)
        {
            var presence = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            presence.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse { Width = 7, Height = 7, Fill = Muted, VerticalAlignment = VerticalAlignment.Center });
            presence.Children.Add(Label(privacy ? "状态已隐藏" : "在线状态未知", 12, false, Muted)); identity.Children.Add(presence);
        }
        var idRow = new Grid { ColumnSpacing = 4, HorizontalAlignment = HorizontalAlignment.Left, MaxWidth = 252 }; idRow.ColumnDefinitions.Add(new ColumnDefinition());
        idRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        string idText = steamId.Length > 0 ? steamId : "未获取";
        if (!owner.Preferences.FullSteamId && steamId.Length > 8) idText = steamId[..4] + "…" + steamId[^4..];
        idRow.Children.Add(Label(privacy ? "身份信息已隐藏" : "SteamID: " + idText, compact ? 10 : 12, false, Muted));
        var copyId = IconButton("copy-id", "\uE8C8", "复制 SteamID", () => CopyAsync(steamId));
        copyId.IsEnabled = !privacy && steamId.Length > 0; copyId.Width = 24; copyId.Height = 22; copyId.Padding = new Thickness(2);
        copyId.Background = Brush(0, 0, 0, 0); copyId.BorderThickness = new Thickness(0);
        Grid.SetColumn(copyId, 1); idRow.Children.Add(copyId); identity.Children.Add(idRow);
        Grid.SetColumn(identity, 1); profile.Children.Add(identity);

        var information = new Grid { RowSpacing = 2, ColumnSpacing = 8, Padding = new Thickness(12, 6, 10, 6),
            CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), BorderBrush = Brush(85, 76, 96, 119),
            Background = Brush(220, 25, 35, 48), Height = compact ? 64 : 76 };
        information.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(compact ? 54 : 64) });
        information.ColumnDefinitions.Add(new ColumnDefinition());
        information.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        information.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        information.RowDefinitions.Add(new RowDefinition()); information.RowDefinitions.Add(new RowDefinition());
        information.Children.Add(Label("登录账号", compact ? 11 : 12, false, Muted));
        var accountText = Label(privacy ? "••••••" : account.LoginName, compact ? 12 : 13); Grid.SetColumn(accountText, 1); information.Children.Add(accountText);
        Grid.SetColumnSpan(accountText, 2);
        var copyAccount = ActionButton("copy-account", "复制", "复制登录账号", () => CopyAsync(account.LoginName));
        copyAccount.Width = 54; copyAccount.Height = compact ? 24 : 28; copyAccount.Padding = new Thickness(4, 0, 4, 0); copyAccount.IsEnabled = !privacy;
        Grid.SetColumn(copyAccount, 3); information.Children.Add(copyAccount);
        var passLabel = Label("登录密码", compact ? 11 : 12, false, Muted); Grid.SetRow(passLabel, 1); information.Children.Add(passLabel);
        password = Label(PasswordText(), compact ? 12 : 13); Grid.SetRow(password, 1); Grid.SetColumn(password, 1); information.Children.Add(password);
        revealButton = IconButton("reveal", "\uE890", revealed ? "隐藏密码" : "显示密码", () => { TogglePassword(); return Task.CompletedTask; });
        revealButton.IsEnabled = account.Password.Length > 0 && !privacy; revealButton.Width = 28; revealButton.Height = compact ? 24 : 28;
        revealButton.Padding = new Thickness(3); revealButton.Background = Brush(0, 0, 0, 0); revealButton.BorderThickness = new Thickness(0);
        Grid.SetRow(revealButton, 1); Grid.SetColumn(revealButton, 2); information.Children.Add(revealButton);
        var copyPassword = ActionButton("copy-password", "复制", "复制登录密码", () => CopyAsync(account.Password));
        copyPassword.Width = 54; copyPassword.Height = compact ? 24 : 28; copyPassword.Padding = new Thickness(4, 0, 4, 0);
        copyPassword.IsEnabled = account.Password.Length > 0 && !privacy; Grid.SetRow(copyPassword, 1); Grid.SetColumn(copyPassword, 3); information.Children.Add(copyPassword);

        var statusGrid = new Grid { ColumnSpacing = 5, Height = 64 };
        for (int i = 0; i < 3; i++) statusGrid.ColumnDefinitions.Add(new ColumnDefinition());
        var levelTile = StatusTile("CS2 等级", history?.CsPlayerLevel is { } level ? "Lv. " + level : "未知", "\uE7FC", Brush(255, 235, 167, 72), White);
        ToolTipService.SetToolTip(levelTile, history?.PremierScore is { } score ? "优先分：" + score : "优先分未获取"); statusGrid.Children.Add(levelTile);
        // No reliable Prime or presence ownership field exists in the upstream snapshot.
        var primeTile = StatusTile("优先状态", "未知", "\uE83D", Brush(255, 205, 167, 93), Muted); Grid.SetColumn(primeTile, 1); statusGrid.Children.Add(primeTile);
        if (!compact)
        {
            var cooldownTile = StatusTile("冷却状态", CreateCooldownStatus(history), "\uE823", Brush(255, 122, 185, 234));
            Grid.SetColumn(cooldownTile, 2); statusGrid.Children.Add(cooldownTile);
        }

        var actions = new Grid { ColumnSpacing = 8, Height = compact ? 36 : 44 };
        actions.ColumnDefinitions.Add(new ColumnDefinition()); actions.ColumnDefinitions.Add(new ColumnDefinition());
        var login = ActionButton("login", ButtonLabel("\uE768", "一键登录"), "一键登录", () => owner.LoginAsync(Account, key));
        login.Style = (Style)owner.Resources["AccountCardLoginButtonStyle"];
        login.Background = Brush(255, 40, 118, 255); login.BorderBrush = Brush(255, 76, 145, 255);
        login.IsEnabled = key is not null && !AppState.IsBusy; login.HorizontalAlignment = HorizontalAlignment.Stretch;
        ToolTipService.SetToolTip(login, key is null ? "请先在原登录页添加凭证并关联账号" : "使用已关联的原有登录凭证"); actions.Children.Add(login);
        var details = ActionButton("details", ButtonLabel("\uE8A5", "查看详情"), "查看详情", () => owner.DetailsAsync(Account, key));
        details.HorizontalAlignment = HorizontalAlignment.Stretch; Grid.SetColumn(details, 1); actions.Children.Add(details);

        if (compact)
        {
            var compactContent = new Grid { Padding = new Thickness(14), ColumnSpacing = 18, RowSpacing = 8, MinHeight = 132 };
            compactContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            compactContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
            compactContent.RowDefinitions.Add(new RowDefinition()); compactContent.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            profile.Margin = new Thickness(0); profile.VerticalAlignment = VerticalAlignment.Center; compactContent.Children.Add(profile);
            Grid.SetColumn(information, 1); compactContent.Children.Add(information);
            var compactStatus = CreateCooldownStatus(history, (history?.CsPlayerLevel is { } lv ? "CS2 Lv. " + lv : "CS2 等级未知") + " · 优先状态未知 · ");
            Grid.SetRow(compactStatus, 1); compactContent.Children.Add(compactStatus);
            Grid.SetColumn(actions, 1); Grid.SetRow(actions, 1); compactContent.Children.Add(actions); layout.Children.Add(compactContent);
        }
        else
        {
            artwork.Children.Add(profile);
            var metadata = Label(privacy ? "分组已隐藏" : (account.Favorite ? "★ " : "") + owner.GroupName(account), 10, false, Muted);
            metadata.MaxWidth = 145; ToolTipService.SetToolTip(metadata, privacy ? "信息已隐藏" : owner.GroupName(account) + (account.Note.Length > 0 ? " · " + account.Note : ""));
            var pill = new Border { Child = metadata, CornerRadius = new CornerRadius(6), Background = Brush(155, 17, 28, 44),
                Padding = new Thickness(7, 3, 7, 3), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(18, 12, 42, 0) };
            artwork.Children.Add(pill);
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var body = new StackPanel { Spacing = 8, Padding = new Thickness(18, 0, 18, 18) };
            body.Children.Add(information); body.Children.Add(statusGrid); body.Children.Add(actions); Grid.SetRow(body, 1); layout.Children.Add(body);
            SizeChanged += (_, _) => artwork.Height = Math.Clamp(ActualWidth * 0.28, 120, 190);
        }
        ContextFlyout = Menu(steamId);
        PointerEntered += (_, _) => SetHovered(true);
        PointerExited += (_, _) => SetHovered(false);
        ToolTipService.SetToolTip(this, "双击编辑账号信息；右键打开账号菜单");
        DoubleTapped += async (_, args) =>
        {
            if (!IsCardDoubleTapSource(args.OriginalSource)) return;
            args.Handled = true; await OpenEditorAsync();
        };
        _ = ApplyBackgroundAsync(avatarPath, avatarUrl, steamId);
    }
    internal bool IsCardDoubleTapSource(object source)
    {
        if (owner.IsPrivate) return false;
        for (var element = source as DependencyObject; element is not null; element = VisualTreeHelper.GetParent(element))
        {
            if (element is ButtonBase || element is TextBox || element is PasswordBox) return false;
            if (element == this) return true;
        }
        return false;
    }
    internal Task OpenEditorAsync() => owner.SafeAsync(() => owner.EditAsync(Account));

    private static SolidColorBrush Brush(byte alpha, byte red, byte green, byte blue) => new(Color.FromArgb(alpha, red, green, blue));
    private static SolidColorBrush White => Brush(255, 239, 245, 255);
    private static SolidColorBrush Muted => Brush(255, 161, 178, 199);
    private static SolidColorBrush PanelBrush => Brush(255, 12, 19, 29);
    private static FontIcon Icon(string glyph, double size, Brush foreground) => new() { Glyph = glyph, FontSize = size, Foreground = foreground, IsHitTestVisible = false,
        FontFamily = new FontFamily(glyph == "\u2744" ? "Segoe UI Symbol" : "Segoe MDL2 Assets") };
    private static TextBlock Label(string text, double size, bool bold = false, Brush? foreground = null) => new()
    { Text = text, FontSize = size, FontWeight = bold ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
        Foreground = foreground ?? White, TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1, VerticalAlignment = VerticalAlignment.Center };
    private static StackPanel ButtonLabel(string glyph, string text)
    { var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 }; panel.Children.Add(Icon(glyph, 14, White)); panel.Children.Add(Label(text, 13, true)); return panel; }
    private static Border StatusTile(string title, string value, string glyph, Brush iconColor, Brush valueColor)
        => StatusTile(title, Label(value, 14, true, valueColor), glyph, iconColor);
    private static Border StatusTile(string title, UIElement value, string glyph, Brush iconColor)
    {
        var row = new Grid { ColumnSpacing = 8 }; row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new ColumnDefinition());
        row.Children.Add(new Border { Width = 32, Height = 36, CornerRadius = new CornerRadius(7), Background = Brush(255, 32, 48, 67), Child = Icon(glyph, 24, iconColor) });
        var texts = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center }; texts.Children.Add(Label(title, 12, false, Muted)); texts.Children.Add(value);
        Grid.SetColumn(texts, 1); row.Children.Add(texts);
        return new Border { Padding = new Thickness(8, 5, 6, 5), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1),
            BorderBrush = Brush(60, 83, 103, 126), Background = Brush(245, 25, 35, 48), Child = row };
    }
    private string PasswordText() => owner.IsPrivate ? "密码 ••••••" : Account.Password.Length == 0 ? "未保存密码" : revealed ? Account.Password : "••••••••";
    internal void TogglePassword()
    {
        if (owner.IsPrivate || Account.Password.Length == 0) return;
        revealed = !revealed; password.Text = PasswordText();
        AutomationProperties.SetName(revealButton, revealed ? "隐藏密码" : "显示密码"); ToolTipService.SetToolTip(revealButton, revealed ? "隐藏密码" : "显示密码");
    }
    internal string VisiblePassword => password.Text;
    internal void SetSelected(bool value) { selected = value; UpdateEmphasis(); }
    internal void SetHovered(bool value) { hovered = value; UpdateEmphasis(); }
    private void UpdateEmphasis()
    {
        emphasis.Opacity = selected ? 1 : hovered ? 0.6 : 0;
        selectedBadge.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
    }
    internal bool IsSelectedCard => selected;
    internal double EmphasisOpacity => emphasis.Opacity;
    internal CornerRadius OutlineRadius => emphasis.CornerRadius;
    internal Grid HeaderArtwork => artwork;
    private Button ActionButton(string name, object content, string accessibleName, Func<Task> action)
    {
        var button = new Button { Content = content, CornerRadius = new CornerRadius(7), Padding = new Thickness(10, 4, 10, 4), FontSize = 12,
            Foreground = White, Background = Brush(255, 35, 47, 63), BorderBrush = Brush(110, 86, 105, 127), BorderThickness = new Thickness(1),
            HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, VerticalAlignment = VerticalAlignment.Stretch };
        AutomationProperties.SetName(button, accessibleName); ToolTipService.SetToolTip(button, accessibleName);
        button.Click += async (_, _) => await owner.SafeAsync(action); Actions[name] = button; return button;
    }
    private Button IconButton(string name, string glyph, string accessibleName, Func<Task> action) => ActionButton(name, Icon(glyph, 14, Muted), accessibleName, action);
    private Task CopyAsync(string text)
    {
        if (owner.IsPrivate) return Task.CompletedTask;
        ClipboardService.Copy(text); AppState.ShowStatus("已复制。", InfoBarSeverity.Success); return Task.CompletedTask;
    }
    private MenuFlyout Menu(string steamId)
    {
        var menu = new MenuFlyout();
        void Add(string title, Func<Task> action, bool enabled = true)
        { var item = new MenuFlyoutItem { Text = title, IsEnabled = enabled }; item.Click += async (_, _) => await owner.SafeAsync(action); menu.Items.Add(item); }
        Add("复制登录账号", () => CopyAsync(Account.LoginName), !owner.IsPrivate);
        Add("复制登录密码", () => CopyAsync(Account.Password), !owner.IsPrivate && Account.Password.Length > 0);
        Add("复制 SteamID", () => CopyAsync(steamId), !owner.IsPrivate && steamId.Length > 0);
        menu.Items.Add(new MenuFlyoutSeparator());
        Add("一键登录", () => owner.LoginAsync(Account, key), key is not null && !AppState.IsBusy);
        Add("查询 CS2 状态", () => owner.QueryAsync(key), ManagedAccounts.History(key) is not null && !AppState.IsBusy);
        Add("查看账号详情", () => owner.DetailsAsync(Account, key));
        menu.Items.Add(new MenuFlyoutSeparator());
        Add("编辑账号信息", () => owner.EditAsync(Account), !owner.IsPrivate);
        Add("修改账号密码", () => owner.EditAsync(Account), !owner.IsPrivate);
        Add("设置账号背景", () => owner.BackgroundAsync(Account), !owner.IsPrivate);
        Add("设置账号分组", () => owner.MoveGroupAsync(Account));
        Add(Account.Favorite ? "从收藏中移除" : "加入收藏", () => owner.ChangeAsync(Account, x => x.Favorite = !x.Favorite));
        Add("手动关联原账号", () => owner.LinkAsync(Account), !owner.IsPrivate);
        menu.Items.Add(new MenuFlyoutSeparator());
        Add("删除密码管理记录", () => owner.DeleteAsync(Account)); return menu;
    }
    private async Task ApplyBackgroundAsync(string? avatarPath, string? avatarUrl, string steamId)
    {
        try
        {
            if (owner.IsPrivate) return;
            var palette = await CardBackgroundService.GetAsync(Account, avatarPath, avatarUrl, steamId, owner.Preferences, owner.ActualTheme != ElementTheme.Light, backgroundCancellation.Token);
            if (stopped) return;
            var brush = CardBackgroundService.Brush(palette);
            if (brush is ImageBrush image) image.Stretch = Account.Background.Stretch switch { "Uniform" => Stretch.Uniform, "Fill" => Stretch.Fill, _ => Stretch.UniformToFill };
            artwork.Background = brush;
        }
        catch { } // cancelled/unusable images keep the valid rounded gradient fallback
    }
}
