using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using SteamEyaWinUI.Localization;
using SteamEyaWinUI.Models;
using SteamEyaWinUI.Pages;
using SteamEyaWinUI.Services;
using Windows.Graphics;

namespace SteamEyaWinUI;

public sealed partial class MainWindow : Window
{
    private const int InitialWindowWidth = 1280;
    private const int InitialWindowHeight = 860;
    private const int MinWindowWidth = 1180;
    private const int MinWindowHeight = 780;
    private const uint WmGetMinMaxInfo = 0x0024;
    private const nuint WindowSubclassId = 1;

    private static nint s_hwnd;

    // Informational/Success 状态若干秒后自动收起；Warning/Error 常驻，直到被替换或用户手动关闭。
    private static readonly TimeSpan StatusAutoDismissDelay = TimeSpan.FromSeconds(6);
    private readonly DispatcherQueueTimer _statusDismissTimer;

    public static MainWindow? Instance { get; private set; }

    /// <summary>主窗口句柄，供文件/目录选择器等 WinRT 互操作（InitializeWithWindow）使用；在 ConfigureWindowSize 中赋值。</summary>
    public static nint Hwnd => s_hwnd;

    public MainWindow()
    {
        Instance = this;

        InitializeComponent();
        SystemBackdrop = new MicaBackdrop();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        SetWindowIcon();
        TitleVersionText.Text = $"v{GitHubUpdateService.CurrentVersion}";

        var startupSettings = AppState.SettingsService.Load();
        ThemeService.LoadFromSettings();
        ApplyTheme(ParseTheme(startupSettings.Theme));
        RefreshThemePalette();
        RefreshNavText();
        Loc.LanguageChanged += RefreshNavText;

        _statusDismissTimer = DispatcherQueue.CreateTimer();
        _statusDismissTimer.Interval = StatusAutoDismissDelay;
        _statusDismissTimer.IsRepeating = false;
        _statusDismissTimer.Tick += (_, _) => StatusInfoBar.IsOpen = false;

        AppState.StatusReporter = ShowStatus;
        AppState.BusyChanged += OnBusyChanged;
        AppState.UpdateStateChanged += RefreshUpdateBadge;
        RefreshUpdateBadge();

        ConfigureWindowSize();
        Activated += OnStartupActivated;

#if PERF_HARNESS
        if (PerfHarness.Enabled)
        {
            ApplyTheme(ElementTheme.Dark);
            _ = PerfHarness.RunAsync(this);
            return;
        }
#endif

#if MANAGER_UI_CHECKS
        if (ManagerUiChecks.Enabled)
        {
            ApplyTheme(ElementTheme.Dark);
            RootNavigationView.SelectedItem = ManagerNavItem;
            RootNavigationView.Loaded += async (_, _) =>
            {
                if (ContentFrame.Content is AccountManagerPage page)
                    await ManagerUiChecks.RunAsync(this, page);
            };
            return;
        }
#endif

        // 预载历史账号，登录页的头像/资料复用依赖该缓存。
        AppState.ReloadHistory();
        // 只选一次：启动时反复改写 SelectedItem 会让顶部指示条停在上一个项目的位置。
        RootNavigationView.SelectedItem = ManagerNavItem;

        // 首次启动即解析并持久化 Steam 安装路径（之后上号直接复用，不再每次探测）。
        // 等内容进入可视树（XamlRoot 就绪）后再跑，检测失败才需要弹框。
        RootNavigationView.Loaded += OnRootNavigationViewLoaded;

        _ = AppState.CheckForUpdatesAsync(isAutomatic: true);
    }

    // 启动时解析 Steam 路径：成功则静默持久化；自动检测失败时弹框让用户手动选择含 steam.exe 的目录。
    private async void OnRootNavigationViewLoaded(object sender, RoutedEventArgs e)
    {
        RootNavigationView.Loaded -= OnRootNavigationViewLoaded;
        try
        {
            await SteamPathCoordinator.EnsureResolvedAsync();
        }
        catch (Exception ex)
        {
            AppLog.Error("启动时解析 Steam 安装路径失败。", ex);
        }
    }

