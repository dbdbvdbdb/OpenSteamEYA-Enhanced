namespace SteamEyaWinUI.Cs2Settings;

internal static partial class SettingCatalog
{
    public const string SourceRevision = "5ab573fa8afb5b8db98869d79ed79399fca6e747";
    public const string VideoVersion = "16";
    private static class Cache
    {
        internal static readonly SettingSpec[] Visible = All.Select(Describe).Concat(VideoSettings()).Select(English).GroupBy(s => s.Key).Select(g => g.Last()).ToArray();
        internal static readonly SettingSpec[] Editable = Visible.Where(s => s.Availability == SettingAvailability.Verified).ToArray();
    }

    private static SettingSpec English(SettingSpec spec) => spec with { EnglishName = spec.Key switch
    {
        "fps_max" => "Maximum game FPS",
        "fps_max_ui" => "Maximum menu FPS",
        "r_fullscreen_gamma" => "Fullscreen gamma",
        "volume" => "Master volume",
        "snd_gamevolume" => "Game volume",
        "snd_voipvolume" => "Voice volume",
        "snd_musicvolume" => "Music volume",
        "snd_menumusic_volume" => "Main menu music",
        "snd_mvp_volume" => "MVP music",
        "snd_roundstart_volume" => "Round start music",
        "snd_roundend_volume" => "Round end music",
        "snd_tensecondwarning_volume" => "Ten second warning",
        "snd_mute_losefocus" => "Mute while unfocused",
        "snd_mute_mvp_music_live_players" => "Mute MVP music while alive",
        "hud_scaling" => "HUD scale",
        "cl_hud_radar_scale" => "Radar HUD scale",
        "cl_radar_scale" => "Radar map zoom",
        "cl_radar_icon_scale_min" => "Minimum radar icon size",
        "cl_radar_always_centered" => "Always center radar",
        "cl_radar_rotate" => "Rotate radar",
        "cl_radar_square_with_scoreboard" => "Square radar with scoreboard",
        "cl_showloadout" => "Always show inventory",
        "cl_use_opens_buy_menu" => "Open buy menu with use key",
        "mm_dedicated_search_maxping" => "Maximum matchmaking ping",
        "viewmodel_fov" => "Viewmodel field of view",
        "viewmodel_offset_x" => "Viewmodel X offset",
        "viewmodel_offset_y" => "Viewmodel Y offset",
        "viewmodel_offset_z" => "Viewmodel Z offset",
        "cl_crosshair_length" => "Crosshair length",
        "cl_crosshair_gap" => "Crosshair gap",
        "cl_crosshair_thickness" => "Crosshair thickness",
        "cl_crosshaircolor_r" => "Crosshair red",
        "cl_crosshaircolor_g" => "Crosshair green",
        "cl_crosshaircolor_b" => "Crosshair blue",
        "cl_crosshairdot" => "Crosshair center dot",
        "cl_crosshair_t" => "T style crosshair",
        "cl_crosshair_drawoutline" => "Crosshair outline",
        "cl_crosshair_recoil" => "Follow recoil",
        "cl_crosshair_sniper_width" => "Sniper crosshair width",
        "sensitivity" => "Mouse sensitivity",
        "zoom_sensitivity_ratio" => "Zoom sensitivity ratio",
        "cl_sniper_auto_rezoom" => "Rezoom after sniper shot",
        _ => spec.EnglishName
    } };

