# v0.2.9 CS2 配置真实生效性与兼容性验证报告

审查基线：`v0.2.9`，提交 `453ddafad244b0cc6af7a3a92f3013abc2451375`。验证分支：`validation/cs2-config-feasibility-v0.2.9`。本报告随证据更新，不代表全部游戏验收已完成。

## 1. 执行摘要

目前完成了静态审查、隔离部署，以及两个明确授权测试账号的真实启动、菜单/控制台读取、正常退出和重启检查。43 个可编辑 convar 全部返回测试值；12 个可编辑视频字段的测试档位由原生菜单识别；67 个按键逐键查询返回测试绑定，重启后仍保留。**这些证据证明配置读取及保留，不等同于全部实际渲染、声音或操作效果已通过。**

独立工具直接链接当前生产配置代码，664 项隔离断言通过。其中包含两项“成功复现缺陷”的断言，不能将这个数量解释为 664 项行为都正确。另含三个证据门槛反例测试：文件证据、缺少重启观察、缺失证据文件均不能被工具接受为游戏通过。现有 Native AOT 数据回归 280 项通过，仅使用虚构账号。生产登录、密码格式、模板及部署实现均未修改。

当前总体状态为 **122 项部分通过、16 项待实机验证，0 项完整效果验证通过**。没有把解析测试或数值查询升级为效果通过，也没有证据将已开放参数断言为游戏不支持。已复现两个文件处理缺陷，并确认 autoexec 覆盖、原生 FSR/MSAA 组合与应用约束不一致、无边框模式刷新率条件等实际兼容边界。第二个专用账号首次启动曾重写20个用户级参数，重复部署后43个convar均读取目标值并正常退出保留；其最后一轮重启查询因中断未完成。Cloud 关闭/开启及远端回下载尚未实测。

第一账号本轮已结束，7 个选定本地配置文件已恢复备份并逐一核对 SHA-256；第二账号6个选定本地文件也已恢复并核对散列。共享 autoexec 已恢复原名并核对原散列。中断后第一账号出现24项后续配置变化，未确认来源，没有用旧备份覆盖这些变化。恢复前另有安全备份。**仅确认本地恢复，未确认远端 Cloud 已恢复。** 未进入竞技对局、未注入游戏、未修改 Steam 登录凭据。

## 2. 环境及证据范围

| 项目 | 实际观察 |
| --- | --- |
| 系统 | Windows 11 x64，build 26200；Windows 10 未测 |
| 工具链 | .NET SDK 10.0.401，Windows x64 Native AOT 可构建 |
| Steam / CS2 | 安装存在；用户手动启动后存在 CS2 进程，已观察到真实设置菜单 |
| 客户端 | ClientVersion 2000933，Patch 1.41.9.1，2026-10-15；Steam build 25966348 |
| GPU | NVIDIA RTX 5070 Laptop GPU / AMD Radeon 610M，另有虚拟显示适配器 |
| 测试账号 | 两专用账号均核对并执行测试，选定本地配置已恢复；第二账号最终重启查询未完成。公共证据以 DedicatedTestA/B 标记 |
| 多账号 | 两个虚构账号隔离部署已测；两个真实账号分时部署同一55项模板，第二账号首次重写、重复部署后读取目标 |
| 显示环境 | 初期菜单为 S2719DGF / 144 Hz；后续会话为 Generic PnP Monitor / 240 Hz，截图逻辑尺寸也变化。没有将两阶段画面直接作渲染或物理刷新率 A/B 证据 |
| Steam Cloud | 游戏控制台出现 RemoteStorage 保存成功日志；不等同于服务器上传已完成。未切换云开关、未验证旧云配置回下载或远端恢复 |

