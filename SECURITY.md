# 安全政策

仅当前增强版接受修复，当前发布状态以[本仓库 Releases](https://github.com/dbdbvdbdb/OpenSteamEYA-Enhanced/releases/latest)为准。发布或 beta 标记不代表所有真实登录、CS2 游戏效果和联网场景已验收。上游和增强版的安全维护独立。

## 数据边界

管理密码、导出及备份是明文。隐私模式只遮挡界面，不加密文件或限制其他进程读取；复制密码不会自动清空剪贴板。管理库 `%LocalAppData%\OpenSteamEYA-Enhanced` 与原凭据目录 `%AppData%\SteamEYA` 独立，管理备份不包含原 Token/登录缓存，不能恢复全部登录状态。本次开源准备不改变既有凭据处理或密码格式。

## 漏洞报告

不要在公开 Issue、PR 或构建日志提供密码、EYA Token、Steam 会话、共享密钥、真实数据库、备份或个人资料。复现步骤和截图应使用虚构数据。

如仓库已启用 GitHub Private vulnerability reporting，请使用 Security → Report a vulnerability；否则先通过维护者 GitHub 个人页提供的私下渠道确认接收方式。尚未配置专用安全邮箱或响应时限，不能保证上游收到报告。普通功能问题可通过 Issue 提交。

## 发现凭据泄露

1. 停止公开仓库、Release 和资产发布，记录受影响路径、提交和类型，不输出凭据值。
2. 由所有者立即轮换密码/密钥、撤销会话和 Token；删除文件不会使已泄露凭据失效。
3. 备份原仓库后，使用 `git filter-repo` 等清理所有受影响 refs。协调协作者重新克隆，并按托管平台说明处理缓存、PR refs 和发布资产。历史重写和强推需维护者明确授权。
4. 重新扫描完整历史、工作区和发布内容，人工复核后再解除门禁。

## 下载校验

只从审核通过的本仓库 Releases 下载，比对 SHA256SUMS.txt。校验和验证完整性，不代替代码签名或安全审查。当前包未申请维护者 Authenticode 签名。
