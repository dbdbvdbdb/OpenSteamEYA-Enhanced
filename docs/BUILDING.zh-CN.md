# 编译、测试与下载校验

普通使用无需编译，下载与运行见 [README](../README.md)。本页面向源码开发者，不代表在所有 Windows 版本或真实 Steam 账号中完成验收。

## 开发环境

- Windows x64、Git、PowerShell 7。
- .NET SDK **10.0.401**，以仓库 [global.json](../global.json) 为准；关闭自动滚动升级。
- Visual Studio 2022 / 2026 C++ Build Tools（MSVC x64/x86）及 Windows SDK **10.0.26100.0**。
- 生成 7z 包需要 7-Zip；WinUI 检查需要可用的 Windows 图形会话。

依赖从官方 NuGet 源按锁文件还原。当前 WinUI 组件为 2.3.9，完整条款见[第三方声明](../THIRD_PARTY_NOTICES.md)。升级 SDK 或依赖时同时审阅锁文件、运行时版本与许可证。

## 源码构建

```powershell
git clone https://github.com/dbdbvdbdb/OpenSteamEYA-Enhanced.git
cd OpenSteamEYA-Enhanced
pwsh ./scripts/ensure-native-toolchain.ps1

dotnet restore ./SteamEyaWinUI/SteamEyaWinUI.csproj -r win-x64 `
    -p:Platform=x64 -p:PublishAot=true --locked-mode

pwsh ./scripts/check-data.ps1
pwsh ./scripts/check-manager-ui.ps1 -NativeAot
pwsh ./scripts/publish-enhanced.ps1 -NativeAot -Version 0.3.0 -ReleaseLabel 0.3.0
```

这里的发布命令在本机打包，不创建 GitHub Release；版本参数示例对应 v0.3.0，发布其他版本时同步核对源码和标签。工具链脚本默认检查环境，缺失组件时按提示安装。

数据测试通过控制台程序执行，`dotnet test` 不能代替它们。WinUI QA 使用隔离的虚构数据，正式包不含 QA 入口；这些检查不证明真实 Steam 登录、CS2 游戏效果或云同步已通过。

发布脚本验证 Native AOT，不会在失败时自动退回普通 .NET 自包含程序。保留必需的运行文件和许可证，生成便携 ZIP，并在 7-Zip 可用时生成 7z。具体输出以脚本执行结果为准。

## 下载校验

从[本仓库 Releases](https://github.com/dbdbvdbdb/OpenSteamEYA-Enhanced/releases/latest)下载压缩包与同版本 `SHA256SUMS.txt`，运行：

```powershell
Get-FileHash .\OpenSteamEYA-Enhanced-*-win-x64-aot.zip -Algorithm SHA256
```

将结果与校验文件逐项比对。校验和用于确认文件完整性，不代替代码签名或安全审查。通过后完整解压，不能只提取 EXE。

## 数据位置与调试边界

- 增强版管理数据：`%LocalAppData%\OpenSteamEYA-Enhanced`，包括管理库、分类偏好、背景和模板等。
- 原登录、历史和凭据数据：`%AppData%\SteamEYA`。
- 详细格式与备份边界见[格式与备份](FORMATS-AND-BACKUP.zh-CN.md)及[统一配置说明](CS2-UNIFIED-CONFIG.zh-CN.md)。

两类存储独立，但显式关联、登录和删除操作仍会按界面选择影响对应记录，不能理解为业务完全互不关联。管理数据备份不含全部原登录凭据。真实数据库、凭据、备份和个人路径不得提交到 Git。

发布前的文档与许可检查见[贡献指南](../CONTRIBUTING.md)。
