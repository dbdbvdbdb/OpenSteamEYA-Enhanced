# 增强版开发前审查

日期：2026-10-09。审查先于业务代码修改。

## 基线与参考

- OpenSteamEYA：`e68ae7d24ac116826d791902bbaace6076f1936b`，MIT，保留根 LICENSE。
- Sakiora/SteamAccountManager：`50e9ee4640e8ca52bca4dace803b3cc60668f4f5`，根 LICENSE 为 MIT，旧 LICENSE.md 的专有声明已划线，README 尚有过时标签。只分析交互，不复制资源或代码。
- 参考项目实际为 C++ WebView2 + Vue/Vite，当前 App.vue 是表格列表，没有任务书描述的大背景卡片。用户已确认依据任务书原创实现 WinUI 卡片。

## 当前架构及依赖

单个 SteamEyaWinUI.csproj，C#、net10.0-windows10.0.19041.0，最低 Windows 10 17763，WinUI 3，Microsoft.WindowsAppSDK.WinUI 2.2.1。发布为 win-x64 Native AOT、自包含；禁用反射 JSON，现有存储使用 JsonSerializerContext。CI 使用 Windows 2025 / VS2026、.NET 10 和 Inno Setup。

现有架构是页面 code-behind + AppState 服务单例及事件，并非完整 MVVM。MainWindow NavigationView/Frame 导航到 Login、History、CachedAccounts、Loadout、Personalization、Settings、About；常驻页面缓存。账号对象 INotifyPropertyChanged，跨 WinRT ABI 类型标记 partial。

AccountHistoryService 存储 `%AppData%/SteamEYA/history/accounts.json`，含账号名、SteamID、EYA Token、头像、昵称、CS2 快照、备注、分组成员。去重键为 `id:SteamID`，缺 SteamID 时为 `name:AccountName`，查找优先 SteamID 后账号名。没有独立 GUID；不按昵称匹配。SteamLoginCacheService 管理 cached-login.json 与缓存头像；SteamConfigService/VdfDocument 管理 Steam 本地配置。SteamLoginService 校验 JWT、调用 SteamCryptoService 保护令牌、停止 Steam、更新配置并启动 Steam。SteamCredentialsAuthService 是现有账号密码认证流程，与新增明文密码库隔离。

CsPremierScoreService/SteamTokenOnlineValidationService 提供实际查询，History 模型含等级、优先分、冷却/VAC、国服信息。LoginPage.QuickLoginAsync 与 QueryAndSaveCsStatusAsync 可直接复用，缓存账号复用 RestoreCachedLogin。头像可复用本地 AvatarPath/AvatarUrl。现有 SteamProfileService 用于修改资料，不是个人资料背景获取服务；需新增只读获取及回退。

GitHubUpdateService 从 hvh-software/OpenSteamEYA latest.json 检查更新，UpdateInstallerService 下载执行安装器。增强版未指定远程地址前必须禁用下载安装路径，避免覆盖。

## 改动、复用及隔离

- 新增纯 .NET 账号管理模块：稳定 GUID、登录账号/明文密码、可选已确认 SteamID、昵称、备注、独立分组、排序、收藏、背景设置、显式关联键。
- 独立 JSON 文件及背景资源在 `%LocalAppData%/OpenSteamEYA-Enhanced`，不写原 history/settings/cached-login 数据；不会把密码传给登录/查询/网络服务或日志。
- 新增账号管理页面及卡片/紧凑列表/隐私展示，MainWindow 仅新增导航入口，Settings 增加入口。原历史功能保留。
- 可复用登录、查询、头像和状态展示、路径选择窗口句柄、主题和全局忙碌协调。
- 不改 SteamCryptoService、JWT、SteamConfigService、缓存凭证、EYA 登录算法、CS2 协议、配装/工坊/个性化业务。
- 新增导入导出、映射、背景、备份恢复服务和独立自动化测试；CSV 支持引用/换行，TXT 使用明确转义协议并兼容 account----password。

## 风险与检查

1. 本机只有 .NET 6 Runtime，无 SDK，尚未发现 VS C++ 工具链；所需开发工具安装方式已向用户提问。
2. Native AOT：所有 JSON 类型注册到生成上下文，UI 跨 ABI 类型 partial；不能引入反射依赖。
3. SteamID 与账号名冲突、多个候选必须让用户确认，不自动分配密码，不按昵称匹配。
4. 新数据库坏文件不能降级成空后覆盖；写入临时文件原子替换，批量导入/恢复先校验再提交，错误只报告行号/通用原因。
5. 隐私模式遮盖昵称、头像、账号、SteamID、备注等标识；切换保留选中 GUID 和筛选状态。
6. ZIP 恢复只接受列入清单的资源与版本，防路径越界、重复资源、异常体积，提交前完成资源校验。
7. 背景提取/下载限并发、异步、缓存，失败回退。个人资料背景可能不可获取，不能伪造。
8. 原 MIT 保留；Steamworks DLL、武器 SVG 和 Windows App SDK 的既有许可需随发布说明列明，不能据根 MIT 推断第三方均为 MIT。
9. 不用未经授权的真实账号登录测试；该部分最终单列为待用户实机验收。

## 实施顺序

按任务书阶段 2–9：独立数据与关联 → 导入导出 → 卡片和原登录/查询接入 → 背景 → 布局分组排序搜索 → ZIP 备份恢复 → 设置/更新整合 → 构建、自包含发布与文档。每阶段更新 DEVELOPMENT-LOG，运行对应测试；不得把未编译或未验证事项声明为完成。
