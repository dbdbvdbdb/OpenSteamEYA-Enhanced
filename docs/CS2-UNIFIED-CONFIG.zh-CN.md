# CS2 统一配置：设计、参数依据与验收

本改造基于 v0.2.8，位于独立功能分支，尚未合并或发布。它把原先的逐账号编辑改为一套独立模板；“CS2 配置查看”继续按账号只读查看所有槽位。

## 使用方式

1. 进入“CS2 统一配置”，没有 Steam 或本机账号也可以编辑。首次创建应用默认模板，以后读取已经保存的模板。
2. “导入模板”可使用应用默认配置、导入包含原生文件的文件夹，或从一个本机账号复制一次。导入替换前确认并备份旧模板，不与来源账号持续绑定。文件夹导入为平铺导出目录或 `730/local/cfg`，不递归扫描不相关文件。
3. 在视频、音频、游戏、HUD、准星、鼠标键盘及键位绑定分类中编辑。分类和搜索不丢失当前草稿；无效输入即时标记并禁止保存。悬停名称可查看英文名称、原始键、文件/节点、依据和兼容版本。
4. “保存配置”仅提交模板，不修改任何账号。未保存的编辑可撤销；关闭应用会丢弃未保存修改。
5. “导出模板”将四种原生文件和 JSON 清单复制到空文件夹；在另一台电脑可通过文件夹导入恢复。带显示设备绑定的模板需重新校验本机设备。
6. 关闭 CS2，再点“应用到所有账号”。预览列出账号数量、实际会更改的文件、失败和跳过原因。确认后逐账号执行，显示进度，结束后提供成功、无需更改、跳过、失败及备份位置。取消确认不写入任何账号。
7. “恢复备份”可恢复旧模板、此次批量应用前的账号配置，或 v0.2.8 原有账号备份。恢复账号备份还会备份恢复前的内容；外部修改导致内容不符时拒绝覆盖。

本页不调用 Steamworks 或 `ForcePush`，不读写 `remote` 镜像，不处理 Steam Token/密码。原“设置 → CS2 设置同步”仍是独立功能。Steam Cloud 之后可能下载旧配置覆盖本地文件，本地应用成功不等于云同步成功。

## 存储和事务

```text
%LocalAppData%/OpenSteamEYA-Enhanced/cs2-config/
  template.json
  template-files/<revision>/
    cs2_user_convars_0_slot0.vcfg
    cs2_user_keys_0_slot0.vcfg
    cs2_machine_convars.vcfg
    cs2_video.txt
  backups/
    templates/<backup-id>/template.json
    accounts/apply-<time>-<id>/<AccountID>/
      manifest.json
      <本次覆盖前的原生文件>
    accounts/apply-<time>-<id>/results.json
```

`template.json` 记录版本、名称、不可变 generation、文件 SHA-256、参数依据版本、本地作用域以及可选的已验证显示设备绑定。每次保存先写全新的 generation，最后原子替换清单，避免多文件保存只完成一半；旧 generation 保留供模板备份引用，不自动删除。保存采用进程内闸门和文件独占锁，并校验外部修改。

旧备份仍位于 `%AppData%/SteamEYA/cs2-settings-backups/`，不会迁移或删除。模板、CS2 备份和账号管理 ZIP 备份相互独立。导出目录包含完整原生文件；模板在应用中逻辑统一，未将不同原生格式强行拼成 cfg 脚本。

批量目标限定为指定 Steam 的规范十进制账号目录，且已存在 `userdata/<AccountID>/730`。只写 `local/cfg` 的上述四种文件；其他 slot、remote、凭据、其他游戏和未知文件不写入。目标的未知参数、硬件 ID 和未覆盖的自定义绑定保留。模板的未知字段和自定义命令保留在导出中，但批量部署只复制已验证参数和白名单标准绑定动作，避免用来源账号的未知字段覆盖目标。可以独立编辑验证过的标准按键。旧版 `<unbound>` 哨兵的原生持久化语义尚未核实，统一页暂不提供该动作，导入后也不向账号传播。