读取了计划列出的八个文件及模板模型、本地目录发现、页面事件和现有测试。固定参考修订为 [`SteamTracking/GameTracking-CS2@5ab573f`](https://github.com/SteamTracking/GameTracking-CS2/tree/5ab573fa8afb5b8db98869d79ed79399fca6e747)。它是客户端提取数据，不是 Valve 的运行时兼容承诺；本机客户端版本恰与参数目录目标一致。

本机 `pak01_dir.vpk` 索引 SHA-256：`f0943b4d5a86fe8f4891ab98983b9d132b4450b9b7401a03a530bcd63f293f99`。只读提取了四个原生画质预设、video_version 及编译菜单资源。可复核摘要见 [client-evidence.json](cs2-feasibility/client-evidence.json)。其中 `extractorLaunchesGame=false` 表示提取器不执行游戏，不描述后续人工游戏会话。

## 3. 完整参数映射与状态

完整清单包含 **71 个显示项目（55 个普通可编辑项目、2 个条件分辨率字段及其余保留项目）和 67 个可绑定按键，共 138 行**，不是只列常见设置。

- [完整实机状态表（Markdown，138 行）](cs2-feasibility/runtime-parameter-results.md)
- [完整实机状态表（CSV）](cs2-feasibility/runtime-parameter-results.csv)：包含范围、游戏观察值、重启值、状态、证据及每项限制。
- [静态映射表（Markdown）](cs2-feasibility/parameter-mapping.md)、[静态映射表（CSV）](cs2-feasibility/parameter-mapping.csv)：保留生成时的静态状态，含参考值、插入规则、重启声明等；最终实机状态以以上合并表为准。
- [生成目录及静态检查](cs2-feasibility/catalog.json)
- [43 个 convar 声明和归档作用域逐项核对](cs2-feasibility/convar-reference-audit.json)

源码 `SettingAvailability.Verified` 表示应用允许编辑的白名单，**不等于本报告的“验证通过”**。[逐项观察记录](cs2-feasibility/runtime-observations.json)只将已实际查询/查看并重启的 122 项标为部分通过。所有枚举档位、数值边界、动作组合均未穷举；单个测试值成功不能推出整个范围有效。没有证据支持将某个已开放参数无条件断言为“只能写入但不能生效”。

| 文件 | 读写节点 | 处理原则 |
| --- | --- | --- |
| cs2_user_convars_0_slot0.vcfg | config/convars | per_user 字段；只处理槽位 0 |
| cs2_machine_convars.vcfg | config/convars | archive 且非 per_user 字段 |
| cs2_user_keys_0_slot0.vcfg | config/bindings | 67 个按键及动作白名单；目标自定义命令保留，模板自定义命令不部署 |
| cs2_video.txt | video.cfg | 仅 Version=16；目标缺失或版本未知时跳过，不强行初始化硬件配置 |

模板缺失且 AllowInsert=true 的字段可插入；目标已有 `$N` 后缀时保留目标后缀，缺失时写入无后缀键。是否由游戏读取新增无后缀键必须逐项实测。重复版本键会拒绝处理，不能任取一个覆盖。非 slot0 配置不在当前工具读写范围内。

## 4. 视频专项

下表保留逐项效果验收方法。已执行的档位/数值和重启结果见本节后面的实际结果表；未取得客观效果证据的项目继续部分通过。所有档位和范围见完整映射表。

| 项目 / 键 | 当前可写能力 | 游戏内及客观验证步骤 |
| --- | --- | --- |
| 显示模式 / setting.fullscreen | 禁用、保留 | 另需核对 nowindowborder/coop_fullscreen 等关联字段；以菜单、窗口边框、实际输出确认，不能仅改单键 |
| 显示器 / setting.monitor_index | 禁用、保留 | 对照 Windows 输出和游戏监视器名称；Windows device 名不能直接转换为游戏索引 |
| 宽高比 / setting.aspectratiomode | 禁用、保留 | 对照菜单、输出宽高及黑边/拉伸；验证与分辨率组合 |
| 分辨率宽高 / setting.defaultres、setting.defaultresheight | 组合控件条件允许 | 必须已有 video16、整数保存刷新率、驱动枚举匹配；对照菜单及实际渲染/窗口输出尺寸，至少重启一次 |
| 刷新率 / setting.refreshrate_numerator | 禁用、保留 | 同时记录 denominator、显示器模式、游戏模式和输出；不能只看菜单数字 |
| Gamma / r_fullscreen_gamma | 可写 | 全屏下对照亮度菜单及固定画面的明暗；窗口模式下数值变化不能证明视觉变化 |
| V-Sync / setting.mat_vsync | 可写 | 菜单开关、实际刷新与帧时间对照；排除驱动强制覆盖 |
| FPS 上限 / fps_max | 可写 | 控制台查询；相同离线场景记录实际帧率，不能仅查询到 137 就声称限帧生效 |
| 菜单 FPS / fps_max_ui | 可写 | 主菜单控制台值和帧率统计；识别后台限帧、V-Sync、硬件瓶颈 |
| 玩家对比度 / r_player_visibility_mode | 可写 | 菜单与相同离线场景、固定视角的人物轮廓对照 |
| MSAA / setting.msaa_samples | 可写 | 游戏菜单档位、重启、固定边缘的图像比较；同时记录 CMAA 和 FSR |
| CMAA / setting.r_csgo_cmaa_enable | 可写 | 菜单联动、控制变量边缘比较，记录与 MSAA 的优先级 |
| 全局阴影 / setting.videocfg_shadow_quality | 可写 | 菜单档位、固定离线场景阴影分辨率和距离 |
| 动态阴影 / setting.videocfg_dynamic_shadows | 可写 | 菜单范围及固定局部光源阴影对照 |
| 模型/纹理 / setting.videocfg_texture_detail | 可写 | 菜单、重启资源、固定材质清晰度对照 |
| 纹理过滤 / setting.r_texturefilteringquality | 可写 | 菜单 0..5 与倾斜表面纹理对照 |
| Shader / setting.shaderquality | 可写 | 菜单、资源重载、固定着色表面对照 |
| 粒子 / setting.videocfg_particle_detail | 可写 | 菜单及固定离线粒子场景，不能用随机对局主观判断 |
| AO / setting.videocfg_ao_detail | 可写 | 菜单 0/2/3 与固定遮蔽接触区域对照 |
| HDR / setting.videocfg_hdr_detail | 可写 | 菜单质量/性能与固定画面；不等同 Windows HDR 开关 |
| FSR / setting.videocfg_fsr_detail | 可写 | 菜单 0..4、实际画质/渲染比例、AA 联动及重启持久化 |
| Reflex / setting.r_low_latency | 硬件相关、禁用 | 此机有 NVIDIA 显卡仍不能据此认定可用；需确认实际渲染 GPU、菜单及延迟状态 |
| CPU/GPU/显存自动检测等级 | 禁用、保留 | 先查自动检测重建与 Version 变化；不允许凭旧经验解锁 |
| 渲染比例、G-Sync / ui:* | 纯 UI 占位、禁用 | 没有原生映射，不写入配置；G-Sync 需驱动/显示器证据，不视为已实现 |

### 本轮实际视频结果

| 参数 | 实际测试与游戏结果 | 当前结论 / 尚缺证据 |
| --- | --- | --- |
| fps_max / fps_max_ui | 先 137/47，后 141/51；控制台和菜单读取，正常退出及重启保留。目标已有 fps_max_ui$2 后缀，仍正确读取 | 部分通过；临时 cl_showfps 观察出现 49/62/93 等不同页面帧率，不能认定菜单 47 的实际限帧已通过。cl_showfps 已恢复原 0 |
| Gamma | 2.2→2.3；控制台 2.3，菜单亮度 93%→90%，重启读取仍 2.3 | 部分通过；当前全屏窗口模式，缺少固定图像亮度测量及独占全屏对照 |
| 玩家对比度 / V-Sync | true→false / 0→1；菜单分别禁用/启用，重启保留 | 部分通过；缺少轮廓及帧时间测量 |
| MSAA / CMAA | 原 4× MSAA→2×；后 MSAA0/CMAA1，菜单 CMAA2，重启保留 | 部分通过；缺少固定边缘量化比较 |
| 阴影 / 动态阴影 | 2→1（中），1→0（仅限日光）；菜单及重启匹配 | 部分通过；缺少固定局部光源对照。曾观察动态阴影写入警告，但这次离线修改能被菜单识别并保留，不能据警告判为不支持 |
| 纹理 / 过滤 | 纹理2→1（中），过滤3→5（16×）→4（8×）；菜单及重启匹配 | 部分通过；没有量化斜面纹理清晰度 |
| Shader / 粒子 | 1→0（低）/2→1（中）；菜单及重启匹配 | 部分通过；缺少受控着色/粒子场景 |
| AO / HDR | 2→0（关闭）/-1→3（性能）；菜单及重启匹配 | 部分通过；缺少固定遮蔽及 HDR 渲染效果比较 |
| FSR | 0→2，MSAA0/CMAA0；菜单品质档位匹配。随后用原生菜单改 MSAA2，FSR2 不被清除，正常退出和重启菜单均保留 | 部分通过；不能据两个菜单值推断两种算法同时执行 |
| 分辨率宽高 | 原生菜单 2560×1440。全屏窗口模式的保存刷新率为 0/0，应用要求正整数刷新率，条件不满足 | 待实机验证；未通过应用部署分辨率，截图逻辑尺寸不能充当实际输出尺寸 |
| 显示模式、显示器、宽高比、刷新率、Reflex、G-Sync、自动检测等级 | 只读观察；应用未开放这些修改 | 待实机验证；没有解锁、没有修改驱动或系统设置 |

证据：[首次完整高级页](cs2-feasibility/screenshots/batch-all-advanced-bottom.png)、[重启高级页](cs2-feasibility/screenshots/batch-restarted-advanced-bottom.png)、[FSR 与 MSAA 原生组合](cs2-feasibility/screenshots/fsr-msaa-native-combination.png)、[组合重启](cs2-feasibility/screenshots/fsr-msaa-restarted.png)。

本机原生中档预设亦含 MSAA2/CMAA0/FSR2。现在除资源证据外，已有原生菜单设置、保存、重启保留证据，支持重新评审应用的保守禁用规则；尚不支持直接宣称渲染算法互斥或全部并行生效。

## 5. 音频、HUD、准星及键鼠逐项验收方法

对完整清单中每一个可写项目分别执行，不能用一项代表一类。记录修改值、菜单/控制台值、实际效果及重启值，分别保留证据。

| 类别 | 游戏识别 | 客观行为及持久化 |
| --- | --- | --- |
| 主/游戏/语音/音乐及提示音 | 查询各原始 convar，对应菜单滑块/开关 | 固定声音来源分别比较输出；MVP/回合提示需相应离线事件，不能主菜单听到音乐就验收所有音频项 |
| HUD、雷达、物品栏 | 对应菜单及控制台值 | 固定离线地图截图量取雷达/HUD 尺寸和中心、旋转/计分板行为；重启后重测 |
| viewmodel FOV/偏移 | 查询 per_user 参数 | 固定武器、视角、分辨率比较屏幕位置；识别预设覆盖 |
| 匹配 ping 等 | 查询值及菜单 | 不自动匹配竞技对局；没有实际筛选行为证据时仅记录数值读取，不宣称行为验收通过 |
| 准星长度/间距/厚度/颜色/点/T/描边/跟随/狙击宽度 | 逐项查询及菜单/预览 | 固定分辨率量取像素和 RGB，区分动态状态；后坐力等由用户在离线环境人工验证，无竞技自动输入 |
| 鼠标灵敏度/开镜/自动再开镜 | 查询及菜单 | 固定 DPI、移动距离和转角；开镜用相同武器条件；主菜单数值只算识别证据 |
| 全部按键及自定义覆盖 | 游戏 bind 查询、菜单 | 人工离线触发动作；保留原自定义命令，逐键确认冲突和重启后的绑定 |

隐藏旧准星参数及未支持项继续保留，不把“应用未开放”误报为“游戏不支持”。模板中默认值是应用参考值，不是当前账号的原值或硬件推荐。

本轮 43 个 convar 均经两组只读控制台查询和重启再查询，实际返回值见逐项表。菜单另观察到主音量 0.36 显示 60%、语音 0.49 显示 70%、音乐 0.25 显示 50%，回合开始/结束/MVP/十秒提示分别显示 20%/30%/40%/50%。原生音频控件使用 audiogain，不能把原始增益直接当作线性百分比。[原生音频布局来源](https://github.com/SteamTracking/GameTracking-CS2/blob/5ab573fa8afb5b8db98869d79ed79399fca6e747/game/csgo/pak01_dir/panorama/layout/settings/settings_audio.xml)。未录制/量测声音输出，也未触发所有回合事件。

HUD 0.95、雷达大小 0.90/缩放 0.40、匹配上限 80、灵敏度 1.23/开镜 0.93 均反映到相应菜单。准星原生样式为静态圆形，颜色色块随 RGB200/100/50 改变，粗细1、间隙2、无中心点在菜单可见；不能据此验收传统线长、T形、后坐力和狙击效果。67 个键位统一部署 +lookatweapon，仅逐键查询绑定读取和重启保留，**没有触发 67 次动作，也没有验证全部白名单动作的实际行为**。

## 6. 模板、部署、快照和 Cloud

隔离流程已实际执行：生产代码创建模板 → 每个可编辑参数改值并保存 → 重新加载比对 → 两个不同虚构账号部署 → 校验目标后缀、未知键和 remote 哨兵 → 事务恢复。绑定另逐键及逐动作检查。测试日志见 [isolated-results.json](cs2-feasibility/isolated-results.json)。

真实会话首次正常退出仍无 cs2_video.txt，之后通过游戏原生 MSAA 菜单修改并保存才生成 video16；随后恢复原 4× 档位并备份该初始化状态。应用不会初始化缺失 video，因此新账号视频部署具有前置条件。

实际部署使用本地忽略目录内的专用测试驱动，直接链接 v0.2.9 的 UnifiedConfigStore/ConfigApplyService，执行模板创建、修改保存、预览、筛选唯一已授权账号、Apply。没有点击正式 WinUI“应用到所有账号”按钮，也没有完成这段 WinUI UI 的端到端验收。批次：四项 convar 对照；六项 convar/视频控制组；全部55设置；FSR与67键位。另将同一份已保存55项模板按唯一账号筛选后分时部署到第二账号；不是同时点击WinUI批量按钮。第二账号最后重启查询未完成。

| 阶段 | 可复核证据 / 实际差异 |
| --- | --- |
| 首次四项部署 | 文件写入 fps_max137、fps_max_ui47、sensitivity1.37、volume0.43；游戏返回原400/63/1.5/1。共享 autoexec 明确重设这四项 |
| 停用 autoexec 对照 | 用户确认后备份并改名共享文件；相同四值由游戏读取，另含 MSAA2/过滤5。游戏退出后无白名单值差异，重启查询保留四值 |
| 全部55设置 | [应用后](cs2-feasibility/runtime-phases/all-settings/applied.json)、[启动后](cs2-feasibility/runtime-phases/all-settings/started.json)、[退出后](cs2-feasibility/runtime-phases/all-settings/exited.json)、[重启](cs2-feasibility/runtime-phases/all-settings/restarted.json)；退出及重启的白名单值差异均0。文件散列变化不等同于参数值变化 |
| FSR/键位 | [阶段文件](cs2-feasibility/runtime-phases/fsr-bindings/applied.json)、[退出差异](cs2-feasibility/runtime-phases/fsr-bindings/diff-exit.json)；MSAA0→2 是明确的原生菜单测试操作，不是自动回退。绑定字符串未纳入快照，另有6张逐键查询/重启截图 |
| 本地恢复 | [恢复核对结果](cs2-feasibility/runtime-phases/restore-result.json)、[恢复后快照](cs2-feasibility/runtime-phases/restored.json)。7个指定文件与备份散列一致；视频恢复到原生初始化后原4×状态。没有声称目录所有文件与首次启动前完全相同 |

第二账号证据见 [多账号摘要](cs2-feasibility/multi-account-runtime-summary.json)、[首次启动20项差异](cs2-feasibility/runtime-phases/test-b-first/diff-start.json)、[重复部署启动](cs2-feasibility/runtime-phases/test-b-repeat/started.json)、[退出零值差异](cs2-feasibility/runtime-phases/test-b-repeat/diff-exit.json)和[本地恢复](cs2-feasibility/runtime-phases/restore-result-b.json)。第二次即时应用后快照漏采，比较使用第一次应用后快照作为同模板目标参考，不能冒充完整的第二轮应用时点证据。最后重启仅观察到游戏启动并输入查询，未完成结果读取，故无第二账号重启通过结论。

第二账号第一次运行时，用户文件20项被重写，键位文件也重新生成；原autoexec当时已临时停用。正常退出后用完全相同的模板再次部署，43项真实控制台查询均返回目标，正常退出值差异0。未改变云开关，不能区分Cloud回填、客户端初始化或其他加载/写入机制；也不能将其归为错误SteamID。第一账号在第二账号最初部署期间文件散列未变；跨中断后第一账号的24项变化另作边界记录，不宣称全程目录不变。

公开阶段快照只含白名单参数与散列；原始账号文件、路径和未知自定义命令保留在忽略目录。首次对照完整原始日志不公开，不能据公共摘要重构整个账号配置。

统一模板路径和账号 local/cfg 是分开的。代码只部署白名单设置，保留未知内容；不会因 Steam 当前登录账号不同而只选择当前账号，它会扫描所有已有 730 的 userdata。因此专用测试时禁止直接用“应用到所有账号”按钮操作真实多账号目录。

v0.2.9 此页面保存和部署均明确不推送 Steam Cloud，不能把本地部署成功解释为云端成功。实际游戏控制台出现保存 user_convars/keys 到 RemoteStorage 成功及本地 _lastclouded 更新；这最多支持客户端保存流程被调用，不证明服务器内容或后续回下载。Steam Cloud 的上传下载行为须单独验证；官方说明见 [Steam Cloud 文档](https://partner.steamgames.com/doc/features/cloud)。没有删除远端数据。后续需分别记录 Cloud 关闭、本地修改、开启首次启动、正常退出、切号及旧云文件回下载的散列与时间；本轮本地恢复也不代表远端恢复。

## 7. 已复现问题与建议

### P1 · DISPLAY-01：模板原始分辨率与清单不一致仍被接受

复现：隔离 video16 文件含 1280×720@60；清单声明同一合法模拟显示器 1920×1080@60。预览未拒绝，部署内容变为 1920×1080。证据：[display-manifest-probe.json](cs2-feasibility/display-manifest-probe.json)。

原因：预览检查清单在当前枚举模式中、刷新率相同，却未比对清单宽高和原生文件宽高。建议在模板加载/预览中检查二者一致，冲突时拒绝部署并要求重选。此为元数据完整性问题，不证明分辨率运行时失效。

### P1 · MERGE-01：部分模板合并后没有重新校验组合

复现：模板仅含 MSAA=4，目标含 FSR=2、MSAA=0、CMAA=0。模板本身通过检查，合并结果为 FSR=2、MSAA=4；对合并结果调用应用自身的组合校验会抛错，部署预览却接受。证据：[partial-video-merge-probe.json](cs2-feasibility/partial-video-merge-probe.json)。

原因：组合校验只执行于源模板，最终目标仅做语法检查。建议合并后执行经过实机验证的统一组合策略，逐账号提示冲突；首先需厘清下一项原生预设兼容差异，不能把保守约束直接宣称为游戏互斥规则。

### P1 · VIDEO-POLICY-01：模板规则拒绝原生菜单能够保存并重启保留的组合

复现：原生菜单设 FSR 品质，再选2×MSAA；FSR没有清除。正常退出后video保存MSAA2/CMAA0/FSR2，重启菜单两项仍保留。相同组合会被 ValidateVideoCombination 拒绝，影响导入或保存原生配置。证据见上述原生组合及重启截图、fsr-bindings阶段JSON，另有原生中档资源。建议将“禁止保存”改为经评审的兼容提示/组合策略；仍需验证实际渲染，不能直接宣布同时启用或互斥。本轮不改生产实现。

### P3 · ENV-01：autoexec 覆盖导致本地文件成功但运行值恢复

明确对照：相同四项写入，有共享autoexec时返回原值，临时停用后返回测试值并重启保留。原因是额外启动配置重设，而不是已证明参数无效。建议只读提示与模板目标键冲突的autoexec，勿擅自删除或禁用用户配置。共享文件本轮经用户授权临时改名，现已恢复。

### P3 · DISPLAY-02：当前全屏窗口保存0/0刷新率使分辨率控件不可用

原生菜单显示有效显示模式，保存文件刷新率分子/分母却为0/0；应用仅接受正整数保存刷新率。此条件阻止本次分辨率测试，不能报告为“分辨率部署通过”，也不代表CS2不支持外部修改。建议针对无边框与独占全屏分别设计枚举和明确禁用原因，先验证实际输出再解锁。

### P1 · USER-LOAD-01：第二账号首次启动重写20项用户配置，原因未定

复现条件：明确匹配第二测试账号、备份、停用共享autoexec、生产核心部署同一55项模板。应用后用户参数正确，启动后控制台与文件同时变为另一组值，例如sensitivity1.23→1.5、cl_crosshair_gap2→8、viewmodel_fov64→68；另23个机器级参数及12个视频字段保持目标。正常退出后再次部署同一模板，43项convar全部返回目标且退出保留。

根本原因尚未确认；重试成功说明不能把这些参数判为游戏不支持。建议补充部署后启动差异诊断、客户端初始化和Cloud时序对照，不能自动反复覆盖用户文件，也不能凭一次失败更换映射。按首次部署结果未被采纳的P1兼容问题追踪，尚不将原因归于应用代码或SteamCloud。

### P3 · 菜单/效果解释边界

音频原始增益与游戏菜单百分比不同；当前准星样式影响传统参数效果；曾有 videocfg_dynamic_shadows 写入警告，但离线档位测试可读取并保留。建议补充精确提示和样式/模式适用条件；警告本身不够判定映射失败。

未观察到 P0 配置破坏，也尚无证据将某项归为 P2“重启丢失”。不允许用尚未执行的游戏测试填补这些结论。

## 8. 复核、证据与后续优先级

本 PR 新增独立工具、只读资源核对脚本、CI 隔离检查和本文档。运行方式见 [验证工具说明](../tools/Cs2ConfigVerifier/README.zh-CN.md)。公开证据包含虚构测试结果、白名单参数、客户端资源摘要和44张原始游戏截图；[截图散列清单](cs2-feasibility/screenshot-manifest.json)。原始账号配置、全量控制台日志、SteamID、含昵称的主菜单及被遮挡桌面截图留在本机忽略目录，不进入 PR。截图没有AI生成或修改。

1. 补齐第二账号最终重启的游戏内查询，以及首次20项重写的独立复现和Cloud/初始化时序对照；已有重复部署读取和退出保留证据，不跳过首次失败。
2. 在固定显示环境中完成分辨率/实际输出、FSR/MSAA/CMAA、Gamma、V-Sync与帧时间的受控效果比较。不得把不同显示环境截图作直接效果A/B。
3. 逐项补齐音频输出、HUD/雷达像素与操作、准星样式、鼠标物理位移及键位动作证据；当前只有参数/档位与保留证据。
4. 用户配合操作Cloud开关，分别验证远端上传、回下载和切号；不得删除远端配置，最后单独确认远端恢复。
5. 根据真实证据单独评审小型修复。当前仅提交验证工具、测试和文档，不合并 PR、不发布新版本。

结论：**第一账号全部55个设置及67个键位已有游戏读取/档位及重启保留证据；完整客观效果验收尚未完成。autoexec确实能造成“文件写入成功、运行值被覆盖”；FSR/AA模板规则确实拒绝原生可保存并重启保留的组合。当前不能宣称全部游戏效果兼容，也不能无条件断言某个已开放参数仅能写入而不被游戏识别。第二账号重复部署已读取并退出保留，但最终重启、实际输出与Cloud专项仍未完成。**
