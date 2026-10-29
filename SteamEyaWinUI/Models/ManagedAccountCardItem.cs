using System.ComponentModel;
using OpenSteamEya.AccountManagement;
using SteamEyaWinUI.Pages;

namespace SteamEyaWinUI.Models;

/// <summary>
/// 账号卡片的轻量条目（view-model）：只放标量与页面引用，不含任何 XAML 控件。
/// GridView 的 ItemsSource 放它，真正的 <see cref="AccountCard"/> 由 ItemTemplate 在容器实现时才创建，
/// 因此 100 个账号只付出 100 个数据对象，而不是 100 棵可视化树。
/// 该类型会作为 ItemsSource 跨越 WinRT ABI（x:Bind），故为 public sealed partial。
/// </summary>
public sealed partial class ManagedAccountCardItem : INotifyPropertyChanged
{
    internal ManagedAccountCardItem(AccountManagerPage owner, ManagedAccount account, string? mappingKey, bool compact, SteamAccountHistoryItem? history)
    {
        Owner = owner;
        Account = account;
        MappingKey = mappingKey;
        Compact = compact;
        History = history;
        IsPrivate = owner.IsPrivate;
        // 隐私模式下不保存明文；其余情况保留明文供「显示密码」使用（默认仍按偏好隐藏）。
        PasswordPlain = IsPrivate ? null : account.Password;
        PasswordRevealed = !IsPrivate && !owner.Preferences.HidePasswords;
        cooldownText = history?.RemainingCooldownText ?? "";
    }

    /// <summary>拥有该条目的页面（卡片重建时从这里取偏好、快照与回调）。</summary>
    internal AccountManagerPage Owner { get; }

    public ManagedAccount Account { get; }

    internal string? MappingKey { get; }

    public bool Compact { get; }

    /// <summary>创建该条目时的隐私状态；隐私模式不保留任何明文。</summary>
    public bool IsPrivate { get; }

    internal SteamAccountHistoryItem? History { get; }

    // ---------- 卡片尺寸（列数/宽度变化时由页面一次性写入，容器实现时卡片读取） ----------

    private double cardWidth;

    /// <summary>卡片目标宽度（DIP）。0 表示尚未计算，卡片使用自适应宽度。</summary>
    public double CardWidth
    {
        get => cardWidth;
        set
        {
            if (cardWidth == value) return;
            cardWidth = value;
            Raise(nameof(CardWidth));
        }
    }

    // ---------- 选择 / 揭示（页面与自检的瞬时状态，不持久化） ----------

    private bool isSelected;

    /// <summary>是否属于页面的选中集合（真源仍是页面的 Id 集合，这里只是投影）。</summary>
    public bool IsSelected
    {
        get => isSelected;
        set
        {
            if (isSelected == value) return;
            isSelected = value;
            Raise(nameof(IsSelected));
        }
    }

    private bool passwordRevealed;

    /// <summary>密码是否处于「显示」状态。揭示状态挂在条目上，容器回收再实现后仍然保持。</summary>
    public bool PasswordRevealed
    {
        get => passwordRevealed;
        set
        {
            if (passwordRevealed == value) return;
            passwordRevealed = value;
            Raise(nameof(PasswordRevealed));
        }
    }

    public bool HasPassword => Account.Password.Length > 0;

    public string PasswordMasked => "••••••••";

    /// <summary>明文密码；隐私模式下为 null（不是空串）。</summary>
    public string? PasswordPlain { get; }

    // ---------- 冷却倒计时（由页面共享的 1s 计时器驱动，卡片订阅后原地更新文本） ----------

    private string cooldownText;

    /// <summary>实时剩余冷却文本（随计时器递减），卡片收到通知后原地改写 Run。</summary>
    public string CooldownText => cooldownText;

    /// <summary>是否仍有正在走的冷却。</summary>
    public bool HasLiveCooldown => History?.HasLiveCooldown == true;

    /// <summary>计时器每秒调用一次；文本变化时通知卡片。返回是否仍需继续计时。</summary>
    internal bool TickCooldown()
    {
        // 即使冷却刚刚到期也要发通知：卡片据此把最后一格时间改写成「正常」并收起异常行。
        var text = History?.RemainingCooldownText ?? "";
        if (text != cooldownText)
        {
            cooldownText = text;
            Raise(nameof(CooldownText));
        }

        return HasLiveCooldown;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise(string property) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}
