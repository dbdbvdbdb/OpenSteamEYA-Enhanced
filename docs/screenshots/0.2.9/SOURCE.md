# v0.2.9 统一配置截图来源

`cs2-unified-video-advanced-dark.png` 为真实 WinUI QA 运行导出的原图，未修改图像。它展示隔离模板中的视频选项，没有真实 SteamID、密码、Token 或用户目录；模板参考值不代表硬件推荐值或已完成游戏实测。

- 来源：[Windows Native AOT CI 39106152857](https://github.com/dbdbvdbdb/OpenSteamEYA-Enhanced/actions/runs/39106152857)，产物 `cs2-unified-ui`。
- 源提交：`39212fa6e4d2c23c55b0038fddd1d94dbd01c663`；其 `SteamEyaWinUI` 应用源码与发布标签 `v0.2.9`（`453ddafad244b0cc6af7a3a92f3013abc2451375`）一致。该分支另有独立验证工具与文档，不把它们当成新发布功能。
- 原始 QA 批次：`run-20261018-070552-2a3ca7`，2026-10-18。
- 真实程序标题版本：v0.2.9；测试使用隔离模板及合成账号目录，没有使用 Steam 会话、凭据或 Cloud API。
- SHA-256：`3C6F3321971F11045284DD1AC049477FA3574A626757BB6D898303EA112204B0`。

CI 产物可能到期，本仓库内保留该图片；可用原提交重新运行 `scripts/check-manager-ui.ps1 -NativeAot` 复核。自动化渲染不代替参数的游戏内效果或硬件兼容性验收。
