# OpenSteamEYA 增强版 0.2.4

以 OpenSteamEYA 为基础的 C# / WinUI 3 Windows x64 桌面程序。原登录、历史账号、缓存账号、配装、个性化、工坊和 CS2 功能保留，新增独立的账号密码管理页。参考项目当前为表格界面，账号卡片按任务书原创实现，没有复制其资源或代码。

> 当前说明见 README.md、THIRD_PARTY_NOTICES.md 和 docs/RELEASE-AUDIT.zh-CN.md。源码已获维护者批准公开，v0.2.4-beta Release 尚未发布。

## 运行

当前优化交付为 `OpenSteamEYA-Enhanced-0.2.4-win-x64-aot.zip` 或更小的 `.7z`。解压到普通本地文件夹，运行 `SteamEyaWinUI.exe`。必须保留解压后的全部 DLL、PRI、Assets、Languages 等文件，不能只拷贝 EXE。程序由 .NET 10 Native AOT 原生编译，Windows App SDK 自包含，不需要另外安装 .NET 运行时。原普通自包含 ZIP 保留用于体积对照。本次在当前 Windows x64 主机验证，其他 Windows 10/11 系统仍需实机确认；源码声明最低版本为 Windows 10 Build 17763。参见 [体积优化报告](docs/SIZE-OPTIMIZATION.zh-CN.md)。

## 账号管理

- 已有 EYA/缓存账号在新页显示，未添加密码时显示“未保存密码”，仍可调用原登录凭证登录。
- 手动添加登录账号、密码、已确认的 SteamID（可选）、昵称、备注及分组。昵称不会用于身份匹配。
- 自动关联优先 SteamID、登录账号；身份冲突或多个候选须手动确认。菜单可显式关联；SteamID 不一致会阻止关联。
- 一键登录复用原 EYA / 缓存登录流程。保存的明文密码只用于管理，绝不传入该登录流程或替代 Token。
- 复制账号/密码没有计时清空、Windows Hello 或强制加密。密码默认遮挡，可以显示或隐藏。
- 卡片与紧凑列表保留搜索、分组及多选状态。隐私模式遮盖头像、昵称、账号、SteamID、密码、备注、分组与自定义图片，仅保存 UI 偏好，不改变身份与密码。
- 卡片右键提供复制、原登录、CS2 查询、详情、编辑、背景、分组、收藏、关联、删除密码管理记录。删除不会调用原凭证删除；已有原账号可能继续显示为空密码卡片。
- 创建、改名、移动、删除分组；删除分组时成员转入默认分组。支持分组顺序、自定义拖动排序、昵称、登录账号、最近使用、CS2 等级和收藏优先。
- CS2 状态读取原查询结果，缺失时显示未获取。未提供可靠在线状态接口，因此不显示在线/离线猜测。

## 存储与明文说明

新增数据：`%LocalAppData%\OpenSteamEYA-Enhanced\manager.json`；本地背景在 `backgrounds`，可再生成的图片缓存在 `cache`，备份位置提示在 `backups`。固定在当前用户本地目录，避免放入漫游 AppData；本程序没有云同步功能，也不上传密码。

这些文件、导出文件和 ZIP 备份包含可直接读取的明文 Steam 密码。请自行管理文件和备份。程序不会在新增模块日志/异常提示中输出密码或解析失败的输入内容。

原 `%AppData%\SteamEYA` 的凭证、历史记录、Token 和原设置继续由原业务模块管理。新增导入、删除和恢复服务不读写其凭证文件；用户显式登录/查询仍按原流程更新历史。

写入使用独占文件租约、进程内门锁、临时文件及原子替换，避免多实例覆盖和中断留下半截文件。坏数据不降级为空后覆写；可用有效备份的“覆盖新增管理数据”恢复。

## 背景

默认从头像像素提取多种颜色生成柔和渐变，异步处理、并发上限 4、缓存并取消旧卡片任务。浅色主题调整配色，文字采用遮罩保持可读。

可选择公开 Steam 资料背景，无法访问、页面结构改变、私密资料或不支持的动画资源时回退取色；不使用伪造背景。PNG/JPG/JPEG/WEBP 本地背景在选择时验证系统解码器，限制 20MB / 6400 万像素。支持裁切、完整显示、拉伸及恢复默认。柔化采用低分辨率重采样；它是视觉软化，不是特定高斯模糊算法。

## 导入、导出和备份

查看 [格式与备份说明](docs/FORMATS-AND-BACKUP.zh-CN.md)。TXT、CSV、JSON 都支持明文密码及空密码，导入预览、重复策略、行号错误统计；导出支持全部、多分组和选中范围。

ZIP 备份包括新增账号、密码、备注、分组及顺序、排序、收藏、背景配置/图片、UI 偏好和关联键，不包括 EYA Token、Steam 会话或原认证数据。预览版本、数量、重复和资源后选择合并或覆盖。

## 构建与测试

