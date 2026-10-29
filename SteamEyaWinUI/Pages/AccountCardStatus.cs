using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using SteamEyaWinUI.Models;

namespace SteamEyaWinUI.Pages;

internal sealed partial class AccountCard
{
    private TextBlock? cooldownLabel;
    private Run? cooldownValue;   // 冷却时间 / 正常 / 未知
    private Run? cooldownSep;     // 两个状态之间的分隔（"\n" 或 " · "）
    private Run? cooldownFlag;    // GCVAC 标记
    private Run? cooldownPrefix;  // 紧凑列表里的 "CS2 Lv. x · 优先状态未知 · "

    /// <summary>卡片当前是否还有正在走的冷却（由页面共享的 1s 计时器驱动）。</summary>
    internal bool HasLiveCooldown => !stopped && Item?.HasLiveCooldown == true;

#if MANAGER_UI_CHECKS
    internal bool HasActiveCooldownTimer => HasLiveCooldown;
#endif

    /// <summary>页面计时器每秒调一次；返回是否仍需继续计时。</summary>
    internal bool TickCooldown()
    {
        if (!HasLiveCooldown) return false;
        RefreshCooldown();
        return HasLiveCooldown;
    }

    private TextBlock CreateCooldownStatus(string? prefix = null)
    {
        var label = Label("", prefix is null ? 12 : 11, false, Muted);
        label.Name = "AccountCooldownStatus";
        // Run 实例建一次、长期复用：页面计时器只改文本与颜色，不替换 Inlines 里的对象。
        cooldownValue = new Run();
        cooldownFlag = new Run();
        cooldownSep = new Run { Text = prefix is null ? "\n" : " · " };
        cooldownPrefix = prefix is null ? null : new Run { Text = prefix };
        cooldownLabel = label;
        RefreshCooldown();
        return label;
    }

    /// <summary>按当前条目的冷却快照原地刷新文本/颜色（不重建控件，也不查询 Steam）。</summary>
    private void RefreshCooldown()
    {
        if (cooldownLabel is null || cooldownValue is null || cooldownFlag is null || cooldownSep is null) return;
        var history = Item?.History;
        uint? seconds = history?.RemainingCooldownSeconds;
        bool cooling = seconds is > 0 and <= int.MaxValue;
        bool flagged = history?.GcVacBanned == true;
        bool knownNormal = seconds is not null && history?.GcVacBanned == false;
        // 只把"确实存在"的值放进 Inlines：GCVAC 单独出现时不能把上一次的冷却/正常/未知文本留下来。
        var values = new List<Run>(2);
        if (cooling)
        {
            cooldownValue.Text = history!.RemainingCooldownText; cooldownValue.Foreground = Brush(255, 240, 196, 92);
            values.Add(cooldownValue);
        }
        if (flagged)
        {
            cooldownFlag.Text = "GCVAC：" + history!.GcVacText; cooldownFlag.Foreground = Brush(255, 242, 111, 123);
            values.Add(cooldownFlag);
        }
        if (values.Count == 0)
        {
            cooldownValue.Text = knownNormal ? "正常" : "未知";
            cooldownValue.Foreground = knownNormal ? Brush(255, 105, 204, 156) : Muted;
            values.Add(cooldownValue);
        }
        cooldownLabel.Inlines.Clear();
        if (cooldownPrefix is not null) cooldownLabel.Inlines.Add(cooldownPrefix);
        for (int i = 0; i < values.Count; i++)
        {
            if (i > 0) cooldownLabel.Inlines.Add(cooldownSep);
            cooldownLabel.Inlines.Add(values[i]);
        }
        cooldownLabel.MaxLines = cooldownPrefix is null ? values.Count : 1;
        ToolTipService.SetToolTip(cooldownLabel, string.Join("\n", values.Select(x => x.Text)));
    }
}