    public void ShowStatus(string message, InfoBarSeverity severity)
    {
        // 状态可能来自后台线程（如登录后的 CS2 云推送进度），统一封送到 UI 线程。
        if (!DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(() => ShowStatus(message, severity));
            return;
        }

        StatusInfoBar.Message = message;
        StatusInfoBar.Severity = severity;
        StatusInfoBar.IsOpen = true;

        _statusDismissTimer.Stop();
        if (severity is InfoBarSeverity.Informational or InfoBarSeverity.Success)
        {
            _statusDismissTimer.Start();
        }
    }

    /// <summary>有新版本时在「关于」导航项上亮红点，代替曾经常驻底部的更新横幅。</summary>
    private void RefreshUpdateBadge()
    {
        if (!DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(RefreshUpdateBadge);
            return;
        }

        AboutNavItem.InfoBadge = AppState.LatestUpdate?.IsUpdateAvailable == true
            ? new InfoBadge()
            : null;
    }

    /// <summary>历史页“载入到登录页”：切到登录页并填充账号。</summary>
    public void LoadAccountIntoLogin(SteamAccountHistoryItem account)
    {
        RootNavigationView.SelectedItem = LoginNavItem;
        DispatcherQueue.TryEnqueue(() => AppState.LoginPage?.LoadHistoryAccount(account));
    }

