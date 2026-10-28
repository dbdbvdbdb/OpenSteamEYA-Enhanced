#if MANAGER_UI_CHECKS
namespace SteamEyaWinUI.Services;

/// <summary>UI 自检专用的轻量计数器，只在 EnableManagerUiChecks 构建中存在。</summary>
internal static class UiCheckPerf
{
    public static int CardCreations;
    public static int PaletteBuilds;
    public static int RenderCount;
    public static int LastRenderCards;
    public static double LastRenderMs;
    public static double MaxRenderMs;
    public static double TotalRenderMs;

    public static void CardCreated() => CardCreations++;

    public static void PaletteBuilt() => PaletteBuilds++;

    public static void Render(double ms, int cards)
    {
        RenderCount++;
        LastRenderMs = ms;
        LastRenderCards = cards;
        TotalRenderMs += ms;
        if (ms > MaxRenderMs) MaxRenderMs = ms;
    }

    public static void Reset()
    {
        CardCreations = 0;
        PaletteBuilds = 0;
        RenderCount = 0;
        TotalRenderMs = 0;
        MaxRenderMs = 0;
    }

    public static string Snapshot() =>
        "renders=" + RenderCount + " cards_created=" + CardCreations + " palette_builds=" + PaletteBuilds +
        " last_ms=" + LastRenderMs.ToString("0.0") + " max_ms=" + MaxRenderMs.ToString("0.0") +
        " total_ms=" + TotalRenderMs.ToString("0.0") + " last_cards=" + LastRenderCards;
}
#endif
