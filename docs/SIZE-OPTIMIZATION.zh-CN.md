# OpenSteamEYA-Enhanced 0.2.0 Windows x64 体积优化报告

日期：2026-10-09。已完成真正的 Native AOT 发布，没有使用普通 .NET 自包含产物冒充 AOT。原始程序 ZIP 保留，优化文件使用 `-win-x64-aot` 名称。

## 体积对比

单位为 MiB（1 MiB = 1,048,576 字节，与此前所报 68.27 / 169.18 的计量一致）。文件数量只计文件，不计文件夹；解压大小为实际文件字节总和，不是磁盘簇占用。

| 发布包 | 压缩大小 | 解压大小 | 文件数 | 相对原 ZIP 的压缩体积减少 |
|---|---:|---:|---:|---:|
| 原普通 .NET 自包含 ZIP | 68.27 MiB | 169.18 MiB | 520 | — |
| 优化 Native AOT ZIP | 27.00 MiB | 70.05 MiB | 296 | 60.45% |
| 优化 Native AOT 7z | 16.53 MiB | 70.05 MiB | 296 | 75.78% |

解压体积减少 **58.59%**，减少 224 个文件。精确字节：原 ZIP 71,581,488，原解压 177,393,657；AOT ZIP 28,309,645，AOT 7z 17,338,179，AOT 解压 73,455,082。

ZIP 便于直接解压；7z 更小，需要 7-Zip 等支持 7z 的工具。两者运行文件完全相同，没有精简版/完整版功能差别。

## 检查、工具链与发布配置

首先检查 csproj、publish-enhanced.ps1 和 GitHub Actions。csproj 已启用 PublishAot、WinUI 包 2.2.1、Windows App SDK 自包含及 JSON 源生成；原脚本只有显式 `-NativeAot` 才走 AOT，默认普通自包含，并将截图/开发文档复制进包。原 CI 走 AOT，但没有独立产物类型断言，且重复执行依赖删除。

首次调用现有 `-NativeAot` 失败：`Platform linker not found`。排查确认无 MSVC / Windows SDK。用户明确授权后，从微软 `https://aka.ms/vs/17/release/vs_BuildTools.exe` 下载安装器，Authenticode 状态 Valid、签名 Microsoft Corporation；安装 C++ Build Tools 工作负载与推荐组件，退出码 0、无需重启。最终环境：

- .NET SDK 10.0.401 / Native AOT 编译包 10.0.12。
- Visual Studio Build Tools 2022，17.14.41；MSVC 14.44.35207（dumpbin 工具版本 14.44.35229）。
- Windows SDK 10.0.26100.0；当前主机 OS 版本 10.0.26200.0，x64。
- 7-Zip 实际命令行版本 26.02。