    private void RootNavigationView_SelectionChanged(
        NavigationView sender,
        NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer?.Tag is string pageName)
        {
            NavigateTo(pageName);
        }
    }

    internal void NavigateTo(string pageName)
    {
        var pageType = pageName switch
        {
            "manager" => typeof(AccountManagerPage),
            "history" or "black" => typeof(HistoryPage),
            "cachedAccounts" => typeof(CachedAccountsPage),
            "loadout" => typeof(LoadoutPage),
            "cs2LocalSettings" => typeof(Cs2HubPage),
            "personalization" => typeof(PersonalizationPage),
            "settings" => typeof(SettingsPage),
            "about" => typeof(AboutPage),
            _ => typeof(LoginPage)
        };

        if (ContentFrame.CurrentSourcePageType != pageType || pageType == typeof(HistoryPage))
        {
            ContentFrame.Navigate(pageType, pageName, new SuppressNavigationTransitionInfo());
            ContentFrame.BackStack.Clear();
        }
    }

    public void OpenAccountManagerSettings()
    {
        RootNavigationView.SelectedItem = ManagerNavItem;
        if (ContentFrame.Content is AccountManagerPage page) page.OpenManagerSettings();
    }
    public void OpenAccountManagerTool(string tool)
    {
        RootNavigationView.SelectedItem = ManagerNavItem;
        if (ContentFrame.Content is AccountManagerPage page) page.OpenTool(tool);
    }

    private void OnBusyChanged(bool isBusy)
    {
        BusyRing.IsActive = isBusy;
        BusyRing.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
    }

    internal NavigationView Nav => RootNavigationView;

    internal NavigationViewItem ManagerNav => ManagerNavItem;

    internal NavigationViewItem SettingsNav => SettingsNavItem;

    /// <summary>把主题套用到内容根（无打包下 Application.RequestedTheme 不可后置，故走根元素 RequestedTheme）。</summary>
    public void ApplyTheme(ElementTheme theme)
    {
        if (Content is FrameworkElement root)
        {
            root.RequestedTheme = theme;
        }

        ThemePaletteService.Apply();
        ApplyCaptionColors();
    }

    /// <summary>
    /// 自定义标题栏后，最小化/最大化/关闭三个按钮由系统绘制，默认会跟随窗口主题；
    /// 浅色主题下若系统仍按深色绘制，就会出现「按钮和标题栏同色」看不见的问题。
    /// 这里显式给它们上我们自己的令牌色，保证两种主题都清晰可辨。
    /// </summary>
    private void ApplyCaptionColors()
    {
        try
        {
            var titleBar = AppWindow.TitleBar;
            if (titleBar is null) return;

            var dark = (Content as FrameworkElement)?.ActualTheme == ElementTheme.Dark;
            var primary = ThemeColor("PclTextPrimaryColor", dark ? 0xFFF2F3F5 : 0xFF1F2329);
            var secondary = ThemeColor("PclTextTertiaryColor", dark ? 0xFF7C828D : 0xFF9AA1AC);
            var hover = ThemeColor("PclSurfaceAltColor", dark ? 0xFF2B2D31 : 0xFFFAFBFC);
            var pressed = ThemeColor("PclBorderStrongColor", dark ? 0xFF4A4E57 : 0xFFD3D8E0);

            titleBar.ButtonBackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0);
            titleBar.ButtonInactiveBackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0);
            titleBar.ButtonForegroundColor = primary;
            titleBar.ButtonInactiveForegroundColor = secondary;
            titleBar.ButtonHoverBackgroundColor = hover;
            titleBar.ButtonHoverForegroundColor = primary;
            titleBar.ButtonPressedBackgroundColor = pressed;
            titleBar.ButtonPressedForegroundColor = primary;
        }
        catch
        {
            // 旧系统或特殊桌面环境下 TitleBar 可能不可定制，忽略即可。
        }
    }

    /// <summary>从主题字典里取一个已解析的颜色，取不到时用回退色。</summary>
    private static Windows.UI.Color ThemeColor(string key, uint fallback)
    {
        if (Application.Current.Resources.TryGetValue(key, out var value) && value is Windows.UI.Color color) return color;
        return Windows.UI.Color.FromArgb(
            (byte)(fallback >> 24), (byte)(fallback >> 16), (byte)(fallback >> 8), (byte)fallback);
    }

    private static ElementTheme ParseTheme(string theme) => theme switch
    {
        "Light" => ElementTheme.Light,
        "Dark" => ElementTheme.Dark,
        _ => ElementTheme.Default
    };

    /// <summary>
    /// 用当前自定义主题色重算调色板并立即刷新。令牌（Color 资源）改完后，XAML 里的画刷会自动跟随，
    /// 但卡片等代码构造的画刷是快照，因此重进当前页面让它们按新颜色重建。
    /// </summary>
    public void RefreshThemePalette()
    {
        ThemePaletteService.Apply();
        ApplyCaptionColors();
        if (Content is not FrameworkElement root) return;

        // WinUI 的主题字典画刷在主题切换时才重新解析；这里先切到反色再切回，
        // 让旧样式页面（卡片、文本框等系统画刷）立即采用新令牌颜色。
        var theme = root.RequestedTheme;
        root.RequestedTheme = theme == ElementTheme.Dark ? ElementTheme.Light : ElementTheme.Dark;
        root.DispatcherQueue.TryEnqueue(() =>
        {
            if (Content is FrameworkElement current) current.RequestedTheme = theme;
            if (ContentFrame.Content is Page page && page is not Pages.ThemeEditorPage)
                ContentFrame.Navigate(page.GetType(), null, new SuppressNavigationTransitionInfo());
        });
    }

    /// <summary>供 UI 自检切换到其它页面使用。</summary>
    internal Microsoft.UI.Xaml.Controls.Frame MainFrame => ContentFrame;
    /// <summary>本地化导航项文字；语言切换时由 Loc.LanguageChanged 再次调用。</summary>
    private void RefreshNavText()
    {
        ManagerNavItem.Content = "白号管理";
        LoginNavItem.Content = Loc.T("Nav_Login");
        HistoryNavItem.Content = Loc.T("Nav_History");
        CachedAccountsNavItem.Content = Loc.T("Nav_CachedAccounts");
        SettingsNavItem.Content = Loc.T("Nav_Settings");
        AboutNavItem.Content = Loc.T("Nav_About");
    }

    private void SetWindowIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        if (File.Exists(iconPath))
        {
            AppWindow.SetIcon(iconPath);
        }
    }

    private unsafe void ConfigureWindowSize()
    {
        s_hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var scale = GetDpiForWindow(s_hwnd) / 96.0;

        AppWindow.Resize(new SizeInt32(
            (int)Math.Ceiling(InitialWindowWidth * scale),
            (int)Math.Ceiling(InitialWindowHeight * scale)));

        // 用 WM_GETMINMAXINFO 子类化实时按当前 DPI 计算最小尺寸，
        // 跨多显示器 / DPI 变化时 OverlappedPresenter.PreferredMinimum*（启动时固定的物理像素）会失效。
        SetWindowSubclass(s_hwnd, &SubclassProc, WindowSubclassId, 0);
        Closed += OnClosed;
    }

    private unsafe void OnClosed(object sender, WindowEventArgs args)
    {
        RemoveWindowSubclass(s_hwnd, &SubclassProc, WindowSubclassId);
    }

    private bool _startupUpdatePromptHandled;

    /// <summary>启动后只自动检查一次更新；有新版时弹窗，用户可选择稍后或立即下载安装。</summary>
    private async void OnStartupActivated(object sender, WindowActivatedEventArgs args)
    {
        if (_startupUpdatePromptHandled)
        {
            return;
        }

        _startupUpdatePromptHandled = true;
        Activated -= OnStartupActivated;

#if MANAGER_UI_CHECKS
        if (ManagerUiChecks.Enabled)
        {
            return;
        }
#endif

        // 等首帧渲染完成再弹窗，避免和主界面初始化抢焦点。
        await Task.Delay(1200);
        if (AppState.UpdateCheckedAt.HasValue)
        {
            return;
        }

        await AppState.CheckForUpdatesAsync(isAutomatic: true);
        var update = AppState.LatestUpdate;
        if (update is null || !update.IsUpdateAvailable)
        {
            return;
        }

        var skipped = AppState.SettingsService.Load().SkippedUpdateVersion;
        if (string.Equals(skipped, update.LatestVersion, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await ShowStartupUpdateDialogAsync(update);
    }

    private async Task ShowStartupUpdateDialogAsync(GitHubUpdateInfo update)
    {
        var dialog = BuildStartupUpdateDialog(update);
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.None)
        {
            // 用户选择“暂不更新”：记住该版本，后续启动不再为同一版本弹窗，关于页仍可更新。
            var settings = AppState.SettingsService.Load();
            settings.SkippedUpdateVersion = update.LatestVersion;
            AppState.SettingsService.Save(settings);
            return;
        }

        if (result == ContentDialogResult.Secondary)
        {
            await DownloadUpdateOnlyAsync(update);
            return;
        }

        await InstallUpdateFromStartupAsync(update);
    }

    private ContentDialog BuildStartupUpdateDialog(GitHubUpdateInfo update)
    {
        var secondary = (Brush)Application.Current.Resources["PclTextSecondaryBrush"];
        var notes = new StackPanel { Spacing = 3 };
        if (update.Changelog.Count == 0)
        {
            notes.Children.Add(new TextBlock { Text = Loc.T("UpdateDialog_Notes_Empty"), Foreground = secondary, TextWrapping = TextWrapping.Wrap });
        }

        foreach (var line in update.Changelog.Take(80))
        {
            notes.Children.Add(new TextBlock
            {
                Text = line,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Foreground = secondary
            });
        }

        var content = new StackPanel { Spacing = 8, MinWidth = 460 };
        content.Children.Add(new TextBlock { Text = Loc.Tf("UpdateDialog_Current_Format", update.CurrentVersion), Foreground = secondary });
        content.Children.Add(new TextBlock
        {
            Text = Loc.Tf("UpdateDialog_Latest_Format", update.LatestVersion),
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.Resources["PclAccentBrush"]
        });
        content.Children.Add(new TextBlock
        {
            Text = Loc.T("UpdateDialog_Notes_Header"),
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Margin = new Thickness(0, 8, 0, 0)
        });
        content.Children.Add(new ScrollViewer
        {
            MaxHeight = 240,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = notes
        });

        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = Loc.T("UpdateDialog_Title"),
            Content = content,
            PrimaryButtonText = Loc.T("UpdateDialog_Now"),
            SecondaryButtonText = Loc.T("UpdateDialog_Download"),
            CloseButtonText = Loc.T("UpdateDialog_Later"),
            DefaultButton = ContentDialogButton.Primary
        };

        return dialog;
    }

