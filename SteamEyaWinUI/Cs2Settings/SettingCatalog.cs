using System.Globalization;
using System.Text.RegularExpressions;

namespace SteamEyaWinUI.Cs2Settings;

internal enum SettingAvailability { Verified, HardwareDependent, Unsupported, Pending }

internal sealed record SettingChoice(string Value, string Label);
internal sealed record SettingSpec(string Key, string Label, string Category, double Min = 0, double Max = 1,
    double Step = .01, SettingChoice[]? Choices = null, bool Boolean = false)
{
    public string EnglishName { get; init; } = "";
    public string Description { get; init; } = "";
    public string FileName { get; init; } = LocalConfigStore.UserFile;
    public string Section => FileName == LocalConfigStore.VideoFile ? "video.cfg" : "config/convars";
    public string? DefaultValue { get; init; }
    public string DefaultOrigin { get; init; } = "游戏参数声明";
    public SettingAvailability Availability { get; init; } = SettingAvailability.Pending;
    public bool AllowInsert { get; init; }
    public bool HardwareDependent { get; init; }
    public bool RestartRequired { get; init; } = true;
    public string Compatibility { get; init; } = SettingCatalog.GameCompatibility;
    public string Evidence { get; init; } = "";
    public string DataType => Boolean ? "boolean" : Choices is not null ? "enum" : Step >= 1 ? "integer" : "number";

    public string Validate(string value, string original)
    {
        if (Boolean)
        {
            if (value is not "true" and not "false") throw new InvalidDataException(Label + "：开关值无效。");
            return original is "0" or "1" ? value == "true" ? "1" : "0" : value;
        }
        if (Choices is not null)
        {
            if (!Choices.Any(c => c.Value == value)) throw new InvalidDataException(Label + "：请选择支持的选项。");
            return value;
        }
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) || !double.IsFinite(number) || number < Min || number > Max || (Step >= 1 && number != Math.Truncate(number)))
            throw new InvalidDataException($"{Label}：请输入 {Min}–{Max} 范围内的有效数值。");
        return number.ToString("0.######", CultureInfo.InvariantCulture);
    }
}

