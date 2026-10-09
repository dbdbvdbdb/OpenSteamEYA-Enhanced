# 贡献指南

保持现有功能和 Steam 凭据行为。未经明确设计评审，不修改明文密码要求、密码文件格式、登录/Token 服务、批量导入导出和备份语义。

## 开发与验证

环境及命令见 [README](README.md)。修改依赖时检查实际 NuGet 包内许可证，重新生成并审阅 `packages.lock.json`，不要只依据包名或仓库 MIT 标识判断再分发权。

数据测试是控制台程序，执行 `scripts/check-data.ps1` 编译并运行 Native AOT EXE；`dotnet test` 不会执行这些断言。`scripts/check-manager-ui.ps1 -NativeAot` 使用隔离合成数据运行真实 WinUI 检查并导出截图，需要图形会话。正式包使用 `EnableManagerUiChecks=false` 排除 QA 入口。

- 提交前检查 diff/status，不提交数据库、导出、备份、Token、个人路径或编译产物。`.gitignore` 不清理已有 Git 历史。
- 只使用明确虚构账号/密码；新增图像和库须提供来源及许可，更新 THIRD_PARTY_NOTICES.md，保留原作者声明。
- PR 描述问题、改动及验证。UI 变化附合成数据真实截图，说明未执行的系统 DPI、主题和联网测试。
- 用脱敏模式扫描全部 refs：`gitleaks git --log-opts="--all --full-history" --redact=100`；工作区用 `gitleaks dir --redact=100 .`。补充执行 `python scripts/audit-source.py --report artifacts/source-audit.json`。人工复核命中，不用宽泛白名单掩盖真实凭据。

## 发布

`docs/release-readiness.json` 是公开门禁。未解决授权项保持 pending，不能为通过 CI 改成 resolved；解决时提供可核验条款/授权并更新报告。免责声明不代替许可。

Release 流程接受 `0.2.4-beta`，分离数字编译版本和包标签，运行 AOT、测试、许可证归档和 ZIP 内容哈希核对后生成草稿预发布。公开前报告真实 Steam 登录、CS2 联网及其他 Windows 系统的验收状态；未测项必须在说明中列出。不自动创建正式版、标记 Latest 或覆盖资产。

只贡献有权提供的内容。项目代码使用 MIT，第三方图像和运行组件不因此改用 MIT。