    private static SettingSpec Describe(SettingSpec spec)
    {
        if (spec.Key.StartsWith("setting.")) return spec with { Category = "视频", FileName = LocalConfigStore.VideoFile,
            Availability = SettingAvailability.Pending, HardwareDependent = true,
            Description = "显示设备模式需实际枚举；请使用下方显示模式组合控件，未知映射不写入。", EnglishName = spec.Key };
        return spec.Key switch
        {
            "fps_max" => spec with { FileName = LocalConfigStore.MachineFile, DefaultValue = "120", EnglishName = "fps_max", Evidence = "DumpSource2/convars.txt", Availability = SettingAvailability.Verified, AllowInsert = true, Description = "已核对当前 CS2 参数声明及归档作用域；保存后需重新启动游戏读取。", Category = "视频" },
            "fps_max_ui" => spec with { FileName = LocalConfigStore.MachineFile, DefaultValue = "0", EnglishName = "fps_max_ui", Evidence = "DumpSource2/convars.txt", Availability = SettingAvailability.Verified, AllowInsert = true, Description = "已核对当前 CS2 参数声明及归档作用域；保存后需重新启动游戏读取。", Category = "视频" },
            "r_fullscreen_gamma" => spec with { FileName = LocalConfigStore.MachineFile, DefaultValue = "2.2", EnglishName = "r_fullscreen_gamma", Evidence = "DumpSource2/convars.txt", Min = 1, Max = 4, Availability = SettingAvailability.Verified, AllowInsert = true, Description = "已核对当前 CS2 参数声明及归档作用域；保存后需重新启动游戏读取。", Category = "视频" },
            "volume" => spec with { FileName = LocalConfigStore.MachineFile, DefaultValue = "1", EnglishName = "volume", Evidence = "DumpSource2/convars.txt", Min = 0, Max = 1, Availability = SettingAvailability.Verified, AllowInsert = true, Description = "已核对当前 CS2 参数声明及归档作用域；保存后需重新启动游戏读取。" },
            "snd_gamevolume" => spec with { FileName = LocalConfigStore.MachineFile, DefaultValue = "1", EnglishName = "snd_gamevolume", Evidence = "DumpSource2/convars.txt", Min = 0, Max = 1, Availability = SettingAvailability.Verified, AllowInsert = true, Description = "已核对当前 CS2 参数声明及归档作用域；保存后需重新启动游戏读取。" },
            "snd_voipvolume" => spec with { FileName = LocalConfigStore.MachineFile, DefaultValue = "1", EnglishName = "snd_voipvolume", Evidence = "DumpSource2/convars.txt", Min = 0, Max = 2, Availability = SettingAvailability.Verified, AllowInsert = true, Description = "已核对当前 CS2 参数声明及归档作用域；保存后需重新启动游戏读取。" },
            "snd_musicvolume" => spec with { FileName = LocalConfigStore.MachineFile, DefaultValue = "1", EnglishName = "snd_musicvolume", Evidence = "DumpSource2/convars.txt", Min = 0, Max = 1, Availability = SettingAvailability.Verified, AllowInsert = true, Description = "已核对当前 CS2 参数声明及归档作用域；保存后需重新启动游戏读取。" },
            "snd_menumusic_volume" => spec with { FileName = LocalConfigStore.MachineFile, DefaultValue = "0.04", EnglishName = "snd_menumusic_volume", Evidence = "DumpSource2/convars.txt", Min = 0, Max = 1, Availability = SettingAvailability.Verified, AllowInsert = true, Description = "已核对当前 CS2 参数声明及归档作用域；保存后需重新启动游戏读取。" },
            "snd_mvp_volume" => spec with { FileName = LocalConfigStore.MachineFile, DefaultValue = "0.16", EnglishName = "snd_mvp_volume", Evidence = "DumpSource2/convars.txt", Min = 0, Max = 1, Availability = SettingAvailability.Verified, AllowInsert = true, Description = "已核对当前 CS2 参数声明及归档作用域；保存后需重新启动游戏读取。" },
            "snd_roundstart_volume" => spec with { FileName = LocalConfigStore.MachineFile, DefaultValue = "0", EnglishName = "snd_roundstart_volume", Evidence = "DumpSource2/convars.txt", Min = 0, Max = 1, Availability = SettingAvailability.Verified, AllowInsert = true, Description = "已核对当前 CS2 参数声明及归档作用域；保存后需重新启动游戏读取。" },
            "snd_roundend_volume" => spec with { FileName = LocalConfigStore.MachineFile, DefaultValue = "0.16", EnglishName = "snd_roundend_volume", Evidence = "DumpSource2/convars.txt", Min = 0, Max = 1, Availability = SettingAvailability.Verified, AllowInsert = true, Description = "已核对当前 CS2 参数声明及归档作用域；保存后需重新启动游戏读取。" },
            "snd_tensecondwarning_volume" => spec with { FileName = LocalConfigStore.MachineFile, DefaultValue = "0.04", EnglishName = "snd_tensecondwarning_volume", Evidence = "DumpSource2/convars.txt", Min = 0, Max = 1, Availability = SettingAvailability.Verified, AllowInsert = true, Description = "已核对当前 CS2 参数声明及归档作用域；保存后需重新启动游戏读取。" },
            "snd_mute_losefocus" => spec with { FileName = LocalConfigStore.MachineFile, DefaultValue = "true", EnglishName = "snd_mute_losefocus", Evidence = "DumpSource2/convars.txt", Boolean = true, Availability = SettingAvailability.Verified, AllowInsert = true, Description = "已核对当前 CS2 参数声明及归档作用域；保存后需重新启动游戏读取。" },
            "snd_mute_mvp_music_live_players" => spec with { FileName = LocalConfigStore.MachineFile, DefaultValue = "false", EnglishName = "snd_mute_mvp_music_live_players", Evidence = "DumpSource2/convars.txt", Boolean = true, Availability = SettingAvailability.Verified, AllowInsert = true, Description = "已核对当前 CS2 参数声明及归档作用域；保存后需重新启动游戏读取。" },
            "hud_scaling" => spec with { FileName = LocalConfigStore.MachineFile, DefaultValue = "1", EnglishName = "hud_scaling", Evidence = "DumpSource2/convars.txt", Min = 0.9, Max = 1.1, Availability = SettingAvailability.Verified, AllowInsert = true, Description = "已核对当前 CS2 参数声明及归档作用域；保存后需重新启动游戏读取。", Category = "HUD" },
            "cl_hud_radar_scale" => spec with { FileName = LocalConfigStore.MachineFile, DefaultValue = "1", EnglishName = "cl_hud_radar_scale", Evidence = "DumpSource2/convars.txt", Min = 0.8, Max = 1.3, Availability = SettingAvailability.Verified, AllowInsert = true, Description = "已核对当前 CS2 参数声明及归档作用域；保存后需重新启动游戏读取。", Category = "HUD" },
            "cl_radar_scale" => spec with { FileName = LocalConfigStore.MachineFile, DefaultValue = "0.7", EnglishName = "cl_radar_scale", Evidence = "DumpSource2/convars.txt", Min = 0.25, Max = 1, Availability = SettingAvailability.Verified, AllowInsert = true, Description = "已核对当前 CS2 参数声明及归档作用域；保存后需重新启动游戏读取。", Category = "HUD" },
            "cl_radar_icon_scale_min" => spec with { FileName = LocalConfigStore.MachineFile, DefaultValue = "0.6", EnglishName = "cl_radar_icon_scale_min", Evidence = "DumpSource2/convars.txt", Min = 0.4, Max = 1.25, Availability = SettingAvailability.Verified, AllowInsert = true, Description = "已核对当前 CS2 参数声明及归档作用域；保存后需重新启动游戏读取。", Category = "HUD" },
            "cl_radar_always_centered" => spec with { FileName = LocalConfigStore.MachineFile, DefaultValue = "true", EnglishName = "cl_radar_always_centered", Evidence = "DumpSource2/convars.txt", Boolean = true, Availability = SettingAvailability.Verified, AllowInsert = true, Description = "已核对当前 CS2 参数声明及归档作用域；保存后需重新启动游戏读取。", Category = "HUD" },
            "cl_radar_rotate" => spec with { FileName = LocalConfigStore.MachineFile, DefaultValue = "true", EnglishName = "cl_radar_rotate", Evidence = "DumpSource2/convars.txt", Boolean = true, Availability = SettingAvailability.Verified, AllowInsert = true, Description = "已核对当前 CS2 参数声明及归档作用域；保存后需重新启动游戏读取。", Category = "HUD" },
            "cl_radar_square_with_scoreboard" => spec with { FileName = LocalConfigStore.MachineFile, DefaultValue = "true", EnglishName = "cl_radar_square_with_scoreboard", Evidence = "DumpSource2/convars.txt", Boolean = true, Availability = SettingAvailability.Verified, AllowInsert = true, Description = "已核对当前 CS2 参数声明及归档作用域；保存后需重新启动游戏读取。", Category = "HUD" },
            "cl_showloadout" => spec with { FileName = LocalConfigStore.UserFile, DefaultValue = "true", EnglishName = "cl_showloadout", Evidence = "DumpSource2/convars.txt", Boolean = true, Availability = SettingAvailability.Verified, AllowInsert = true, Description = "已核对当前 CS2 参数声明及归档作用域；保存后需重新启动游戏读取。", Category = "游戏" },
            "cl_use_opens_buy_menu" => spec with { FileName = LocalConfigStore.UserFile, DefaultValue = "false", EnglishName = "cl_use_opens_buy_menu", Evidence = "DumpSource2/convars.txt", Boolean = true, Availability = SettingAvailability.Verified, AllowInsert = true, Description = "已核对当前 CS2 参数声明及归档作用域；保存后需重新启动游戏读取。", Category = "游戏" },
            "mm_dedicated_search_maxping" => spec with { FileName = LocalConfigStore.MachineFile, DefaultValue = "150", EnglishName = "mm_dedicated_search_maxping", Evidence = "DumpSource2/convars.txt", Min = 25, Max = 350, Availability = SettingAvailability.Verified, AllowInsert = true, Description = "已核对当前 CS2 参数声明及归档作用域；保存后需重新启动游戏读取。", Category = "游戏" },
            "viewmodel_fov" => spec with { FileName = LocalConfigStore.UserFile, DefaultValue = "60", EnglishName = "viewmodel_fov", Evidence = "DumpSource2/convars.txt", Min = 60, Max = 68, Availability = SettingAvailability.Verified, AllowInsert = true, Description = "已核对当前 CS2 参数声明及归档作用域；保存后需重新启动游戏读取。", Category = "游戏" },
            "viewmodel_offset_x" => spec with { FileName = LocalConfigStore.UserFile, DefaultValue = "1", EnglishName = "viewmodel_offset_x", Evidence = "DumpSource2/convars.txt", Min = -2, Max = 2.5, Availability = SettingAvailability.Verified, AllowInsert = true, Description = "已核对当前 CS2 参数声明及归档作用域；保存后需重新启动游戏读取。", Category = "游戏" },
            "viewmodel_offset_y" => spec with { FileName = LocalConfigStore.UserFile, DefaultValue = "1", EnglishName = "viewmodel_offset_y", Evidence = "DumpSource2/convars.txt", Min = -2, Max = 2, Availability = SettingAvailability.Verified, AllowInsert = true, Description = "已核对当前 CS2 参数声明及归档作用域；保存后需重新启动游戏读取。", Category = "游戏" },
            "viewmodel_offset_z" => spec with { FileName = LocalConfigStore.UserFile, DefaultValue = "-1", EnglishName = "viewmodel_offset_z", Evidence = "DumpSource2/convars.txt", Min = -2, Max = 2, Availability = SettingAvailability.Verified, AllowInsert = true, Description = "已核对当前 CS2 参数声明及归档作用域；保存后需重新启动游戏读取。", Category = "游戏" },
            "cl_crosshair_length" => spec with { FileName = LocalConfigStore.UserFile, DefaultValue = "8", EnglishName = "cl_crosshair_length", Evidence = "DumpSource2/convars.txt", Min = 0, Max = 255, Availability = SettingAvailability.Verified, AllowInsert = true, Description = "已核对当前 CS2 参数声明及归档作用域；保存后需重新启动游戏读取。" },
            "cl_crosshair_gap" => spec with { FileName = LocalConfigStore.UserFile, DefaultValue = "4", EnglishName = "cl_crosshair_gap", Evidence = "DumpSource2/convars.txt", Min = -3840, Max = 3840, Availability = SettingAvailability.Verified, AllowInsert = true, Description = "已核对当前 CS2 参数声明及归档作用域；保存后需重新启动游戏读取。" },
            "cl_crosshair_thickness" => spec with { FileName = LocalConfigStore.UserFile, DefaultValue = "2", EnglishName = "cl_crosshair_thickness", Evidence = "DumpSource2/convars.txt", Min = 0, Max = 32, Availability = SettingAvailability.Verified, AllowInsert = true, Description = "已核对当前 CS2 参数声明及归档作用域；保存后需重新启动游戏读取。" },
            "cl_crosshairsize" => spec with { FileName = LocalConfigStore.UserFile, DefaultValue = "3.9", EnglishName = "cl_crosshairsize", Evidence = "DumpSource2/convars.txt", Availability = SettingAvailability.Unsupported, AllowInsert = false, Description = "当前游戏声明为隐藏参数；仅保留导入值。" },
            "cl_crosshairgap" => spec with { Availability = SettingAvailability.Unsupported, Description = "当前游戏参数声明中未找到此项；保留导入文件，不允许新增或修改。" },
            "cl_crosshairthickness" => spec with { FileName = LocalConfigStore.UserFile, DefaultValue = "0.6", EnglishName = "cl_crosshairthickness", Evidence = "DumpSource2/convars.txt", Availability = SettingAvailability.Unsupported, AllowInsert = false, Description = "当前游戏声明为隐藏参数；仅保留导入值。" },
            "cl_crosshaircolor_r" => spec with { FileName = LocalConfigStore.UserFile, DefaultValue = "0", EnglishName = "cl_crosshaircolor_r", Evidence = "DumpSource2/convars.txt", Min = 0, Max = 255, Availability = SettingAvailability.Verified, AllowInsert = true, Description = "已核对当前 CS2 参数声明及归档作用域；保存后需重新启动游戏读取。" },
            "cl_crosshaircolor_g" => spec with { FileName = LocalConfigStore.UserFile, DefaultValue = "255", EnglishName = "cl_crosshaircolor_g", Evidence = "DumpSource2/convars.txt", Min = 0, Max = 255, Availability = SettingAvailability.Verified, AllowInsert = true, Description = "已核对当前 CS2 参数声明及归档作用域；保存后需重新启动游戏读取。" },
            "cl_crosshaircolor_b" => spec with { FileName = LocalConfigStore.UserFile, DefaultValue = "0", EnglishName = "cl_crosshaircolor_b", Evidence = "DumpSource2/convars.txt", Min = 0, Max = 255, Availability = SettingAvailability.Verified, AllowInsert = true, Description = "已核对当前 CS2 参数声明及归档作用域；保存后需重新启动游戏读取。" },
            "cl_crosshairalpha" => spec with { FileName = LocalConfigStore.UserFile, DefaultValue = "200", EnglishName = "cl_crosshairalpha", Evidence = "DumpSource2/convars.txt", Min = 0, Max = 255, Availability = SettingAvailability.Unsupported, AllowInsert = false, Description = "当前游戏声明为隐藏参数；仅保留导入值。" },
            "cl_crosshairdot" => spec with { FileName = LocalConfigStore.UserFile, DefaultValue = "false", EnglishName = "cl_crosshairdot", Evidence = "DumpSource2/convars.txt", Boolean = true, Availability = SettingAvailability.Verified, AllowInsert = true, Description = "已核对当前 CS2 参数声明及归档作用域；保存后需重新启动游戏读取。" },
            "cl_crosshair_t" => spec with { FileName = LocalConfigStore.UserFile, DefaultValue = "false", EnglishName = "cl_crosshair_t", Evidence = "DumpSource2/convars.txt", Boolean = true, Availability = SettingAvailability.Verified, AllowInsert = true, Description = "已核对当前 CS2 参数声明及归档作用域；保存后需重新启动游戏读取。" },
            "cl_crosshair_drawoutline" => spec with { FileName = LocalConfigStore.UserFile, DefaultValue = "1", EnglishName = "cl_crosshair_drawoutline", Evidence = "DumpSource2/convars.txt", Min = 0, Max = 2, Boolean = false, Step = 1, Availability = SettingAvailability.Verified, AllowInsert = true, Description = "已核对当前 CS2 参数声明及归档作用域；保存后需重新启动游戏读取。" },
            "cl_crosshair_recoil" => spec with { FileName = LocalConfigStore.UserFile, DefaultValue = "true", EnglishName = "cl_crosshair_recoil", Evidence = "DumpSource2/convars.txt", Boolean = true, Availability = SettingAvailability.Verified, AllowInsert = true, Description = "已核对当前 CS2 参数声明及归档作用域；保存后需重新启动游戏读取。" },
            "cl_crosshair_sniper_width" => spec with { FileName = LocalConfigStore.UserFile, DefaultValue = "1", EnglishName = "cl_crosshair_sniper_width", Evidence = "DumpSource2/convars.txt", Availability = SettingAvailability.Verified, AllowInsert = true, Description = "已核对当前 CS2 参数声明及归档作用域；保存后需重新启动游戏读取。" },
            "sensitivity" => spec with { FileName = LocalConfigStore.UserFile, DefaultValue = "1.25", EnglishName = "sensitivity", Evidence = "DumpSource2/convars.txt", Min = 0.0001, Max = 8, Availability = SettingAvailability.Verified, AllowInsert = true, Description = "已核对当前 CS2 参数声明及归档作用域；保存后需重新启动游戏读取。" },
            "zoom_sensitivity_ratio" => spec with { FileName = LocalConfigStore.UserFile, DefaultValue = "1", EnglishName = "zoom_sensitivity_ratio", Evidence = "DumpSource2/convars.txt", Min = 0.01, Max = 3, Availability = SettingAvailability.Verified, AllowInsert = true, Description = "已核对当前 CS2 参数声明及归档作用域；保存后需重新启动游戏读取。" },
            "cl_sniper_auto_rezoom" => spec with { FileName = LocalConfigStore.UserFile, DefaultValue = "true", EnglishName = "cl_sniper_auto_rezoom", Evidence = "DumpSource2/convars.txt", Boolean = true, Availability = SettingAvailability.Verified, AllowInsert = true, Description = "已核对当前 CS2 参数声明及归档作用域；保存后需重新启动游戏读取。" },
            _ => spec
        };
    }

