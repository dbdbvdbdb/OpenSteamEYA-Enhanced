using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using SteamEyaWinUI.Localization;
using SteamEyaWinUI.Services;

namespace SteamEyaWinUI.Pages;

/// <summary>
/// 「CS2 配置」入口页：用胶囊页签在「统一配置」「配装」「个性化」之间切换，
/// 三个视图仍是各自的 Page，因此保留原有的 OnNavigatedTo 初始化逻辑。
/// </summary>
public sealed partial class Cs2HubPage : Page
{
    private readonly Type[] _pages =
    [
        typeof(Cs2LocalSettingsPage),
        typeof(LoadoutPage),
        typeof(PersonalizationPage),
    ];

    private bool _syncing;

#if MANAGER_UI_CHECKS
    internal void SelectTab(int index) => Select(index);
#endif

    public Cs2HubPage()
    {
        InitializeComponent();
        RefreshTabText();
        Loc.LanguageChanged += OnLanguageChanged;
        Unloaded += (_, _) => Loc.LanguageChanged -= OnLanguageChanged;
    }

    private void OnLanguageChanged()
    {
        DispatcherQueue.TryEnqueue(RefreshTabText);
    }

    private void RefreshTabText()
    {
        UnifiedTab.Content = Loc.T("Cs2Hub_Unified");
        LoadoutTab.Content = Loc.T("Nav_Loadout");
        PersonalizeTab.Content = Loc.T("Nav_Personalization");
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (HubFrame.Content is null)
        {
            Select(0);
        }
    }

    private void Tab_Click(object sender, RoutedEventArgs e)
    {
        if (_syncing || sender is not ToggleButton button || button.Tag is not string tag)
        {
            return;
        }

        Select(tag switch
        {
            "loadout" => 1,
            "personalize" => 2,
            _ => 0,
        });
    }

    private void Select(int index)
    {
        _syncing = true;
        try
        {
            var buttons = new[] { UnifiedTab, LoadoutTab, PersonalizeTab };
            for (var i = 0; i < buttons.Length; i++)
            {
                ApplyPill(buttons[i], i == index);
            }

            var target = _pages[index];
            if (HubFrame.CurrentSourcePageType != target)
            {
                HubFrame.Navigate(target);
            }
        }
        finally
        {
            _syncing = false;
        }
    }

    private static void ApplyPill(ToggleButton button, bool active)
    {
        button.IsChecked = active;
        button.Background = (Brush)Application.Current.Resources[active ? "PclAccentBrush" : "PclSurfaceAltBrush"];
        button.BorderBrush = (Brush)Application.Current.Resources[active ? "PclAccentBrush" : "PclBorderBrush"];
        button.Foreground = (Brush)Application.Current.Resources[active ? "PclOnAccentBrush" : "PclTextSecondaryBrush"];
    }
}