官方工具链要求见 [Microsoft Native AOT 文档](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/)，组件见 [Build Tools 组件目录](https://learn.microsoft.com/en-us/visualstudio/install/workload-component-id-vs-build-tools?view=vs-2022)。

## 优化内容及保留项

1. **原生编译与体积偏好**：Release + PublishAot 使用 `OptimizationPreference=Size`。不关闭全球化、异常栈、安全能力或任何业务功能，不使用 InvariantGlobalization 等功能裁减开关。
2. **全新发布目录**：每次发布到独立 staging，成功后归档旧目录并替换。避免普通版 DLL、旧测试文件混入 AOT 包。AOT 失败立即停止，没有自动 managed fallback；默认发布已改为 AOT。
3. **符号外置**：PDB 移到 `artifacts/symbols-0.2.0-win-x64-aot`，合计 83,646,284 字节。用户程序包不含 PDB，开发者仍可保留符号分析崩溃。
4. **运行文档精简**：保留使用说明、格式/备份说明、LICENSE、第三方声明及各依赖许可；截图、测试结果、架构审查和开发记录留在源码，不再打入运行包。没有删除背景/图标等运行图片。
5. **可确认未用的 WebView2**：源代码与 XAML 没有 WebView/WebView2 控件或 API 使用。仅对确认为未使用的 WebView2 文件模式处理，并增加源码使用检测；未来增加相关功能时自动保留。CI 不再另行无条件删除这些组件。
6. **运行依赖保留**：Native AOT 编译器自行移除托管运行时载荷；没有手工删 coreclr 来伪造 AOT。WinUI / Windows App SDK 本机 DLL、PRI、WINMD、全部系统 MUI、三份语言包、34 个 CS2 武器 SVG、Steam API DLL 全部保留。未盲删未知 SDK 文件。优化后仍有 32 个 DLL（约 44.69 MiB），主要为必须保留的本机 UI 依赖。
7. **高压缩**：ZIP 使用 7-Zip Deflate `-mx=9 -mfb=258 -mpass=15`，可被常规 ZIP 工具解压。7z 使用 LZMA2 `-mx=9 -md=128m -mfb=273 -ms=on -mmt=2`；编码器根据数据实际采用约 96 MiB 字典并自动应用 BCJ2。试验 64 MiB 配置为 17,386,062 字节，最终配置再减少 47,883 字节。

原 Steam 登录服务、缓存、JWT、DPAPI、CS2 协议和云推送业务代码均未因体积优化而修改。新增离线检查代码只在显式 QA 构建中编译，正式发布关闭 `EnableManagerUiChecks`。

## AOT 真实性与包完整性

- 构建日志出现 `Generating native code`，发布成功，没有编译/裁剪/AOT 告警。
- PE 为 AMD64，COM Descriptor Directory 为 0，无托管 CLI 头。
- dumpbin `/exports` 确认 `DotNetRuntimeDebugHeader` 原生导出，区别于普通 .NET apphost。
- 发布目录不存在 SteamEyaWinUI.dll、AccountManagement.dll、coreclr.dll、clrjit.dll、hostfxr.dll、托管 runtimeconfig/deps 等普通版文件。
- required-file 断言确认 WinUI DLL、应用 PRI、Steam API 和语言/图标资源存在。
- ZIP 与 7z 均通过 7-Zip 完整性测试；7z 解压到全新目录后，296 个文件逐一 SHA-256 与发布目录一致。
- 运行包检查无 PDB、临时/日志文件、测试截图、QA 数据和开发文档。

`scripts/assert-native-aot.ps1` 实现可复用验证。GitHub Actions 新增 MSVC 检查及 AOT 断言，采用统一清理/打包脚本，并发布便携 ZIP 与可用的 7z；本次只修改工作流，没有执行远程 CI 或发布。

## 功能验证及真实账号待测范围

| 功能 | 结果及范围 |
|---|---|
| 启动/页面 | 用户使用正式 AOT ZIP 反馈“页面打开成功”；不据此推断所有页面业务已验证 |
| 卡片、列表、背景、隐私、筛选、多选、收藏/排序、密码显示及复制 | Native AOT QA 的 111 项检查中包含原有 101 项界面检查，10/50/100 账号、明暗主题及不同窗口尺寸通过；本次实际 DPI 150% |
| 密码新增/修改/删除/重载、映射、TXT/CSV/JSON、100 条导入、特殊字符、分组/选中导出、重复策略 | 52 项数据检查单独发布成 Native AOT EXE，全部通过 |
| ZIP 备份/预览/合并/覆盖、背景恢复、异常回滚、损坏文件恢复 | 包含于上述 52 项，全部通过 |
| 原登录基础服务 | 原 JWT 解析/坏输入拒绝、DPAPI 调用用合成输入在 AOT 中通过；不写真实凭证、不实际登录 |
| CS2 离线协议 | 原 Steam/CS2 protobuf 的 varint、fixed32/64、bool、UTF-8、bytes、结束及截断拒绝检查通过 |
| EYA/缓存一键登录、未保存密码账号登录、实际 CS2 联网查询 | 用户明确表示“稍后自行测试”；本次标为未测试，不声称通过 |
| 其他 Windows、100%/200% DPI、完整配装/工坊/云推送业务 | 本次未完成全面实机回归；原业务及资源保留 |

QA 全部用合成账号和测试目录。离线服务检查不会把伪造 JWT 用于登录，也不把其格式校验结果当作真实认证成功。当前代码不主动牺牲功能以减小体积，但未测的实际联网业务不能被自动检查替代。

证据在 `test-evidence/aot/`：52 项数据结果、111 项界面/服务结果、AOT 构建日志、PE 依赖信息、体积 JSON 和截图。界面运行记录 run-20261009-202240-c0b722。早期普通版记录仍保留。

## 文件校验

| 文件 | SHA-256 |
|---|---|
| 原 win-x64.zip | `05A9A3198C799B3A636CCF7F951356621D3922CA017F145DE34FE332EF82CD59` |
| win-x64-aot.zip | `B89AB455CCD65844A8DB4E38E050298B2BCB585FACFB861F53AB4E8CD7C6F011` |
| win-x64-aot.7z | `89E3E281B7C79A8C1F832C16F60C0D0D69C6613A424DCF8609B849555E306AB6` |
| 正式 SteamEyaWinUI.exe | `099D3A9EBBA73B8643E4A127C72F086C1AED512DA52069C0FE60BCAFCD4DCD0C` |

## 重新构建

```powershell
# 必须先安装 .NET 10 SDK、C++ Build Tools 和 Windows SDK
./scripts/publish-enhanced.ps1 -NativeAot
./scripts/assert-native-aot.ps1 -PublishDirectory artifacts/release-win-x64-aot
./scripts/check-manager-ui.ps1 -NativeAot
dotnet publish tests/AccountManagement.Tests/AccountManagement.Tests.csproj -c Release -r win-x64 -p:PublishAot=true -p:OptimizationPreference=Size -o artifacts/data-check-aot
./artifacts/data-check-aot/AccountManagement.Tests.exe
./scripts/package-source.ps1 -Suffix aot
```

7-Zip 不可用时可生成普通 Optimal ZIP；Native AOT 仍严格要求原生工具链。`-NativeAot:$false` 是人工明确选择普通版的独立构建方式，不是 AOT 失败时的回退。
