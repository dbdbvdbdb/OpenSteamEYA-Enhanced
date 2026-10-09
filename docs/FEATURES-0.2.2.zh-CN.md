# OpenSteamEYA Enhanced 0.2.2 — 账号管理功能交付

日期：2026-10-09。基于 0.2.1 UI-R1 增加用户要求的四项功能，继续使用 WinUI 3 / .NET 10 / Windows x64 Native AOT。

## 使用与行为

1. 添加账号按钮打开单个/批量添加菜单。批量对话框支持每行 `steam账号----steam密码`，不需要引号。第一处 `----` 是分隔符，后续分隔符以及密码中的空格、引号、反斜杠原样保存。支持不同换行符、空行、空密码，错误仅提示行号和固定原因，不回显输入。存在格式错误时整批不提交，保留输入。默认跳过重复账号，可选覆盖密码或只补充缺失密码；保存复用原 AccountStore.ImportAsync 原子写入。
2. 有选择时在隐私模式后显示全选、反选、取消全选和查询 CS2 状态。按用户确认，全选/反选仅作用于当前可见账号，隐藏选择保留；取消全选清除全部。查询使用全部选中账号的原 EYA 凭据，缺失关联跳过，同一凭据去重，串行调用原 QueryAndSaveCsStatusAsync。单次失败继续后续账号，原取消机制仍有效。
3. 昵称显示为 `昵称（备注）`，没有备注则不显示括号。长文本省略并提供完整提示；隐私模式不暴露备注。
4. 删除卡片底部三个点按钮，原完整右键菜单保留，支持原生上下文菜单及键盘菜单入口。双击卡片内容打开原账号信息编辑窗口；内部按钮排除双击编辑，防止登录/复制时意外打开编辑。

## 修改文件

| 范围 | 文件 |
|---|---|
| 新增纯数据/编排模块 | AccountManagement/BatchAccountInput.cs、AccountQueryBatch.cs |
| 新增页面分部 | SteamEyaWinUI/Pages/AccountManagerBatchAdd.cs、AccountManagerSelection.cs |
| 卡片、工具栏及显示 | SteamEyaWinUI/Pages/AccountCard.cs、AccountManagerPage.xaml、AccountManagerPage.xaml.cs |
| 自动检查 | SteamEyaWinUI/Pages/AccountManagerUiChecks.cs、Services/ManagerUiChecks.cs、tests/AccountManagement.Tests/FeatureChecks.cs、Program.cs |
| 版本和交付说明 | SteamEyaWinUI/SteamEyaWinUI.csproj、scripts/publish-enhanced.ps1、package-source.ps1、build-installer.ps1、README.md、README-ENHANCED.zh-CN.md、docs/RUNNING.zh-CN.md、DEVELOPMENT-LOG.zh-CN.md、本报告及截图/证据 |

版本更新至 0.2.2。未引入大型 UI 框架；未重写 Steam 查询或登录。44 个基线业务文件 SHA256 保持一致，页面原 LoginAsync/QueryAsync 保持一致；存储、导入导出、备份及背景服务未修改。

## 验证及限制

托管 WinUI 构建和 Native AOT WinUI 构建均运行隔离合成账号检查。198 项界面/离线检查覆盖原卡片、圆角状态、焦点、主题、列表、隐私、背景、复制、筛选以及新增选择、原生批量添加菜单/对话框、错误输入不写入、重复跳过、特殊密码保存和编辑窗口。66 项 Native AOT 数据测试包含原 52 项回归及新增解析、重复保留、队列串行/失败/去重/取消检查。构建和日志见 test-evidence/features-0.2.2。

截图来自实际 WinUI 应用的 RenderTargetBitmap，使用明确标注的合成账号，并非生成效果图。参见 [主页面](features-0.2.2/main.png)、[浅色窄窗口](features-0.2.2/minimum-light.png)、[隐私模式](features-0.2.2/privacy.png)。长昵称和备注在卡片有限宽度内截断，通过提示可看完整内容。窄窗口工具栏可横向滚动。

当前 Windows 系统 DPI 为 150%；100%、125%、150%、200% 的导出渲染尺寸已检查，不能等同于在四种系统 DPI 下实测。自动检查验证双击源过滤与实际编辑方法/窗口，未注入物理鼠标双击。真实 Steam 登录及 CS2 联网查询需要用户测试账号，本次未执行；不将离线编排通过计为联网通过。正式包不包含 QA 入口、合成账号、测试截图、PDB 或开发报告，保留 WinUI/Windows App SDK、Steam 文件、必要资源及许可证。

## 本机复测

```powershell
./scripts/check-manager-ui.ps1 -NativeAot
dotnet publish tests/AccountManagement.Tests/AccountManagement.Tests.csproj -c Release -r win-x64 -p:PublishAot=true -o artifacts/data-check-aot
./artifacts/data-check-aot/AccountManagement.Tests.exe
./scripts/publish-enhanced.ps1 -NativeAot -Version 0.2.2
```

实机打开正式包：批量粘贴测试账号、检查重复策略与错误行；选中并筛选后操作全选/反选/清除；修改备注；右键菜单、双击卡片编辑、内部按钮点击；用已关联测试账号查询 CS2 并检查真实结果。不要在聊天中提供密码或 Token。

## 发布结果

正式发布构建没有编译警告或错误；PE 检查确认 x64 原生映像，不含托管应用 DLL、.NET JIT 或运行时宿主。ZIP 和 7z 均通过 7-Zip 完整性检查，296 个 ZIP 解压文件哈希均与发布目录一致。解压后的正式应用成功建立响应正常的原生窗口，并正常关闭。本次启动检查没有调用登录或联网查询。

单位 MiB；文件数不计文件夹，解压体积为文件字节总和。

| 版本/产物 | ZIP | 7z | 解压体积 | 文件数 |
|---|---:|---:|---:|---:|
| 0.2.0 原普通 .NET 自包含 | 68.27 | — | 169.18 | 520 |
| 0.2.1 Native AOT | 27.03 | 16.55 | 70.14 | 296 |
| 0.2.2 Native AOT | 27.07 | 16.58 | 70.24 | 296 |

新增功能后 ZIP 较 0.2.1 增加 43,766 字节，解压体积增加 103,972 字节。最终精确大小：ZIP 28,387,395 字节，7z 17,380,283 字节，解压 73,655,286 字节。

SHA256：

```text
OpenSteamEYA-Enhanced-0.2.2-win-x64-aot.zip
02346382908383A948BDBDDDC9B69DA3F5739AA7005D92F6AD2AF26AD1E578B3
OpenSteamEYA-Enhanced-0.2.2-win-x64-aot.7z
535DBFB6BA8E21FA6C00976524353458617D1D028E8C2F8A4DA5E894C25F8709
```

完整原生编译、压缩和启动验证记录见 test-evidence/features-0.2.2/features-0.2.2-publish.log、size-aot.json、package-check.txt。源码包包含本次与历史报告及实际截图；正式运行包仅含运行所需文件、使用说明、格式说明和许可证。