需要 .NET 10 SDK，项目锁定 WinUI 包 2.2.1。默认发布 Native AOT，另需 Visual Studio C++ Build Tools 及 Windows SDK：

```powershell
dotnet build SteamEyaWinUI/SteamEyaWinUI.csproj -c Release -p:Platform=x64
dotnet run --project tests/AccountManagement.Tests/AccountManagement.Tests.csproj -c Release
./scripts/publish-enhanced.ps1
```

生成到 `artifacts/release-win-x64-aot`，脚本严格验证 AOT，不自动回退普通版。保留第三方许可和使用/格式说明，调试符号保存在 artifacts/symbols-*；截图及开发文档只放源码。7-Zip 可用时同时生成高压缩 Deflate ZIP 和 LZMA2 7z：

```powershell
./scripts/publish-enhanced.ps1 -NativeAot
```

只有明确需要普通自包含版时使用 `./scripts/publish-enhanced.ps1 -NativeAot:$false`；Native AOT 失败不会自动选择它。

完整源码 ZIP 包含原项目源码/资源及新增模块、测试、脚本、文档，不含 `.git`、构建缓存和发布文件。运行 `./scripts/package-source.ps1` 可重新打包；本地未提交变更也会包含。直接交付目录中的 Git 仓库保留原上游历史；源码 ZIP 解压后可自行 `git init`，或在克隆上游后应用其中变更。

界面自动化通过显式 QA 构建运行在构建目录的合成数据，不读取真实凭证，不登录真实账号，QA 开关不编译进正式包：

```powershell
./scripts/check-manager-ui.ps1
./scripts/check-manager-ui.ps1 -NativeAot
```

构建 Inno 安装包可使用已修改的 `build/installer/SteamEYA.iss`，其 AppId 和安装目录与上游独立。当前交付便携 ZIP，未生成或执行安装器。

## 持续维护

本地分支 `enhancement/account-manager`，保留 `upstream` 指向原项目。尚未提供自己的远程仓库，因此没有创建 GitHub 仓库、推送或公开发布。

上游自动下载安装已禁用，避免上游程序覆盖增强版。配置独立发布源时同时修改 GitHubUpdateService 的仓库/元数据地址及开关，再验证安装器哈希和版本流程；不能只开启开关继续使用上游地址。

同步上游前先创建增强版备份；fetch upstream，审查原页面、MainWindow 导航、Settings 入口、更新器及 csproj 冲突，再合并兼容代码并跑数据、UI 与实际 Steam 回归。参见 [架构审查](docs/ARCHITECTURE-REVIEW.zh-CN.md)、[开发记录](docs/DEVELOPMENT-LOG.zh-CN.md)和 [验收记录](docs/VALIDATION.zh-CN.md)。

根 LICENSE 保留原 MIT 声明；运行时和 Valve 资源使用各自条款，见 [第三方声明](THIRD_PARTY_NOTICES.md) 及 `third-party-licenses`。不能将这些第三方资源一概称为 MIT。

## 账号卡片 UI-R1

0.2.1 按演示图重构账号卡片，统一 Hover/选中圆角并保留键盘焦点。页面右上方提供搜索和添加，第二行提供分组、排序、视图、隐私模式及管理工具。原导入、导出、备份、分组和设置入口在管理工具菜单中。详见 [UI 整改报告](docs/UI-R1-REPORT.zh-CN.md)。

## 0.2.2 新功能

添加账号支持按 `steam账号----steam密码` 逐行批量粘贴，默认保留重复账号原密码，可选择其他重复处理策略。选中卡片后，隐私模式后出现全选、反选、取消全选和查询 CS2 状态。全选/反选作用于当前显示账号，取消全选清除全部选择，查询复用原 EYA 服务。

昵称后显示括号备注，长内容可通过提示查看。卡片右下角三个点已移除，右键打开原菜单，双击卡片内容编辑账号。原登录凭证、密码文件格式及导入导出备份服务保持不变。功能实现、测试和截图见 [0.2.2 功能报告](docs/FEATURES-0.2.2.zh-CN.md)。

## 0.2.3 状态和多分组

冷却剩余时间显示为黄色，GCVAC 有标记显示为红色，卡片和紧凑列表均支持。批量查询报错加入“失败”，没有凭据而跳过加入“未查询”，成功查询清除这两个标记；取消后未处理账号不改变分组。所有手动分组继续保留。

编辑账号和右键“设置账号分组”支持多选。默认分组始终包含全部账号。旧单分组数据与备份自动兼容，密码格式与原 Steam 凭证逻辑不变，多分组备份完整保留归属。见 [0.2.3 报告](docs/FEATURES-0.2.3.zh-CN.md)。

## 0.2.4 冷却显示修正

只显示实际异常项：黄色剩余时间、红色 GCVAC 标记。无异常仅显示“正常”，不与异常同时列出“正常”或“无标记”。未知数据继续显示未知，倒计时到期自动收起冷却异常。卡片和紧凑列表同步修正，见 [0.2.4 报告](docs/FEATURES-0.2.4.zh-CN.md)。
