# 应用内更新系统（in-app update）

面向便携版（ZIP 解压即用）的自更新方案。目标是让用户点一次「立即更新」就能完成覆盖，同时保留可回滚、可校验、失败不砸安装目录的底线。

## 非目标

- 不做增量（delta）更新：便携包约 30 MB，全量下载已经足够快。
- 不做 beta / stable 通道切换（保留字段，暂不暴露 UI）。
- 不做 stub launcher（启动器占坑后再换主程序）。
- 不做 Authenticode 代码签名（依赖免费 OSS 签名服务，单独推进；校验链见下）。

## 发布侧

`scripts/publish-enhanced.ps1` 在产出 ZIP/7z 与 `SHA256SUMS.txt` 之后，额外生成 `update.json`：

```json
{
  "schema": 1,
  "version": "0.4.5",
  "tag": "v0.4.5",
  "minVersion": "0.4.0",
  "publishedAt": "2026-10-22T00:00:00Z",
  "assetName": "OpenSteamEYA-Enhanced-0.4.5-win-x64-aot.zip",
  "size": 30021730,
  "sha256": "…",
  "url": "https://github.com/dbdbvdbdb/OpenSteamEYA-Enhanced/releases/download/v0.4.5/OpenSteamEYA-Enhanced-0.4.5-win-x64-aot.zip",
  "notesUrl": "https://github.com/dbdbvdbdb/OpenSteamEYA-Enhanced/releases/tag/v0.4.5",
  "mandatory": false
}
```

`sha256` 与 `size` 由脚本对已生成的 ZIP 计算，发布工作流把 `update.json` 与 ZIP/7z/`SHA256SUMS.txt` 一起上传。工作流最后一步会重新计算 ZIP 摘要并与 `update.json` 对比，不一致则发布失败。

## 校验链

1. **GitHub 资产摘要**：GitHub Release API 对每个资产返回 `digest: sha256:…`，由 GitHub 服务端计算，可作为第一道完整性依据。
2. **`update.json` 与 ZIP 的 sha256 一致性**：清单里的摘要必须与下载完成后的实际摘要相等，且与（存在时的）GitHub digest 相等。
3. **清单签名（可选，默认关闭）**：用 ECDSA P-256 对 `update.json` 原文签名，公钥内置在客户端（`UpdateSigningPublicKey` 常量），签名文件 `update.json.sig`。不引入第三方依赖，AOT 友好。未配置公钥时跳过该项并在界面标注「未签名」。

任何一项不通过都删除已下载文件并报错，不进入覆盖阶段。

## 客户端流程

1. `GitHubUpdateService` 判定有新版并拿到资产 URL / 大小 / GitHub digest（已有逻辑）。
2. `PortableUpdateService` 先尝试取同一 tag 下的 `update.json`；取不到或解析失败时，用第 1 步的数据合成等价清单（兼容没有清单的旧版本）。
3. 下载到 `%LOCALAPPDATA%\SteamEYA\updates\<version>\package.zip`，支持断点续传：已有 `.part` 文件时带 `Range` 请求，服务端返回 206 继续写，返回 200 则重下；完成后整体校验。
4. 解压到 `%LOCALAPPDATA%\SteamEYA\updates\<version>\app\`，逐条拒绝 `..`、绝对路径、盘符、符号链接与可执行白名单外的异常扩展名。
5. 写出 `pending.json`（版本、包路径、解压目录、sha256），由 UI 提示「重启并更新」。

## 覆盖流程（OSEUpdater）

主程序退出后由一个独立的小程序完成替换，避免自己覆盖自己的文件锁问题。

```
OSEUpdater.exe --pid <主程序PID> --source <staging\app> --target <安装目录>
               --restart SteamEyaWinUI.exe --backup <安装目录>\..\backup\<version>
               --log <日志路径> [--dry-run] [--verify-only] [--simulate-failure-at <n>]
```

步骤：

1. 等待 `--pid` 退出（默认 60 秒，超时则报错退出，不覆盖）。
2. `--verify-only`：只校验源目录完整性后退出；`--dry-run`：打印将要替换的文件清单后退出。
3. 备份目标目录中将被替换的文件到 `backup\<version>\`（保留最近两份，更早的清理）。
4. 逐个复制：先写 `*.ose-new`，再 `File.Move(..., overwrite: true)`；保留目标目录里包内没有的文件（例如 `steam_appid.txt`、用户自建文件），不执行删除。
5. 任一步失败：把已替换文件从备份回滚，写出失败日志，退出码 2，不启动新版本。
6. 成功：写出 `last-update.json`（从哪个版本到哪个版本、时间），启动新版本，退出码 0。

## 失败与边界

- 安装目录只读或被杀软占用：覆盖前先做可写探测（创建并删除临时文件），失败则回退到「下载完成，请手动解压替换」并提供打开文件夹按钮。
- 磁盘空间不足：下载前检查可用空间 ≥ 压缩包大小 × 2.5。
- 下载中断：保留 `.part`，下次继续；校验失败则清空重来。
- 关机/强杀发生在覆盖中途：下次启动检测 `pending.json` 与 `last-update.json` 不一致时，提示「更新未完成，请重新下载」。
- 跨版本升级：`minVersion` 高于当前版本时要求先升级到中间版本（避免结构性变更）。

## 自检

- `OSEUpdater --self-test`：在临时目录构造「旧版本树 / 新版本树」，验证替换、保留额外文件、失败回滚、ZipSlip 拒绝四类场景，全部通过才返回 0。CI 的 `check-data.ps1` 调用它。
- UI 自检：更新弹窗的三态（发现新版本 / 下载中 / 已就绪）与「跳过此版本」持久化。
