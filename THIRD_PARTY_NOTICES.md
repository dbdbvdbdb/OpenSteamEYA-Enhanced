# Third-party notices / 第三方声明

更新日期：2026-10-10。项目源代码 MIT 不覆盖第三方运行组件及 Valve 资源。本清单描述实际使用情况，不授予第三方权利。所有上游原版权和各组件完整条款均应保留。

## 上游代码与参考

| 来源 | 使用方式 | 许可证与版权 |
| --- | --- | --- |
| [hvh-software/OpenSteamEYA](https://github.com/hvh-software/OpenSteamEYA) | 基础代码；基线 `e68ae7d24ac116826d791902bbaace6076f1936b`，保留原 Steam/EYA/CS2 服务与页面 | [MIT](https://github.com/hvh-software/OpenSteamEYA/blob/main/LICENSE)，Copyright (c) 2026 tuntun1337；根 LICENSE 保留全文 |
| [Sakiora/SteamAccountManager](https://github.com/Sakiora/SteamAccountManager) | 仅交互参考，检查基线 `50e9ee4640e8ca52bca4dace803b3cc60668f4f5`；未复制代码、图像或控件 | 当前 [MIT](https://github.com/Sakiora/SteamAccountManager/blob/main/LICENSE)，Copyright (c) 2026 xiaoyu_jie；因未分发其内容，致谢不表示混入其代码 |

增强版 AccountManagement、账号管理页面、卡片及相关测试独立实现，继续采用 MIT。普通 BCL 和 Windows 系统 API 的调用不意味着复制对应系统源码。原历史里的作者信息保留用于归属，不视为测试用户数据。

## NuGet / .NET / 构建依赖

以下为 Windows x64 还原结果。唯一直接 UI NuGet 引用为 WinUI，其余为传递/构建依赖。测试工程无第三方测试框架。具体哈希及随包声明列表由发布脚本从实际还原包生成到 `third-party-licenses/nuget-license-inventory.json`，不含本机路径。

| 包 | 实际版本 | 适用许可 / 处理 |
| --- | --- | --- |
| Microsoft.WindowsAppSDK.WinUI | 2.3.9 | Microsoft Windows App SDK 条款；允许随应用再分发 NuGet 放入程序目录的文件，须遵守完整分发要求 |
| Microsoft.WindowsAppSDK.Foundation | 2.3.12 | 同上，包内完整 license.txt |
| Microsoft.WindowsAppSDK.InteractiveExperiences | 2.1.9 | 同上，包内完整 license.txt |
| Microsoft.WindowsAppSDK.Base | 2.0.4 | 同上，包内完整 license.txt / NOTICE.txt |
| Microsoft.Web.WebView2 | 1.0.3719.77 | 包内 Microsoft BSD 风格三条款与 NOTICE；程序无 WebView API 使用，运行载荷在经源码检查后移除，许可保留 |
| Microsoft.Windows.SDK.BuildTools | 10.0.26100.4654 | [Windows SDK 条款](https://aka.ms/WinSDKLicenseURL)，构建用，不随程序分发 SDK 工具 |
| Microsoft.Windows.SDK.BuildTools.MSIX | 1.7.251221100 | SDK 专有条款 sdk_license.txt 及 NOTICE；构建用，许可仍归档 |
| Microsoft.DotNet.ILCompiler | 10.0.12 | MIT 与 THIRD-PARTY-NOTICES，构建用 |
| runtime.win-x64.Microsoft.DotNet.ILCompiler | 10.0.12 | MIT 与 THIRD-PARTY-NOTICES，构建用 |
| Microsoft.NET.ILLink.Tasks | 10.0.12 | MIT 与 THIRD-PARTY-NOTICES，构建用 |
| Microsoft.NETCore.App.Runtime.win-x64 | 10.0.12 | .NET Native AOT 所用运行时代码，MIT 与运行时 THIRD-PARTY-NOTICES；发布脚本归档匹配版本 |

**已修复的发布阻断：** WinUI **2.2.1** 包内及 [官方 NuGet 许可证](https://www.nuget.org/packages/Microsoft.WindowsAppSDK.WinUI/2.2.1/License) 是 Engineering Preview，不允许再分发。现已在用户授权下切换为 [2.3.9 的可再分发条款](https://www.nuget.org/packages/Microsoft.WindowsAppSDK.WinUI/2.3.9/License)。旧 2.2.1 产物不能因称为 beta 而公开发布。打包遇到 Engineering Preview 许可时会直接失败。

CsWinRT、WinRT.Runtime 和 Windows SDK .NET 投影来自 SDK/上述组件的构建资产，并无额外本地第三方源码库。保留组件 NOTICE（含 tools/NOTICE）与 .NET 运行时第三方说明；不得将编译进 Native AOT 的许可义务忽略。Visual Studio、Windows SDK、7-Zip、Git 和审查工具 Gitleaks 仅作开发工具，不把它们的安装目录打入运行包。实际检查工具版本和许可出处见审查报告。

Microsoft 运行组件并非 MIT 源码再许可。本项目及下游分发者必须遵守其完整分发要求，将条款传递给使用者，见 END_USER_TERMS.txt。包中 LICENSE/NOTICE 的完整内容优先于此摘要。

## Valve 与其他图像资源

### steam_api64.dll

`SteamEyaWinUI/steam_api64.dll` 是上游引入的 Steamworks API x64 二进制，用于现有 CS2 云同步。文件带有有效 Valve Corp. Authenticode 签名，版本 `10.51.08.25`；未修改。

使用及再分发适用 Valve 的 Steamworks 条款，该 DLL 不在项目 MIT 许可范围内。参考 [Steamworks API 文档](https://partner.steamgames.com/doc/sdk/api) 与 [Steamworks 开源分发说明](https://partner.steamgames.com/doc/sdk/uploading/distributing_opensource)。保留原文件和现有 CS2 云同步功能。

### 35 个 Counter-Strike 武器 SVG

`SteamEyaWinUI/Assets/weapons/*.svg` 来自 [Juknum/counter-strike-icons](https://github.com/Juknum/counter-strike-icons)，原始图像属于 Valve。完整声明保存在 `third-party-licenses/counter-strike-icons-LICENSE.txt`。

该 [许可证](https://github.com/Juknum/counter-strike-icons/blob/main/LICENSE) 将工具代码的 MIT（Copyright (c) 2026 Julien Constant）与游戏图像区分：图像限信息/社区用途，未经 Valve 许可不得商业使用，并应遵守 Valve 条款。本项目没有复用其提取工具源码，游戏图像不在项目 MIT 许可范围内。

### AppIcon.ico 与 Logo.png

`SteamEyaWinUI/Assets/AppIcon.ico` 和 `Logo.png` 来自上游提交 `0ddd5337aef79c0df476abcc96c4619586dd6582`，沿用原应用品牌图像。保留上游版权和出处，不将第三方图像改为增强版原创或重新许可为 MIT。

### 运行时用户背景和头像

用户自选图片、Steam 公开头像/背景按用户操作加载到本地，运行包不携带用户图片与缓存。README 的实际 WinUI 截图使用合成数据，包含原应用 Logo。

## 商标与版权反馈

Steam、Steamworks、Counter-Strike 为 Valve 的相关商标，本增强版与 Valve、Steam 及上游维护者无官方合作或背书关系。

如有版权问题，请联系删除。请通过维护者 GitHub 联系渠道或本仓库版权问题 Issue 提供依据；不要发送账号密码或 Token。相关资源继续适用各自的版权声明及条款。
