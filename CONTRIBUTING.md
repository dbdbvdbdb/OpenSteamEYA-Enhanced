# 贡献指南

保持现有功能和 Steam 凭据行为。未经明确设计评审，不修改明文密码要求、密码文件格式、登录/Token 服务、批量导入导出和备份语义。

## 开发与验证

环境及命令见[源码编译与测试](docs/BUILDING.zh-CN.md)。修改依赖时检查实际 NuGet 包内许可证，重新生成并审阅 `packages.lock.json`，不要只依据包名或仓库 MIT 标识判断再分发权。

数据测试是控制台程序，执行 `scripts/check-data.ps1` 编译并运行 Native AOT EXE；`dotnet test` 不会执行这些断言。`scripts/check-manager-ui.ps1 -NativeAot` 使用隔离合成数据运行真实 WinUI 检查并导出截图，需要图形会话。正式包使用 `EnableManagerUiChecks=false` 排除 QA 入口。

- 提交前检查 diff/status，不提交数据库、导出、备份、Token、个人路径或编译产物。`.gitignore` 不清理已有 Git 历史。
- 只使用明确虚构账号/密码；新增图像和库须提供来源及许可，更新 THIRD_PARTY_NOTICES.md，保留原作者声明。
- PR 描述问题、改动及验证。UI 变化附合成数据真实截图，说明未执行的系统 DPI、主题和联网测试。
- 用脱敏模式扫描全部 refs：`gitleaks git --log-opts="--all --full-history" --redact=100`；工作区用 `gitleaks dir --redact=100 .`。补充执行 `python scripts/audit-source.py --report artifacts/source-audit.json`。人工复核命中，不用宽泛白名单掩盖真实凭据。

## 发布

发布前同步检查 README、README_EN 与 CHANGELOG：版本、下载入口、截图标注及用户可感知变化应与待发布版本一致。更新记录只维护根 CHANGELOG；保留历史日期、beta 状态和未验收限制，不把本地交付写成公开 Release。

`docs/release-readiness.json` 记录维护者发布准备审核结果，当前三项资源审查已由维护者确认完成。以后新增或变更依赖、资源时重新审阅来源与适用条款并更新记录，保留第三方版权声明。准备审核通过不代表已执行 Release 发布。

Release 流程分离数字编译版本和发布标签，运行 AOT、测试、许可证归档和 ZIP 内容哈希核对后生成草稿预发布。公开前报告真实 Steam 登录、CS2 联网及其他 Windows 系统的验收状态；未测项必须在说明中列出。不自动创建正式版、标记 Latest 或覆盖资产。

只贡献有权提供的内容。项目代码使用 MIT，第三方图像和运行组件不因此改用 MIT。