用户/机器参数和键位文件缺失时按已核实的 `config/convars`、`config/bindings` 结构创建。视频文件必须由游戏初始化且目标 `Version=16`，缺失或其他版本时跳过该文件并报告，其他安全文件仍可应用。单文件上限 2 MiB，拒绝异常结构、重复/大小写重复键、链接、路径逃逸和 NTFS 替代数据流。UTF-8、带 BOM UTF-8、UTF-16 LE/BE 的字节序、注释、未知字段及未修改的文本保持原样；已存在的 `$N` 参数后缀按各目标自身的键名保留。

应用预览冻结文件快照；写入前检查游戏、只读属性和内容冲突，写前备份原始字节及前后哈希。每个文件使用临时文件、落盘和同目录原子替换。中途失败按相反顺序回滚该账号；若文件又被外部改变，保留外部内容并报告无法回滚及备份路径。其他账号的备份和处理不受影响。恢复可删除本事务新建且哈希仍匹配的配置文件，不删除目录或不相关文件。

## 参数核实依据

核对的游戏内容快照：SteamTracking/GameTracking-CS2，提交 **5ab573fa8afb5b8db98869d79ed79399fca6e747**（2026-10-15），`ClientVersion=2000933`、`PatchVersion=1.41.9.1`（同快照 `game/csgo/steam.inf`）。读取的是客户端数据和参数声明；没有在真实 CS2 客户端中逐项操作/验证画面效果，不能把源码核对等同于实机验收。