internal static partial class SettingCatalog
{
    public static string BaseKey(string key) => Regex.Replace(key, @"\$[0-9]+$", "");
    private static SettingSpec N(string key, string title, string category, double min, double max, double step = .01) => new(key,title,category,min,max,step);
    private static SettingSpec B(string key, string title, string category) => new(key,title,category,Boolean:true);
    private static SettingSpec C(string key, string title, string category, params string[] values) => new(key,title,category,Choices:values.Select(v => { var p=v.Split('|'); return new SettingChoice(p[0],p[1]); }).ToArray());
    public static readonly SettingSpec[] All =
    [
        N("setting.defaultres", "分辨率宽度", "画面", 640, 16384, 1),
        N("setting.defaultresheight", "分辨率高度", "画面", 480, 16384, 1),
        B("setting.mat_vsync", "垂直同步", "画面"),
        C("setting.msaa_samples", "多重采样抗锯齿", "画面", "0|关闭", "2|2× MSAA", "4|4× MSAA", "8|8× MSAA"),
        N("fps_max", "游戏最大帧率（0 为不限）", "画面", 0, 1000, 1),
        N("fps_max_ui", "菜单最大帧率（0 为不限）", "画面", 0, 1000, 1),
        N("r_fullscreen_gamma", "全屏 Gamma", "画面", 1.6, 2.6),
        N("volume", "主音量", "音频", 0, 1), N("snd_gamevolume", "游戏音量", "音频", 0, 1),
        N("snd_voipvolume", "语音音量", "音频", 0, 1), N("snd_musicvolume", "音乐音量", "音频", 0, 1),
        N("snd_menumusic_volume", "主菜单音乐", "音频", 0, 1), N("snd_mvp_volume", "MVP 音乐", "音频", 0, 1),
        N("snd_roundstart_volume", "回合开始音乐", "音频", 0, 1), N("snd_roundend_volume", "回合结束音乐", "音频", 0, 1),
        N("snd_tensecondwarning_volume", "十秒警告音量", "音频", 0, 1),
        B("snd_mute_losefocus", "切出游戏时静音", "音频"),
        B("snd_mute_mvp_music_live_players", "存活时关闭 MVP 音乐", "音频"),
        N("hud_scaling", "HUD 缩放", "游戏 / HUD", .5, 1.5),
        N("cl_hud_radar_scale", "雷达面板大小", "游戏 / HUD", .5, 2),
        N("cl_radar_scale", "雷达地图缩放", "游戏 / HUD", .1, 1),
        N("cl_radar_icon_scale_min", "雷达图标大小", "游戏 / HUD", .4, 1),
        B("cl_radar_always_centered", "雷达始终居中", "游戏 / HUD"), B("cl_radar_rotate", "雷达随视角旋转", "游戏 / HUD"),
        B("cl_radar_square_with_scoreboard", "计分板显示方形雷达", "游戏 / HUD"),
        B("cl_showloadout", "始终显示装备栏", "游戏 / HUD"), B("cl_use_opens_buy_menu", "使用键打开购买菜单", "游戏 / HUD"),
        N("mm_dedicated_search_maxping", "匹配可接受最大延迟（ms）", "游戏 / HUD", 25, 350, 1),
        N("viewmodel_fov", "持枪视角 FOV", "游戏 / HUD", 54, 68, 1),
        N("viewmodel_offset_x", "持枪位置：水平", "游戏 / HUD", -2, 2.5),
        N("viewmodel_offset_y", "持枪位置：前后", "游戏 / HUD", -2, 2),
        N("viewmodel_offset_z", "持枪位置：垂直", "游戏 / HUD", -2, 2),
        N("cl_crosshair_length", "准星线长", "准星", 0, 20, .1), N("cl_crosshair_gap", "准星间距", "准星", -20, 20, .1),
        N("cl_crosshair_thickness", "准星粗细", "准星", 0, 10, .1),
        N("cl_crosshairsize", "经典准星线长", "准星", 0, 20, .1), N("cl_crosshairgap", "经典准星间距", "准星", -20, 20, .1),
        N("cl_crosshairthickness", "经典准星粗细", "准星", 0, 10, .1),
        N("cl_crosshaircolor_r", "红色", "准星", 0, 255, 1), N("cl_crosshaircolor_g", "绿色", "准星", 0, 255, 1),
        N("cl_crosshaircolor_b", "蓝色", "准星", 0, 255, 1), N("cl_crosshairalpha", "不透明度", "准星", 0, 255, 1),
        B("cl_crosshairdot", "中心点", "准星"), B("cl_crosshair_t", "T 形准星", "准星"),
        B("cl_crosshair_drawoutline", "描边", "准星"), B("cl_crosshair_recoil", "跟随后坐力", "准星"),
        N("cl_crosshair_sniper_width", "狙击准星宽度", "准星", 1, 4, 1),
        N("sensitivity", "鼠标灵敏度", "鼠标键盘", .01, 20),
        N("zoom_sensitivity_ratio", "开镜灵敏度倍率", "鼠标键盘", .01, 5),
        B("cl_sniper_auto_rezoom", "狙击射击后自动开镜", "鼠标键盘")
    ];
    public static readonly SettingChoice[] BindActions =
    [new("<unbound>","取消绑定"),new("+forward","前进"),new("+back","后退"),new("+left","左移"),new("+right","右移"),
     new("+jump","跳跃"),new("+duck","蹲下"),new("+sprint","静步"),new("+attack","开火"),new("+attack2","副攻击"),
     new("+reload","装填"),new("+use","使用"),new("drop","丢弃武器"),new("lastinv","切换上次武器"),
     new("buymenu","购买菜单"),new("+showscores","计分板"),new("+voicerecord","语音"),new("player_ping","标记"),
     new("slot1","主武器"),new("slot2","副武器"),new("slot3","匕首"),new("slot4","投掷物"),new("slot5","C4"),
     new("slot6","高爆手雷"),new("slot7","闪光弹"),new("slot8","烟雾弹"),new("slot9","诱饵弹"),new("slot10","燃烧弹"),
     new("+lookatweapon","检视武器"),new("switchhands","切换持枪手"),new("toggleconsole","开发者控制台")];
    public static readonly string[] BindKeys = "a b c d e f g h i j k l m n o p q r s t u v w x y z 0 1 2 3 4 5 6 7 8 9 SPACE CTRL SHIFT ALT TAB ENTER BACKSPACE ESCAPE F1 F2 F3 F4 F5 F6 F7 F8 F9 F10 F11 F12 MOUSE1 MOUSE2 MOUSE3 MOUSE4 MOUSE5 MWHEELUP MWHEELDOWN UPARROW DOWNARROW LEFTARROW RIGHTARROW".Split(' ');
}
