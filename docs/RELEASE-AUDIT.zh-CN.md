# v0.2.4-beta 开源发布准备审查

初次审查：2026-10-09；维护者审核更新：2026-10-10。基线：本地 v0.2.4 交付，Windows x64。

## 结论：维护者已批准公开源码仓库

维护者自行查询后确认资源审核完成，并明确批准公开 [dbdbvdbdb/OpenSteamEYA-Enhanced](https://github.com/dbdbvdbdb/OpenSteamEYA-Enhanced)。根据该决定，移除 Steamworks DLL、原品牌图像及 Valve 武器图像的待核实与公开阻断描述，保留现有功能、资源、原作者版权和第三方许可。

审核记录写入 `release-readiness.json`，依据为维护者本次确认，不表述为额外取得了新的授权文件。源码、文档、测试及上游提交历史均保留；生成程序包、日志、工具和测试数据库由 `.gitignore` 排除。**公开源码仓库与发布程序 Release 分别执行，当前尚未创建 Release。**

## 许可证结果

- 在线核实当前 OpenSteamEYA LICENSE：MIT，Copyright (c) 2026 tuntun1337。实际复用基础代码，保留完整许可及版权；增强部分继续 MIT。
- 在线核实 Sakiora/SteamAccountManager LICENSE：MIT，Copyright (c) 2026 xiaoyu_jie。仅交互参考，未复制其代码、控件或图像。
- 唯一直引 UI NuGet 包及实际传递/构建依赖已检查 nuspec、包内 LICENSE/NOTICE，共 10 个 NuGet 包，另保留匹配版本的 .NET 运行时许可。具体版本和独立条款见 [THIRD_PARTY_NOTICES.md](../THIRD_PARTY_NOTICES.md)。
- **发现并修复严重问题：WinUI 2.2.1 是 Engineering Preview 许可，禁止再分发。** 经用户授权升级至 2.3.9；WinUI/Foundation/InteractiveExperiences/Base 当前许可证均含随应用再分发条款。升级没有改变业务源代码。旧 2.2.1 程序包不能直接用于公开 Release。
- Microsoft 专有运行组件保留完整条款，项目 MIT 不覆盖它们；新增 END_USER_TERMS.txt 要求使用者和下游分发者遵守适用许可。无需单独打包 Visual Studio/Windows SDK 的开发工具。
- Steam API 本地库、Valve 游戏图像和原品牌图像保留其出处与独立版权声明，不一概称为 MIT。没有发现其他本地第三方库。

参考：[OpenSteamEYA MIT](https://github.com/hvh-software/OpenSteamEYA/blob/main/LICENSE)、[SteamAccountManager MIT](https://github.com/Sakiora/SteamAccountManager/blob/main/LICENSE)、[WinUI 2.2.1 条款](https://www.nuget.org/packages/Microsoft.WindowsAppSDK.WinUI/2.2.1/License)、[WinUI 2.3.9 条款](https://www.nuget.org/packages/Microsoft.WindowsAppSDK.WinUI/2.3.9/License)、[武器图像声明](https://github.com/Juknum/counter-strike-icons/blob/main/LICENSE)、[Steamworks API 文档](https://partner.steamgames.com/doc/sdk/api)、[Steamworks 与开源分发](https://partner.steamgames.com/doc/sdk/uploading/distributing_opensource)。

## 敏感数据审查

**检查范围内未检出真实密码、EYA Token、API 密钥或私钥；没有凭据泄露导致的历史重写需求。** 这不是所有环境或未来提交永远无泄露的保证。

- Gitleaks **8.30.1** 使用官方校验过的 Windows 便携工具，全程 `--redact=100`；扫描全部 refs 历史以及 tracked + untracked 未忽略的源文件，均无命中。
- Git 全部 refs 共 **61 个提交**。Gitleaks 展示 50 个产生扫描内容的提交；补充扫描全部 **426 个唯一历史 blob**（423 个文本），覆盖历史删除的文件。`git fsck` 未发现额外不可达对象，reflog 未发现额外提交。
- 附加规则检查私钥、JWT、Steam/Token 字面量、用户数据文件名及本机路径。最终公开候选源文件无未解决命中。
- 全工作区含忽略文件的首次 Gitleaks 扫描有 **734 项候选命中**：732 项来自 12 份明确的旧合成 QA 数据，2 项来自下载的 Gitleaks 自带文档示例；均位于忽略的 artifacts。复核所有对应管理库均由 QA 账号生成，未将示例值输出到报告。
- 另盘点项目内忽略的管理数据和 ZIP 内容，识别真实数据库/密钥候选。合成数据、门禁测试文件及空扫描报告均不公开；未发现待处理的真实用户数据库或备份。盘点结果见 `test-evidence/release-beta/local-data-summary.json`。此盘点不读取项目外的真实用户目录。
- 13 份开发日志中的本机路径已脱敏；所有 `.log` 默认忽略，不再将生成日志纳入源码包。必要证据另以脱敏文本归档。
- `.gitignore` 补充数据库、历史/登录配置、导出、备份、日志、PDB、临时文件、私钥、压缩包和 QA 数据目录；源码打包额外拒绝这些高风险路径，防止已 tracked 的文件绕过忽略规则。
- 正式 Native AOT EXE 不含 QA 开关或合成账号标记。发布目录无数据库、凭据、截图、调试文件或临时产物；ZIP 逐文件 SHA-256 与发布目录完全一致。

如果未来发现真实凭据，应立即暂停发布、由所有者撤销会话并轮换凭据，再经授权清理全部 Git 历史和已发布资产；只改 .gitignore 或删当前文件不够。步骤见 [SECURITY.md](../SECURITY.md)。报告不会输出真实凭据。

## 文档完成情况

已完善 README.md、CHANGELOG.md、LICENSE、THIRD_PARTY_NOTICES.md、SECURITY.md、CONTRIBUTING.md，另新增 END_USER_TERMS.txt、发布说明和审查门禁。旧 THIRD-PARTY-NOTICES.md 保留为历史兼容入口，明确新文件优先。

README 含上游致谢、功能、真实合成数据截图、Windows 下载/运行、明文密码安全、备份边界、依赖/编译、非官方关系及版权联系声明；不伪称已有下载或 Release。CHANGELOG 保留 v0.2.0–v0.2.4 的真实本地更新，beta 条目明确 Release 未发布。最新 WinUI 2.3.9 截图在 `screenshots/beta-accounts.png`、`beta-list.png`。

## 编译、测试与自动构建

| 项目 | 实际结果 |
| --- | --- |
| .NET SDK | 10.0.401，global.json 固定补丁系列 |
| C++ / Windows SDK | MSVC x64 及 SDK 10.0.26100.0 检查通过 |
| 依赖锁定 | 三个项目 lock 文件生成；统一 PublishAot=true 后锁定还原通过 |
| WinUI 2.3.9 Native AOT | 编译成功，无编译警告/错误；首次缓存混用已通过 `dotnet clean` 消除 |
| 真实 WinUI 合成数据检查 | **224 / 224 通过** |
| 原生数据回归 | **85 / 85 通过**；锁定依赖后的脚本也已执行通过 |
| 正式包检查 | 原生 x64 PE，无 CLI 头/CLR host/JIT，必需 WinUI/Steam/语言资源齐全 |
| 正式包启动 | 解压后窗口响应正常，正常关闭，未调用登录/查询操作 |
| ZIP / 7z | 两种压缩包完整性检查通过；ZIP 全部 300 文件逐项哈希匹配 |
| 审核/数据打包检查 | 维护者审核状态由 release-readiness.json 记录；manager.json 测试候选被打包检查拒绝 |
| 脚本语法 | 全部 PowerShell 脚本解析通过 |
| Actions 静态检查 | 官方校验过的 actionlint **1.7.12**，两个工作流检查通过 |
| **远端 GitHub Actions** | 提交 `6657c57` 的 [首次 Windows Native AOT CI](https://github.com/dbdbvdbdb/OpenSteamEYA-Enhanced/actions/runs/37957334299) 全部成功：85 项数据检查、224 项真实 WinUI 检查及 ZIP/7z 打包验证通过；后续提交结果以各自 Actions 记录为准 |

CI 使用官方 `windows-2025-vs2026`、固定 Actions 提交、.NET 10 SDK、完整 Git 历史扫描、C++/SDK 检查及必要时官方签名工具链安装、数据与实际 WinUI 检查、AOT 打包和校验。Release 接受 beta 标签，分离编译数字版本与标签，版权门禁在构建/上传前执行；只创建 draft prerelease、不标 Latest、不覆盖现有资产。没有自动公开正式版。

QA 用合成数据与隔离存储。正式包启动烟测使用当前 Windows 用户正常数据路径，仅检查窗口响应与关闭，不输入账号、不截图、不触发登录/CS2 查询。截图测试实际系统 DPI 为 150%；导出其他比例图像不代替系统 DPI 验收。

## 本地发布文件与体积

| 内容 | 原 v0.2.4（WinUI 2.2.1，不可直接公开） | 审查 beta（WinUI 2.3.9） |
| --- | ---: | ---: |
| ZIP | 28,432,720 B / 27.12 MiB | **29,559,348 B / 28.19 MiB** |
| 7z | 17,417,579 B / 16.61 MiB | **17,862,570 B / 17.04 MiB** |
| 解压 | 73,771,401 B / 70.35 MiB | **78,318,847 B / 74.69 MiB** |
| 文件数 | 296 | **300** |

增量来自新 WinUI 运行组件及完整许可/安全说明，未删除必需依赖或牺牲功能。比最初普通 .NET 自包含包约 68.27 MiB / 解压 169.18 MiB 仍显著缩小。

本地文件位于 `artifacts`：

- `OpenSteamEYA-Enhanced-0.2.4-beta-win-x64-aot.zip`
- `OpenSteamEYA-Enhanced-0.2.4-beta-win-x64-aot.7z`
- `SHA256SUMS.txt`
- 发布文字：`docs/releases/v0.2.4-beta.md`

ZIP SHA-256：`a4c18edf3bc1949c68ffaaf1936c32d68b23ca3572e6610621e96139c5cc0560`。

7z SHA-256：`21825e4a128e6aecb518faaf8097c7b20da25b2c5075dac0a03f721078ccc8fa`。

这是本地审查构建，不是已发布资产。远端重编译后需以实际上传文件重新统计、生成校验和并更新 Release 说明。

## 仍需人工验收

真实测试账号的 Steam/EYA 账密登录及 Steam Guard、缓存一键登录、未保存密码账号登录、CS2 联网查询、CS2 云同步、配装/个性化/工坊等原功能；以及 Windows 10/11 其他系统、四种系统 DPI、深浅主题与窗口宽度组合。自动化覆盖管理操作、导入导出、备份恢复和界面合成场景，不能代替真实服务验收。用户先前仅确认原页面打开，联网验证留待自行测试。

## 本次修改范围

- 根文档：README.md、CHANGELOG.md、LICENSE、THIRD_PARTY_NOTICES.md、SECURITY.md、CONTRIBUTING.md、END_USER_TERMS.txt；更新旧增强说明/旧声明入口。
- 构建配置：SteamEyaWinUI.csproj 仅改 WinUI 版本及许可说明；global.json、NuGet.Config、Directory.Build.props 和三个项目 packages.lock.json。
- 工作流：build.yml、release.yml。
- 脚本：publish-enhanced、package-source、check-manager-ui，以及新增工具链、数据、敏感扫描、许可证归档、打包/启动/公开门禁检查和 audit-source.py。
- 文档证据、最新真实截图及发布草稿。

与原 v0.2.4 源码交付比较的 **137 个源码、UI、DLL 和图像文件哈希全部一致**。Steam 凭据、EYA、CS2 业务服务及账号管理密码数据逻辑均未改动。本次公开更新仅调整文档及维护者审核记录，不改业务代码、不删除资源、不重写 Git 历史、不创建 Release。