- [视频菜单定义](https://github.com/SteamTracking/GameTracking-CS2/blob/5ab573fa8afb5b8db98869d79ed79399fca6e747/game/csgo/pak01_dir/panorama/layout/settings/settings_video.xml)：客户端公开的控件、档位和亮度范围。
- [视频格式版本](https://github.com/SteamTracking/GameTracking-CS2/blob/5ab573fa8afb5b8db98869d79ed79399fca6e747/game/csgo/pak01_dir/cfg/video_version.txt)：版本 16。
- [低画质配置](https://github.com/SteamTracking/GameTracking-CS2/blob/5ab573fa8afb5b8db98869d79ed79399fca6e747/game/csgo/pak01_dir/cfg/video_defaults_0.txt)、[高画质配置](https://github.com/SteamTracking/GameTracking-CS2/blob/5ab573fa8afb5b8db98869d79ed79399fca6e747/game/csgo/pak01_dir/cfg/video_defaults_2.txt)、[非常高画质配置](https://github.com/SteamTracking/GameTracking-CS2/blob/5ab573fa8afb5b8db98869d79ed79399fca6e747/game/csgo/pak01_dir/cfg/video_defaults_3.txt)：原生视频键名和相应值。
- [参数声明](https://github.com/SteamTracking/GameTracking-CS2/blob/5ab573fa8afb5b8db98869d79ed79399fca6e747/DumpSource2/convars.txt)：默认值、上下限、归档/per_user/隐藏标志。普通归档参数映射机器文件，per_user 参数映射用户文件。

“已验证可编辑”表示键、节点、档位已由上述当前游戏内容交叉核对，单元测试覆盖文本读写；不表示已实测所有 GPU/显示器。客户端更新后需重新核对快照和视频版本。模板中的高画质参考值来自 `video_defaults_2`，是明确标识的应用模板选择，不声称为每台硬件的游戏默认/推荐值。

## 视频完整性核对表

| 游戏选项 | 原生映射 | 本分支状态 / 范围 |
| --- | --- | --- |
| 显示模式 | fullscreen / nowindowborder / coop_fullscreen 组合 | 待核实原生控件完整组合，保留导入值，不开放编辑 |
| 宽高比 | 原生 AspectRatioEnum 与显示模式联动 | 待核实持久化映射；不强行写入菜单序号 |
| 分辨率 | video.cfg 的已有 defaultres/defaultresheight | Windows `EnumDisplayDevicesW/EnumDisplaySettingsExW` 实际枚举；仅在已有整数刷新率可核实且设备支持该分辨率/刷新率组合时成对修改已有键，逐账号刷新率不同时跳过。缺字段或分数/自动刷新率需先在游戏内处理 |
| 显示器选择 | monitor_index | 只显示 Windows 实际设备用于校验模式，不猜测 CS2 编号 |
| 刷新率 | numerator/denominator | 原生控件负责分数刷新率，当前仅枚举驱动模式，保留导入值、不开放写入 |
| 亮度/Gamma | machine/convars: r_fullscreen_gamma | 可编辑 1–4，声明默认 2.2；不把百分比当 Gamma 写入 |
| 垂直同步 | video.cfg: setting.mat_vsync | 可编辑 0/1；不提供未验证的驱动高级模式 |
| 游戏/菜单最大帧率 | machine/convars: fps_max / fps_max_ui | 可编辑 0–1000（应用范围；0 不限）；默认 120/0 |
| 增强玩家对比度 | machine/convars: r_player_visibility_mode | 可编辑 0/1 |
| NVIDIA Reflex | setting.r_low_latency | 菜单 0/1/2 已核对，但驱动能力未可靠检测，暂不可编辑 |
| MSAA | setting.msaa_samples | 可编辑 0/2/4/8 |
| CMAA | setting.r_csgo_cmaa_enable | 可编辑 0/1（当前游戏布尔参数） |
| 全局阴影质量 | setting.videocfg_shadow_quality | 可编辑 0/1/2/3 |
| 动态阴影 | setting.videocfg_dynamic_shadows | 可编辑 0 仅太阳光 / 1 全部 |
| 模型/纹理 | setting.videocfg_texture_detail | 可编辑 0/1/2 |
| 纹理过滤 | setting.r_texturefilteringquality | 可编辑 0–5：双线性、三线性、2/4/8/16× |
| 着色器细节 | setting.shaderquality | 可编辑 0/1 |
| 粒子细节 | setting.videocfg_particle_detail | 可编辑 0/1/2/3 |
| 环境光遮蔽 | setting.videocfg_ao_detail | 可编辑 0 关闭 / 2 中 / 3 高；不添加不存在的档位 1 |
| HDR | setting.videocfg_hdr_detail | 可编辑 3 性能 / -1 质量；不是 Windows HDR 开关 |
| FSR | setting.videocfg_fsr_detail | 可编辑 0 关闭 / 1 超高质量 / 2 质量 / 3 平衡 / 4 性能 |
| G-Sync、驱动低延迟 | 原生/驱动接口 | 不支持本次离线修改，不生成虚假配置键 |
| 缩放、窗口全屏行为 | 原生控件及操作系统 | 映射尚未完整确认，保留原值 |
| 锐化、独立渲染比例 | 部分是 developmentonly 参数 | 不以开发参数冒充普通用户选项，不开放写入 |
| CPU/GPU/显存自动等级及 VendorID/DeviceID | 自动检测字段 | 只保留，不改写来源硬件信息到其他账号 |
| 图形预设 | 游戏原生控件联动多个字段 | 不伪造单一“预设”键；以独立、经过核对的设置呈现 |
| 预览放大镜 | 仅设置页面的图形预览 | 不当作游戏渲染参数开放 |

为避免猜测游戏原生控件的 FSR/AA 联动，应用采用保守策略：启用 FSR 前需明确把 MSAA 和 CMAA 设为关闭。不将该策略声称为游戏引擎的完整能力矩阵。

旧目录中的 `cl_crosshairgap` 在快照中未声明，经典 size/thickness/alpha 已隐藏；统一页不开放这些旧/隐藏参数。其他类别依据当前声明的合法范围补充，并显示中文说明、英文名、类型、默认参考及文件节点。未知参数保留但不会进入可写白名单。

## 验证与剩余人工验收

本地 Linux 使用 .NET SDK 10.0.401 运行控制台断言（非 `dotnet test`）。测试含四种编码、缺失参数、非法枚举/数值、模板持久化与冲突、导入导出、模板/账号恢复、100 账号应用、只读、损坏、游戏启动中断、写入失败回滚、符号链接和云/身份哨兵不变。精确结果在 PR 中随实际运行记录更新。

Windows CI 使用真实 WinUI 和隔离合成目录，检查无账号编辑、保存与应用分离、草稿保持、无效输入、确认取消、批量应用、结果、备份和深浅主题截图；生产包排除 QA 入口。截图与断言作为 CI 工件供 PR 审阅，不能代替各系统 DPI 实测。

未完成的实机项目：当前游戏客户端逐项设置/重启效果、不同 GPU/驱动的 AA/FSR/Reflex 条件、显示模式完整映射、分数刷新率、外接/热插拔显示器、多系统 DPI、真实 Steam Cloud 回写场景，以及断电级崩溃注入。上述未确认项不作为可编辑参数开放，也不声称已全部覆盖游戏客户端。
