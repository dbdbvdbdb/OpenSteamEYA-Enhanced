using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using SteamEyaWinUI.Models;

namespace SteamEyaWinUI.Pages;

internal sealed partial class AccountCard
{
    private Action? cooldownRefresh;
    private bool cooldownLive;

    /// <summary>卡片当前是否还有正在走的冷却（由页面共享的 1s 计时器驱动）。</summary>
    internal bool HasLiveCooldown => !stopped && cooldownLive;

#if MANAGER_UI_CHECKS
    internal bool HasActiveCooldownTimer => HasLiveCooldown;
#endif

    /// <summary>页面计时器每秒调一次；返回是否仍需继续计时。</summary>
    internal bool TickCooldown()
    {
        if (!HasLiveCooldown || cooldownRefresh is null) return false;
        cooldownRefresh();
        return HasLiveCooldown;
    }

    private TextBlock CreateCooldownStatus(SteamAccountHistoryItem? history, string? prefix = null)
    {
        var label = Label("", prefix is null ? 12 : 11, false, Muted);
        label.Name = "AccountCooldownStatus";
        var time = new Run(); var vac = new Run();
        void Update()
        {
            uint? seconds = history?.RemainingCooldownSeconds;
            bool cooling = seconds is > 0 and <= int.MaxValue;
            bool flagged = history?.GcVacBanned == true;
            bool knownNormal = seconds is not null && history?.GcVacBanned == false;
            var values = new List<Run>();
            if (cooling)
            {
                time.Text = history!.RemainingCooldownText; time.Foreground = Brush(255, 240, 196, 92); values.Add(time);
            }
            if (flagged)
            {
                vac.Text = "GCVAC：" + history!.GcVacText; vac.Foreground = Brush(255, 242, 111, 123); values.Add(vac);
            }
            if (values.Count == 0)
            {
                time.Text = knownNormal ? "正常" : "未知";
                time.Foreground = knownNormal ? Brush(255, 105, 204, 156) : Muted; values.Add(time);
            }
            label.Inlines.Clear();
            if (prefix is not null) label.Inlines.Add(new Run { Text = prefix });
            for (int i = 0; i < values.Count; i++)
            {
                if (i > 0) label.Inlines.Add(new Run { Text = prefix is null ? "\n" : " · " });
                label.Inlines.Add(values[i]);
            }
            label.MaxLines = prefix is null ? values.Count : 1;
            ToolTipService.SetToolTip(label, string.Join("\n", values.Select(x => x.Text)));
            cooldownLive = history?.HasLiveCooldown == true;
        }

        cooldownRefresh = Update;
        Update();
        return label;
    }
}
