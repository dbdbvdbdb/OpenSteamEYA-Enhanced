# v0.3.0 真实应用截图

来源：2026-10-19 本机 Windows x64 Native AOT WinUI QA，批次 `run-20261019-225358-d63c72`。执行 `scripts/check-manager-ui.ps1 -NativeAot`，319 项检查通过，数据检查 238 项通过。

应用标题为 v0.3.0，EXE 文件版本为 0.3.0.0。截图来自本次 v0.3.0 版本修改后的工作树；编译时 Git 基线为 `1a508a68ffc41782105ba5c3ef082b4508b95bdc`，版本文件变更随后随发布准备提交，不能将该基线提交本身当作 v0.3.0 标签。

图片由真实运行的 WinUI `RenderTargetBitmap` 导出，未重绘、拼接或修改内容。使用隔离模板和虚构账号/SteamID，没有真实凭据或 Cloud 操作。旧查看页已从导航移除。界面截图不证明游戏设置效果或联网登录已验收。

均为 PNG，2138 × 1608：

| 文件 | SHA-256 |
| --- | --- |
| `classification-white.png` | `9E7A35A90D6CBEDF64829E2359E041D6C70F5FF83178CAC02C1D24608130AF85` |
| `classification-black.png` | `836FD3E326EDE507104B3C8ED02D4E55DCA8F06C81A8C09FF3A4F6B288A9E565` |
| `cs2-unified-video-advanced-dark.png` | `7DCC57ECDF399DAF31F441368CB97DC8851C3E78A89CAC2B4B08EDCD70C99274` |

日志：[数据检查](../../test-evidence/0.3.0/data-results.txt) · [WinUI 检查](../../test-evidence/0.3.0/ui-results.txt)。原始程序标题、像素尺寸及 QA 测试上下文均保留，不代表已验证所有系统 DPI。