#if MANAGER_UI_CHECKS
    /// <summary>QA 专用：弹出真实的启动更新对话框，截图并校验内容后关闭。</summary>
    internal async Task CheckStartupUpdateDialogAsync(GitHubUpdateInfo update)
    {
        var dialog = BuildStartupUpdateDialog(update);
        var showing = dialog.ShowAsync().AsTask();
        await Task.Delay(900);

        ManagerUiChecks.Check(!string.IsNullOrWhiteSpace(dialog.Title?.ToString()), "startup update dialog title");
        var text = FlattenText(dialog.Content);
        ManagerUiChecks.Check(text.Contains(update.CurrentVersion) && text.Contains(update.LatestVersion), "startup update dialog shows both versions");
        ManagerUiChecks.Check(text.Contains(Loc.T("UpdateDialog_Notes_Header")), "startup update dialog shows changelog header");
        ManagerUiChecks.Check(dialog.PrimaryButtonText == Loc.T("UpdateDialog_Now") && dialog.CloseButtonText == Loc.T("UpdateDialog_Later"), "startup update dialog button labels");

        var image = new RenderTargetBitmap();
        await image.RenderAsync(dialog);
        ManagerUiChecks.Check(image.PixelWidth > 200 && image.PixelHeight > 100, "startup update dialog renders");
        await ManagerUiChecks.SaveBitmapAsync(image, "startup-update-dialog");

        dialog.Hide();
        var result = await showing;
        ManagerUiChecks.Check(result == ContentDialogResult.None, "startup update dialog dismisses without installing");
    }

    private static string FlattenText(object? content)
    {
        var builder = new System.Text.StringBuilder();
        if (content is not DependencyObject root)
        {
            return string.Empty;
        }

        void Walk(DependencyObject node)
        {
            if (node is TextBlock block)
            {
                builder.AppendLine(block.Text);
            }

            int count = VisualTreeHelper.GetChildrenCount(node);
            for (int i = 0; i < count; i++)
            {
                Walk(VisualTreeHelper.GetChild(node, i));
            }
        }

        Walk(root);
        return builder.ToString();
    }