    private static IEnumerable<SettingSpec> VideoSettings()
    {
        SettingSpec V(string key, string cn, string en, string def, params string[] options) =>
            C(key, cn, "视频", options) with { EnglishName = en, FileName = LocalConfigStore.VideoFile,
                DefaultValue = def, DefaultOrigin = "应用模板参考：游戏 video_defaults_2 高画质预设（并非硬件推荐）",
                Availability = SettingAvailability.Verified, AllowInsert = true,
                Description = "高级视频 · 已核对游戏菜单档位及 video_defaults 配置；兼容视频格式 16。",
                Evidence = "panorama/layout/settings/settings_video.xml; cfg/video_defaults_0..3.txt" };
        yield return V("setting.msaa_samples", "多重采样抗锯齿", "MSAA", "4", "0|关闭", "2|2× MSAA", "4|4× MSAA", "8|8× MSAA");
        yield return V("setting.r_csgo_cmaa_enable", "CMAA 抗锯齿", "CMAA", "0", "0|关闭", "1|开启");
        yield return V("setting.videocfg_shadow_quality", "全局阴影质量", "Global shadow quality", "2", "0|低", "1|中", "2|高", "3|非常高");
        yield return V("setting.videocfg_dynamic_shadows", "动态阴影", "Dynamic shadows", "1", "0|仅太阳光", "1|全部");
        yield return V("setting.videocfg_texture_detail", "模型 / 纹理细节", "Model / texture detail", "2", "0|低", "1|中", "2|高");
        yield return V("setting.r_texturefilteringquality", "纹理过滤模式", "Texture filtering", "3", "0|双线性", "1|三线性", "2|2× 各向异性", "3|4× 各向异性", "4|8× 各向异性", "5|16× 各向异性");
        yield return V("setting.shaderquality", "着色器细节", "Shader detail", "1", "0|低", "1|高");
        yield return V("setting.videocfg_particle_detail", "粒子细节", "Particle detail", "2", "0|低", "1|中", "2|高", "3|非常高");
        yield return V("setting.videocfg_ao_detail", "环境光遮蔽", "Ambient occlusion", "2", "0|关闭", "2|中", "3|高");
        yield return V("setting.videocfg_hdr_detail", "高动态范围（HDR）", "HDR", "-1", "3|性能", "-1|质量");
        yield return V("setting.videocfg_fsr_detail", "FidelityFX Super Resolution", "FSR", "0", "0|关闭", "1|超高质量", "2|质量", "3|平衡", "4|性能");
        yield return V("setting.mat_vsync", "垂直同步", "Vertical sync", "0", "0|关闭", "1|开启") with { DefaultOrigin = "应用模板关闭值；非硬件推荐" };
        yield return V("setting.r_low_latency", "NVIDIA Reflex 低延迟", "NVIDIA Reflex", "0", "0|关闭", "1|启用", "2|启用 + 增强") with {
            Availability = SettingAvailability.HardwareDependent, HardwareDependent = true, AllowInsert = false,
            Description = "游戏菜单档位已核对，当前尚未实现可靠的驱动能力检测，离线编辑暂不可用；保留导入值。" };
        yield return new("r_player_visibility_mode", "增强玩家对比度", "视频", Boolean: true) {
            FileName = LocalConfigStore.MachineFile, DefaultValue = "1", EnglishName = "Boost player contrast",
            Availability = SettingAvailability.Verified, AllowInsert = true,
            Description = "高级视频 · 0 为关闭，1 为开启。", Evidence = "DumpSource2/convars.txt; settings_video.xml" };
        foreach (var (key, title) in new[] { ("setting.monitor_index", "显示器选择"), ("setting.fullscreen", "显示模式"),
            ("setting.aspectratiomode", "宽高比"), ("setting.refreshrate_numerator", "刷新率"),
            ("setting.cpu_level", "CPU 自动检测等级"), ("setting.gpu_level", "GPU 自动检测等级"),
            ("setting.gpu_mem_level", "显存自动检测等级"), ("ui:render-scale", "渲染比例 / 画面锐化"), ("ui:gsync", "G-Sync / 驱动高级垂直同步") })
            yield return new(key, title, "视频") { EnglishName = key, FileName = LocalConfigStore.VideoFile,
                Availability = SettingAvailability.Pending, HardwareDependent = true,
                Description = "不支持离线修改：当前无法确认完整的原生映射或驱动能力，不生成此配置键。",
                Evidence = "settings_video.xml（部分行为由原生 CSGOVideoSettings 控件管理）" };
    }

    public static SettingSpec[] Editable => Cache.Editable;
    public static SettingSpec[] Visible => Cache.Visible;
}
