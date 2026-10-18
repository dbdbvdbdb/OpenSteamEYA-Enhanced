# CS2 全量参数实机状态

静态目录与人工复核的真实游戏观察合并。部分通过仅表示读取/档位与重启保留有证据，尚未完成该参数的客观效果验收。完整限制、范围及证据路径见同名 CSV 和 runtime-observations.json。

| ID | 名称 | 文件 / 节点 | 游戏观察 | 重启观察 | 状态 |
| --- | --- | --- | --- | --- | --- |
| setting.defaultres | 分辨率宽度 | cs2_video.txt / video.cfg | 未部署测试 | 未验证 | 待实机验证 |
| setting.defaultresheight | 分辨率高度 | cs2_video.txt / video.cfg | 未部署测试 | 未验证 | 待实机验证 |
| setting.mat_vsync | 垂直同步 | cs2_video.txt / video.cfg | 1 | 1 | 部分通过 |
| setting.msaa_samples | 多重采样抗锯齿 | cs2_video.txt / video.cfg | 0; native menu later 2 | 0; native menu later 2 | 部分通过 |
| fps_max | 游戏最大帧率（0 为不限） | cs2_machine_convars.vcfg / config/convars | 141 | 141 | 部分通过 |
| fps_max_ui | 菜单最大帧率（0 为不限） | cs2_machine_convars.vcfg / config/convars | 51 | 51 | 部分通过 |
| r_fullscreen_gamma | 全屏 Gamma | cs2_machine_convars.vcfg / config/convars | 2.3 | 2.3 | 部分通过 |
| volume | 主音量 | cs2_machine_convars.vcfg / config/convars | 0.36 | 0.36 | 部分通过 |
| snd_gamevolume | 游戏音量 | cs2_machine_convars.vcfg / config/convars | 0.64 | 0.64 | 部分通过 |
| snd_voipvolume | 语音音量 | cs2_machine_convars.vcfg / config/convars | 0.49 | 0.49 | 部分通过 |
| snd_musicvolume | 音乐音量 | cs2_machine_convars.vcfg / config/convars | 0.25 | 0.25 | 部分通过 |
| snd_menumusic_volume | 主菜单音乐 | cs2_machine_convars.vcfg / config/convars | 0.09 | 0.09 | 部分通过 |
| snd_mvp_volume | MVP 音乐 | cs2_machine_convars.vcfg / config/convars | 0.16 | 0.16 | 部分通过 |
| snd_roundstart_volume | 回合开始音乐 | cs2_machine_convars.vcfg / config/convars | 0.04 | 0.04 | 部分通过 |
| snd_roundend_volume | 回合结束音乐 | cs2_machine_convars.vcfg / config/convars | 0.09 | 0.09 | 部分通过 |
| snd_tensecondwarning_volume | 十秒警告音量 | cs2_machine_convars.vcfg / config/convars | 0.25 | 0.25 | 部分通过 |
| snd_mute_losefocus | 切出游戏时静音 | cs2_machine_convars.vcfg / config/convars | true | true | 部分通过 |
| snd_mute_mvp_music_live_players | 存活时关闭 MVP 音乐 | cs2_machine_convars.vcfg / config/convars | false | false | 部分通过 |
| hud_scaling | HUD 缩放 | cs2_machine_convars.vcfg / config/convars | 0.95 | 0.95 | 部分通过 |
| cl_hud_radar_scale | 雷达面板大小 | cs2_machine_convars.vcfg / config/convars | 0.9 | 0.9 | 部分通过 |
| cl_radar_scale | 雷达地图缩放 | cs2_machine_convars.vcfg / config/convars | 0.4 | 0.4 | 部分通过 |
| cl_radar_icon_scale_min | 雷达图标大小 | cs2_machine_convars.vcfg / config/convars | 0.8 | 0.8 | 部分通过 |
| cl_radar_always_centered | 雷达始终居中 | cs2_machine_convars.vcfg / config/convars | false | false | 部分通过 |
| cl_radar_rotate | 雷达随视角旋转 | cs2_machine_convars.vcfg / config/convars | false | false | 部分通过 |
| cl_radar_square_with_scoreboard | 计分板显示方形雷达 | cs2_machine_convars.vcfg / config/convars | false | false | 部分通过 |
| cl_showloadout | 始终显示装备栏 | cs2_user_convars_0_slot0.vcfg / config/convars | false; B first=true, repeat=false | false | 部分通过 |
| cl_use_opens_buy_menu | 使用键打开购买菜单 | cs2_user_convars_0_slot0.vcfg / config/convars | true; B first=false, repeat=true | true | 部分通过 |
| mm_dedicated_search_maxping | 匹配可接受最大延迟（ms） | cs2_machine_convars.vcfg / config/convars | 80 | 80 | 部分通过 |
| viewmodel_fov | 持枪视角 FOV | cs2_user_convars_0_slot0.vcfg / config/convars | 64; B first=68, repeat=64 | 64 | 部分通过 |
| viewmodel_offset_x | 持枪位置：水平 | cs2_user_convars_0_slot0.vcfg / config/convars | 1; B first=2.5, repeat=1 | 1 | 部分通过 |
| viewmodel_offset_y | 持枪位置：前后 | cs2_user_convars_0_slot0.vcfg / config/convars | 1; B first=0, repeat=1 | 1 | 部分通过 |
| viewmodel_offset_z | 持枪位置：垂直 | cs2_user_convars_0_slot0.vcfg / config/convars | -1; B first=-1.5, repeat=-1 | -1 | 部分通过 |
| cl_crosshair_length | 准星线长 | cs2_user_convars_0_slot0.vcfg / config/convars | 4; B first=0, repeat=4 | 4 | 部分通过 |
| cl_crosshair_gap | 准星间距 | cs2_user_convars_0_slot0.vcfg / config/convars | 2; B first=8, repeat=2 | 2 | 部分通过 |
| cl_crosshair_thickness | 准星粗细 | cs2_user_convars_0_slot0.vcfg / config/convars | 1; B first=3, repeat=1 | 1 | 部分通过 |
| cl_crosshairsize | 经典准星线长 | cs2_user_convars_0_slot0.vcfg / config/convars | 未部署测试 | 未验证 | 待实机验证 |
| cl_crosshairgap | 经典准星间距 | cs2_user_convars_0_slot0.vcfg / config/convars | 未部署测试 | 未验证 | 待实机验证 |
| cl_crosshairthickness | 经典准星粗细 | cs2_user_convars_0_slot0.vcfg / config/convars | 未部署测试 | 未验证 | 待实机验证 |
| cl_crosshaircolor_r | 红色 | cs2_user_convars_0_slot0.vcfg / config/convars | 200; B first=0, repeat=200 | 200 | 部分通过 |
| cl_crosshaircolor_g | 绿色 | cs2_user_convars_0_slot0.vcfg / config/convars | 100; B first=255, repeat=100 | 100 | 部分通过 |
| cl_crosshaircolor_b | 蓝色 | cs2_user_convars_0_slot0.vcfg / config/convars | 50; B first=0, repeat=50 | 50 | 部分通过 |
| cl_crosshairalpha | 不透明度 | cs2_user_convars_0_slot0.vcfg / config/convars | 未部署测试 | 未验证 | 待实机验证 |
| cl_crosshairdot | 中心点 | cs2_user_convars_0_slot0.vcfg / config/convars | false; B first=true, repeat=false | false | 部分通过 |
| cl_crosshair_t | T 形准星 | cs2_user_convars_0_slot0.vcfg / config/convars | true; B first=false, repeat=true | true | 部分通过 |
| cl_crosshair_drawoutline | 描边 | cs2_user_convars_0_slot0.vcfg / config/convars | 1; B first=0, repeat=1 | 1 | 部分通过 |
| cl_crosshair_recoil | 跟随后坐力 | cs2_user_convars_0_slot0.vcfg / config/convars | true; B first=false, repeat=true | true | 部分通过 |
| cl_crosshair_sniper_width | 狙击准星宽度 | cs2_user_convars_0_slot0.vcfg / config/convars | 2; B first=1, repeat=2 | 2 | 部分通过 |
| sensitivity | 鼠标灵敏度 | cs2_user_convars_0_slot0.vcfg / config/convars | 1.23; B first=1.5, repeat=1.23 | 1.23 | 部分通过 |
| zoom_sensitivity_ratio | 开镜灵敏度倍率 | cs2_user_convars_0_slot0.vcfg / config/convars | 0.93; B first=0.8, repeat=0.93 | 0.93 | 部分通过 |
| cl_sniper_auto_rezoom | 狙击射击后自动开镜 | cs2_user_convars_0_slot0.vcfg / config/convars | true; B first=false, repeat=true | true | 部分通过 |
| setting.r_csgo_cmaa_enable | CMAA 抗锯齿 | cs2_video.txt / video.cfg | 1; later 0 | 1; later 0 | 部分通过 |
| setting.videocfg_shadow_quality | 全局阴影质量 | cs2_video.txt / video.cfg | 1 | 1 | 部分通过 |
| setting.videocfg_dynamic_shadows | 动态阴影 | cs2_video.txt / video.cfg | 0 | 0 | 部分通过 |
| setting.videocfg_texture_detail | 模型 / 纹理细节 | cs2_video.txt / video.cfg | 1 | 1 | 部分通过 |
| setting.r_texturefilteringquality | 纹理过滤模式 | cs2_video.txt / video.cfg | 4 | 4 | 部分通过 |
| setting.shaderquality | 着色器细节 | cs2_video.txt / video.cfg | 0 | 0 | 部分通过 |
| setting.videocfg_particle_detail | 粒子细节 | cs2_video.txt / video.cfg | 1 | 1 | 部分通过 |
| setting.videocfg_ao_detail | 环境光遮蔽 | cs2_video.txt / video.cfg | 0 | 0 | 部分通过 |
| setting.videocfg_hdr_detail | 高动态范围（HDR） | cs2_video.txt / video.cfg | 3 | 3 | 部分通过 |
| setting.videocfg_fsr_detail | FidelityFX Super Resolution | cs2_video.txt / video.cfg | 0; later 2 | 0; later 2 | 部分通过 |
| setting.r_low_latency | NVIDIA Reflex 低延迟 | cs2_video.txt / video.cfg | 未部署测试 | 未验证 | 待实机验证 |
| r_player_visibility_mode | 增强玩家对比度 | cs2_machine_convars.vcfg / config/convars | 0 | 0 | 部分通过 |
| setting.monitor_index | 显示器选择 | cs2_video.txt / video.cfg | 未部署测试 | 未验证 | 待实机验证 |
| setting.fullscreen | 显示模式 | cs2_video.txt / video.cfg | 未部署测试 | 未验证 | 待实机验证 |
| setting.aspectratiomode | 宽高比 | cs2_video.txt / video.cfg | 未部署测试 | 未验证 | 待实机验证 |
| setting.refreshrate_numerator | 刷新率 | cs2_video.txt / video.cfg | 未部署测试 | 未验证 | 待实机验证 |
| setting.cpu_level | CPU 自动检测等级 | cs2_video.txt / video.cfg | 未部署测试 | 未验证 | 待实机验证 |
| setting.gpu_level | GPU 自动检测等级 | cs2_video.txt / video.cfg | 未部署测试 | 未验证 | 待实机验证 |
| setting.gpu_mem_level | 显存自动检测等级 | cs2_video.txt / video.cfg | 未部署测试 | 未验证 | 待实机验证 |
| ui:render-scale | 渲染比例 / 画面锐化 | cs2_video.txt / video.cfg | 未部署测试 | 未验证 | 待实机验证 |
| ui:gsync | G-Sync / 驱动高级垂直同步 | cs2_video.txt / video.cfg | 未部署测试 | 未验证 | 待实机验证 |
| bind:a | 按键 a | cs2_user_keys_0_slot0.vcfg / config/bindings | +lookatweapon | +lookatweapon | 部分通过 |
| bind:b | 按键 b | cs2_user_keys_0_slot0.vcfg / config/bindings | +lookatweapon | +lookatweapon | 部分通过 |
| bind:c | 按键 c | cs2_user_keys_0_slot0.vcfg / config/bindings | +lookatweapon | +lookatweapon | 部分通过 |
| bind:d | 按键 d | cs2_user_keys_0_slot0.vcfg / config/bindings | +lookatweapon | +lookatweapon | 部分通过 |
| bind:e | 按键 e | cs2_user_keys_0_slot0.vcfg / config/bindings | +lookatweapon | +lookatweapon | 部分通过 |
| bind:f | 按键 f | cs2_user_keys_0_slot0.vcfg / config/bindings | +lookatweapon | +lookatweapon | 部分通过 |
| bind:g | 按键 g | cs2_user_keys_0_slot0.vcfg / config/bindings | +lookatweapon | +lookatweapon | 部分通过 |
| bind:h | 按键 h | cs2_user_keys_0_slot0.vcfg / config/bindings | +lookatweapon | +lookatweapon | 部分通过 |
| bind:i | 按键 i | cs2_user_keys_0_slot0.vcfg / config/bindings | +lookatweapon | +lookatweapon | 部分通过 |
| bind:j | 按键 j | cs2_user_keys_0_slot0.vcfg / config/bindings | +lookatweapon | +lookatweapon | 部分通过 |
| bind:k | 按键 k | cs2_user_keys_0_slot0.vcfg / config/bindings | +lookatweapon | +lookatweapon | 部分通过 |
| bind:l | 按键 l | cs2_user_keys_0_slot0.vcfg / config/bindings | +lookatweapon | +lookatweapon | 部分通过 |
| bind:m | 按键 m | cs2_user_keys_0_slot0.vcfg / config/bindings | +lookatweapon | +lookatweapon | 部分通过 |
| bind:n | 按键 n | cs2_user_keys_0_slot0.vcfg / config/bindings | +lookatweapon | +lookatweapon | 部分通过 |
| bind:o | 按键 o | cs2_user_keys_0_slot0.vcfg / config/bindings | +lookatweapon | +lookatweapon | 部分通过 |
| bind:p | 按键 p | cs2_user_keys_0_slot0.vcfg / config/bindings | +lookatweapon | +lookatweapon | 部分通过 |
| bind:q | 按键 q | cs2_user_keys_0_slot0.vcfg / config/bindings | +lookatweapon | +lookatweapon | 部分通过 |
| bind:r | 按键 r | cs2_user_keys_0_slot0.vcfg / config/bindings | +lookatweapon | +lookatweapon | 部分通过 |
| bind:s | 按键 s | cs2_user_keys_0_slot0.vcfg / config/bindings | +lookatweapon | +lookatweapon | 部分通过 |
| bind:t | 按键 t | cs2_user_keys_0_slot0.vcfg / config/bindings | +lookatweapon | +lookatweapon | 部分通过 |
| bind:u | 按键 u | cs2_user_keys_0_slot0.vcfg / config/bindings | +lookatweapon | +lookatweapon | 部分通过 |
| bind:v | 按键 v | cs2_user_keys_0_slot0.vcfg / config/bindings | +lookatweapon | +lookatweapon | 部分通过 |
| bind:w | 按键 w | cs2_user_keys_0_slot0.vcfg / config/bindings | +lookatweapon | +lookatweapon | 部分通过 |
| bind:x | 按键 x | cs2_user_keys_0_slot0.vcfg / config/bindings | +lookatweapon | +lookatweapon | 部分通过 |
| bind:y | 按键 y | cs2_user_keys_0_slot0.vcfg / config/bindings | +lookatweapon | +lookatweapon | 部分通过 |
| bind:z | 按键 z | cs2_user_keys_0_slot0.vcfg / config/bindings | +lookatweapon | +lookatweapon | 部分通过 |
| bind:0 | 按键 0 | cs2_user_keys_0_slot0.vcfg / config/bindings | +lookatweapon | +lookatweapon | 部分通过 |
| bind:1 | 按键 1 | cs2_user_keys_0_slot0.vcfg / config/bindings | +lookatweapon | +lookatweapon | 部分通过 |
| bind:2 | 按键 2 | cs2_user_keys_0_slot0.vcfg / config/bindings | +lookatweapon | +lookatweapon | 部分通过 |
| bind:3 | 按键 3 | cs2_user_keys_0_slot0.vcfg / config/bindings | +lookatweapon | +lookatweapon | 部分通过 |
| bind:4 | 按键 4 | cs2_user_keys_0_slot0.vcfg / config/bindings | +lookatweapon | +lookatweapon | 部分通过 |
| bind:5 | 按键 5 | cs2_user_keys_0_slot0.vcfg / config/bindings | +lookatweapon | +lookatweapon | 部分通过 |
| bind:6 | 按键 6 | cs2_user_keys_0_slot0.vcfg / config/bindings | +lookatweapon | +lookatweapon | 部分通过 |
| bind:7 | 按键 7 | cs2_user_keys_0_slot0.vcfg / config/bindings | +lookatweapon | +lookatweapon | 部分通过 |
| bind:8 | 按键 8 | cs2_user_keys_0_slot0.vcfg / config/bindings | +lookatweapon | +lookatweapon | 部分通过 |
| bind:9 | 按键 9 | cs2_user_keys_0_slot0.vcfg / config/bindings | +lookatweapon | +lookatweapon | 部分通过 |
| bind:SPACE | 按键 SPACE | cs2_user_keys_0_slot0.vcfg / config/bindings | +lookatweapon | +lookatweapon | 部分通过 |
| bind:CTRL | 按键 CTRL | cs2_user_keys_0_slot0.vcfg / config/bindings | +lookatweapon | +lookatweapon | 部分通过 |
| bind:SHIFT | 按键 SHIFT | cs2_user_keys_0_slot0.vcfg / config/bindings | +lookatweapon | +lookatweapon | 部分通过 |
| bind:ALT | 按键 ALT | cs2_user_keys_0_slot0.vcfg / config/bindings | +lookatweapon | +lookatweapon | 部分通过 |
| bind:TAB | 按键 TAB | cs2_user_keys_0_slot0.vcfg / config/bindings | +lookatweapon | +lookatweapon | 部分通过 |
| bind:ENTER | 按键 ENTER | cs2_user_keys_0_slot0.vcfg / config/bindings | +lookatweapon | +lookatweapon | 部分通过 |
| bind:BACKSPACE | 按键 BACKSPACE | cs2_user_keys_0_slot0.vcfg / config/bindings | +lookatweapon | +lookatweapon | 部分通过 |
| bind:ESCAPE | 按键 ESCAPE | cs2_user_keys_0_slot0.vcfg / config/bindings | +lookatweapon | +lookatweapon | 部分通过 |
| bind:F1 | 按键 F1 | cs2_user_keys_0_slot0.vcfg / config/bindings | +lookatweapon | +lookatweapon | 部分通过 |
| bind:F2 | 按键 F2 | cs2_user_keys_0_slot0.vcfg / config/bindings | +lookatweapon | +lookatweapon | 部分通过 |
| bind:F3 | 按键 F3 | cs2_user_keys_0_slot0.vcfg / config/bindings | +lookatweapon | +lookatweapon | 部分通过 |
| bind:F4 | 按键 F4 | cs2_user_keys_0_slot0.vcfg / config/bindings | +lookatweapon | +lookatweapon | 部分通过 |
| bind:F5 | 按键 F5 | cs2_user_keys_0_slot0.vcfg / config/bindings | +lookatweapon | +lookatweapon | 部分通过 |
| bind:F6 | 按键 F6 | cs2_user_keys_0_slot0.vcfg / config/bindings | +lookatweapon | +lookatweapon | 部分通过 |
| bind:F7 | 按键 F7 | cs2_user_keys_0_slot0.vcfg / config/bindings | +lookatweapon | +lookatweapon | 部分通过 |
| bind:F8 | 按键 F8 | cs2_user_keys_0_slot0.vcfg / config/bindings | +lookatweapon | +lookatweapon | 部分通过 |
| bind:F9 | 按键 F9 | cs2_user_keys_0_slot0.vcfg / config/bindings | +lookatweapon | +lookatweapon | 部分通过 |
| bind:F10 | 按键 F10 | cs2_user_keys_0_slot0.vcfg / config/bindings | +lookatweapon | +lookatweapon | 部分通过 |
| bind:F11 | 按键 F11 | cs2_user_keys_0_slot0.vcfg / config/bindings | +lookatweapon | +lookatweapon | 部分通过 |
| bind:F12 | 按键 F12 | cs2_user_keys_0_slot0.vcfg / config/bindings | +lookatweapon | +lookatweapon | 部分通过 |
| bind:MOUSE1 | 按键 MOUSE1 | cs2_user_keys_0_slot0.vcfg / config/bindings | +lookatweapon | +lookatweapon | 部分通过 |
| bind:MOUSE2 | 按键 MOUSE2 | cs2_user_keys_0_slot0.vcfg / config/bindings | +lookatweapon | +lookatweapon | 部分通过 |
| bind:MOUSE3 | 按键 MOUSE3 | cs2_user_keys_0_slot0.vcfg / config/bindings | +lookatweapon | +lookatweapon | 部分通过 |
| bind:MOUSE4 | 按键 MOUSE4 | cs2_user_keys_0_slot0.vcfg / config/bindings | +lookatweapon | +lookatweapon | 部分通过 |
| bind:MOUSE5 | 按键 MOUSE5 | cs2_user_keys_0_slot0.vcfg / config/bindings | +lookatweapon | +lookatweapon | 部分通过 |
| bind:MWHEELUP | 按键 MWHEELUP | cs2_user_keys_0_slot0.vcfg / config/bindings | +lookatweapon | +lookatweapon | 部分通过 |
| bind:MWHEELDOWN | 按键 MWHEELDOWN | cs2_user_keys_0_slot0.vcfg / config/bindings | +lookatweapon | +lookatweapon | 部分通过 |
| bind:UPARROW | 按键 UPARROW | cs2_user_keys_0_slot0.vcfg / config/bindings | +lookatweapon | +lookatweapon | 部分通过 |
| bind:DOWNARROW | 按键 DOWNARROW | cs2_user_keys_0_slot0.vcfg / config/bindings | +lookatweapon | +lookatweapon | 部分通过 |
| bind:LEFTARROW | 按键 LEFTARROW | cs2_user_keys_0_slot0.vcfg / config/bindings | +lookatweapon | +lookatweapon | 部分通过 |
| bind:RIGHTARROW | 按键 RIGHTARROW | cs2_user_keys_0_slot0.vcfg / config/bindings | +lookatweapon | +lookatweapon | 部分通过 |