#endif

    private async Task DownloadUpdateOnlyAsync(GitHubUpdateInfo update)
    {
        try
        {
            AppState.ShowStatus(Loc.T("About_Update_Downloading"), InfoBarSeverity.Informational);
            var pending = await PreparePortableUpdateAsync(update, CancellationToken.None);
            AppState.ShowStatus(Loc.Tf("Update_Downloaded_Format", pending.Version), InfoBarSeverity.Success);
        }
        catch (TimeoutException ex)
        {
            AppState.ShowStatus(ex.Message, InfoBarSeverity.Warning);
        }
        catch (Exception ex)
        {
            AppState.ShowStatus(Loc.Tf("About_Update_DownloadFailed_Format", ex.Message), InfoBarSeverity.Error);
        }
    }

    private async Task InstallUpdateFromStartupAsync(GitHubUpdateInfo update)
    {
        if (!PortableUpdateService.SelfUpdateSupported)
        {
            AppState.ShowStatus(Loc.T("Update_NoUpdater"), InfoBarSeverity.Warning);
            return;
        }

        try
        {
            var pending = await PreparePortableUpdateAsync(update, CancellationToken.None);
            AppState.ShowStatus(Loc.T("About_Update_Downloaded"), InfoBarSeverity.Success);
            if (!await ConfirmApplyAsync(pending))
            {
                return;
            }

            ApplyPortableUpdate(pending);
        }
        catch (TimeoutException ex)
        {
            AppState.ShowStatus(ex.Message, InfoBarSeverity.Warning);
        }
        catch (Exception ex)
        {
            AppState.ShowStatus(Loc.Tf("About_Update_DownloadFailed_Format", ex.Message), InfoBarSeverity.Error);
        }
    }

    private async Task<PortableUpdate> PreparePortableUpdateAsync(GitHubUpdateInfo update, CancellationToken token)
    {
        var progress = new Progress<UpdateDownloadProgress>(p =>
        {
            string text = p.TotalBytes is > 0
                ? Loc.Tf("About_Update_DownloadProgress_Format",
                    Math.Clamp(p.BytesReceived * 100d / p.TotalBytes.Value, 0d, 100d).ToString("0"),
                    FormatHelper.FormatFileSize(p.BytesReceived),
                    FormatHelper.FormatFileSize(p.TotalBytes.Value))
                : Loc.T("About_Update_DownloadProgress_Unknown");
            AppState.ShowStatus(text, InfoBarSeverity.Informational);
        });

        var proxy = AppState.SettingsService.Load().UpdateProxySite;
        return await AppState.PortableUpdateService.PrepareAsync(update, proxy, progress, token);
    }

    private async Task<bool> ConfirmApplyAsync(PortableUpdate pending)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = Loc.T("Update_Ready_Title"),
            Content = Loc.Tf("Update_Ready_Content_Format", pending.Version),
            PrimaryButtonText = Loc.T("Update_Ready_Apply"),
            CloseButtonText = Loc.T("Common_Cancel"),
            DefaultButton = ContentDialogButton.Primary,
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    internal bool ApplyPortableUpdate(PortableUpdate pending)
    {
        if (!AppState.PortableUpdateService.TryLaunchUpdater(pending, "SteamEyaWinUI.exe", out var error))
        {
            AppState.ShowStatus(error, InfoBarSeverity.Error);
            return false;
        }

        AppState.ShowStatus(Loc.T("About_Update_InstallerLaunched"), InfoBarSeverity.Warning);

        // 更新器已带着 --pid 启动，正在等本进程退出。这里必须正常退出：
        // Process.Kill(entireProcessTree: true) 在“要杀的进程树包含自己”时会抛
        // InvalidOperationException，正是旧版本报出的
        // “Cannot be used to terminate a process tree containing the calling process.”。
        DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                Close();
            }
            catch (Exception ex)
            {
                AppLog.Error("关闭主窗口失败。", ex);
            }

            Microsoft.UI.Xaml.Application.Current.Exit();
        });

        // 兜底：若 UI 线程因等待对话框等原因没有立刻退出，1.5 秒后强制结束进程，
        // 避免更新器一直等待 --pid 直到超时。
        _ = Task.Run(async () =>
        {
            await Task.Delay(1500);
            Environment.Exit(0);
        });

        return true;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static unsafe nint SubclassProc(
        nint hWnd,
        uint message,
        nint wParam,
        nint lParam,
        nuint subclassId,
        nuint referenceData)
    {
        if (message == WmGetMinMaxInfo)
        {
            var scale = GetDpiForWindow(hWnd) / 96.0;
            var info = (MinMaxInfo*)lParam;
            info->MinTrackSize.X = (int)Math.Ceiling(MinWindowWidth * scale);
            info->MinTrackSize.Y = (int)Math.Ceiling(MinWindowHeight * scale);
            return 0;
        }

        return DefSubclassProc(hWnd, message, wParam, lParam);
    }

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForWindow(nint hwnd);

    [LibraryImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool SetWindowSubclass(
        nint hWnd,
        delegate* unmanaged[Stdcall]<nint, uint, nint, nint, nuint, nuint, nint> callback,
        nuint subclassId,
        nuint referenceData);

    [LibraryImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool RemoveWindowSubclass(
        nint hWnd,
        delegate* unmanaged[Stdcall]<nint, uint, nint, nint, nuint, nuint, nint> callback,
        nuint subclassId);

    [LibraryImport("comctl32.dll")]
    private static partial nint DefSubclassProc(nint hWnd, uint message, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public Point Reserved;
        public Point MaxSize;
        public Point MaxPosition;
        public Point MinTrackSize;
        public Point MaxTrackSize;
    }
}
