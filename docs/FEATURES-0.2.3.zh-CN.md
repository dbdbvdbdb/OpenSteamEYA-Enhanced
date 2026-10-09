# OpenSteamEYA Enhanced 0.2.3 — 冷却、查询分组与多分组

日期：2026-10-09。基于 0.2.2，落实用户要求的三项修改及“跳过查询的账号加入未查询分组”的补充。

## 行为

| 场景 | 结果 |
|---|---|
| 有剩余冷却时间 | 卡片和紧凑列表显示实际倒计时，字体黄色 |
| GCVAC 有标记 | 独立显示红色 `GCVAC：有标记` |
| 状态未知 | 显示未知，不用假数据补全 |
| 批量查询报错 | 加入“失败”，移除旧“未查询”标记，保留手动分组 |
| 没有可用查询凭据而跳过 | 加入“未查询”，移除旧“失败”标记，保留手动分组 |
| 查询成功 | 清除“失败”和“未查询”归属，保留手动分组 |
| 主动取消 | 已完成结果保留；被取消和尚未处理的账号不改变分组 |
| 同一查询凭据对应多个选中记录 | 只查询一次，处理范围内记录同步同一个结果 |
| 账号加入多个分组 | 在每个所属分组及默认分组中均显示 |
| 删除一个分组 | 保留账号及其他归属 |

结果分组按名称复用，不重复创建；重命名后下一次对应结果将重新创建原名称分组。单账号查询成功同样清除结果标记，单账号查询失败保留原行为。查询仍串行复用原 QueryAndSaveCsStatusAsync，不读取管理密码作为查询凭据。

冷却使用原历史快照的 RemainingCooldownSeconds/RemainingCooldownText 和 GcVacText，遵循原过期无符号值处理。只有存在有效倒计时锚点的可见卡片启动每秒 UI 计时器，卸载、停止或到期即停，不请求网络，不改变原历史数据。

右键菜单的“移动到分组”更名为“设置账号分组”，支持多选；编辑窗口也使用原生 ListView 多选。默认分组是全部账号视图，不是可删除的额外归属。筛选和多分组导出按并集处理，账号不会重复出现或导出。

## 兼容

文档版本仍为 1；新增可选 groupIds，保留旧 groupId 表示第一个分组。旧账号和备份读取时将单归属归入列表并去重，缺失或 null 数组按旧数据兼容。密码字段、密码转义、明文要求、记录 ID 和凭证关联不改变。保存仍使用原原子文件事务；组引用无效会拒绝写入。完整多分组关系通过 ZIP 备份恢复，合并时映射全部 GUID；普通密码导入继续隔离文件中的分组和关联键。

旧版本只能理解第一个分组，降级后修改可能丢失额外归属。使用 0.2.3 保存多分组数据后，请保持使用支持多分组的版本。

## 修改文件

- AccountManagement：Models.cs、AccountStore.cs、BackupService.cs、ImportExport.cs、AccountQueryBatch.cs；新增 AccountGroups.cs、AccountQueryGrouping.cs。
- WinUI 页面：AccountCard.cs；新增 AccountCardStatus.cs；AccountManagerPage.xaml.cs、AccountManagerDialogs.cs、AccountManagerSelection.cs。
- 检查：AccountManagerUiChecks.cs、ManagerUiChecks.cs；新增 AccountManagerGroupsUiChecks.cs、tests/AccountManagement.Tests/GroupStatusChecks.cs，测试 Program.cs 接入。
- 交付：csproj 和发布/源码/安装器脚本默认版本 0.2.3，README、使用/格式说明、本报告、开发记录及截图证据。

原 Steam 登录、认证、Token、历史写入及 CS2 网络服务没有修改；只有管理页面的单查询成功后增加结果标记清理。原 LoginAsync 保持不变。

## 测试与实机范围

托管 WinUI 检查 220 项通过；Native AOT 数据检查 85 项通过（原 66 项及新增 19 项）。新增检查涵盖旧单归属迁移、重复归属、默认及指定分组、导出并集、密码保持、替换和合并备份映射、分组删除、失败/未查询/成功清理、重复凭据、取消边界、原生多选菜单/编辑保存、实际字色及倒计时刷新/停止。Native AOT 界面结果与发布记录见本报告末尾。

实际截图来自 WinUI RenderTargetBitmap，账号和状态明确使用隔离合成测试数据，不是联网查询结果或生成效果图：[卡片状态](features-0.2.3/main.png)、[紧凑列表](features-0.2.3/list.png)。真实 Steam/CS2 联网仍需用户测试账号验收。当前系统 DPI 为 150%，其他比例仅检查导出渲染尺寸，不宣称四种系统 DPI 实测。QA 代码、合成数据和截图不进入正式运行包。

复测命令：

```powershell
./scripts/check-manager-ui.ps1 -NativeAot
dotnet publish tests/AccountManagement.Tests/AccountManagement.Tests.csproj -c Release -r win-x64 -p:PublishAot=true -o artifacts/data-check-aot
./artifacts/data-check-aot/AccountManagement.Tests.exe
./scripts/publish-enhanced.ps1 -NativeAot -Version 0.2.3
```

## 最终构建和发布

Native AOT WinUI **220 项检查通过**，Native AOT 数据 **85 项通过**。正式和 QA 原生构建均没有编译警告或错误。36 个原业务服务文件 SHA256 不变；原 LoginAsync 保持一致，原 LoginPage 和 SteamAccountHistoryItem 与仓库基线无差异。完整记录在 test-evidence/features-0.2.3。

正式包确认是 x64 原生 PE，不含托管应用、JIT 或运行时宿主；ZIP/7z 完整性检查通过。ZIP 解压出的全部 296 文件哈希均与发布目录相同，程序成功创建响应正常的原生窗口并正常关闭。启动检查没有执行 Steam 登录或查询。运行包保留原 Windows App SDK、WinUI、Steam、资源和许可，排除 PDB、QA 入口、合成数据和测试截图。

| Native AOT 版本 | ZIP MiB | 7z MiB | 解压 MiB | 文件数 |
|---|---:|---:|---:|---:|
| 0.2.2 | 27.07 | 16.58 | 70.24 | 296 |
| 0.2.3 | 27.11 | 16.61 | 70.35 | 296 |

精确字节：ZIP 28,432,135，7z 17,412,006，解压 73,768,619。

```text
OpenSteamEYA-Enhanced-0.2.3-win-x64-aot.zip
SHA256 594D7008E9E0D134FD4894772BAAFF1A5B3F0AAFD9D9578C9885E5A1ADAC2F89
OpenSteamEYA-Enhanced-0.2.3-win-x64-aot.7z
SHA256 8682CAB8A53E535BE591D19238ED91DD12E0BA1863BA343EF10DE68E2A356A52
```

实机建议：给同一账号设置两个分组，分别筛选并在默认分组检查；完成查询后检查失败/未查询标记，成功重试确认移除；取消批量查询确认未开始账号保持原组；用可信的真实冷却/GCVAC 数据查看字色。截图中的合成状态不代表真实账号。
