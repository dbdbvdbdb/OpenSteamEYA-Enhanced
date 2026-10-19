# CS2 配置验证工具

独立 .NET 10 命令行工具，直接链接生产 `Cs2Settings/*.cs`，不依赖 WinUI、不访问登录凭据、不注入游戏。所有写入测试均使用 `--out` 下新建的虚构 Steam 目录。工具不会启动游戏或修改真实配置，也不会把源码的 `Verified` 白名单标记解释成游戏验证通过。

```powershell
./scripts/check-cs2-config-feasibility.ps1
./scripts/check-cs2-config-feasibility.ps1 -NativeAot
dotnet run --project tools/Cs2ConfigVerifier -- catalog --out artifacts/cs2-feasibility/catalog
```

`self-test` 覆盖全部 55 个可编辑设置的模板保存、重新加载、两个虚构账号部署、原后缀保留、未知键保留、remote 不变与事务恢复；另外覆盖全部 67 个按键以及全部白名单绑定动作。结果中的缺陷复现断言通过表示成功复现缺陷，不代表该行为正确。原游戏资源不随工具分发。

## Windows 实机采集

先手动确认 Steam 当前账号为专用测试账号，退出游戏，为该账号完整备份 `730/local/cfg`。不要使用正式应用的“应用到所有账号”按钮操作多账号生产环境。测试期间保留 Steam Cloud 状态，记录其开启情况；工具不负责云同步。

下面的 `<测试账号配置目录>` 必须手动替换为选定账号的 `Steam/userdata/<AccountID>/730/local/cfg`，不能根据昵称猜测。不要将原始配置、SteamID、Steam 账号信息、控制台完整日志上传到公共仓库。

```powershell
dotnet run --project tools/Cs2ConfigVerifier -- snapshot --cfg '<测试账号配置目录>' --stage before --out artifacts/private-trial
# 保存模板并经确认只部署到专用账号后：
dotnet run --project tools/Cs2ConfigVerifier -- snapshot --cfg '<测试账号配置目录>' --stage applied --out artifacts/private-trial
# 手动启动真实 CS2 到主菜单后：
dotnet run --project tools/Cs2ConfigVerifier -- snapshot --cfg '<测试账号配置目录>' --stage started --out artifacts/private-trial
# 在游戏菜单、控制台或客观行为测试中观察实际值，正常退出游戏后：
dotnet run --project tools/Cs2ConfigVerifier -- snapshot --cfg '<测试账号配置目录>' --stage exited --out artifacts/private-trial
# 再次启动并检查实际值；stage restarted。恢复备份后 stage restored。
dotnet run --project tools/Cs2ConfigVerifier -- compare --before artifacts/private-trial/applied.json --after artifacts/private-trial/exited.json --out artifacts/private-trial/diff
```

快照只采集已注册字段及必要视频模式键和文件 SHA-256，刻意不记录自定义绑定命令。即使 `Cs2ProcessPresent=true`，也不能证明游戏加载了快照对应的账号或参数。需另行保存人工确认记录和游戏内证据。`compare` 只比较已持久化值，不判断实际效果。

`observe --input observations.json --out ...` 校验记录完整性，格式为 `GameObservation` 数组，字段见 `Program.cs`。状态只能是“验证通过、部分通过、验证失败、待实机验证、不适用”。结论性状态要求实际启动声明、客户端版本及存在的证据文件；通过还要求实际观察值及重启观察值。工具无法鉴别人为填写的真实性，审核人仍须查看证据，不能将格式校验结果视为验收结论。

## 当前安装包依据（只读）

```powershell
python scripts/capture-cs2-client-evidence.py --game-root '<CS2 安装目录>' --out artifacts/cs2-feasibility/client
```

脚本读取固定白名单 VPK 资源并核对 CRC，输出版本、散列和原生视频预设键值。所有提取文件仅保存在本地忽略目录；这些数据只能证明安装包包含相应资源，不能证明运行时采纳或效果生效。参数完整清单和逐项验收步骤见 `docs/CS2-CONFIG-FEASIBILITY-REPORT.zh-CN.md`。

## 已复核实机记录汇总

`python scripts/render-cs2-runtime-report.py` 将静态目录与已人工复核的 `docs/cs2-feasibility/runtime-observations.json` 合并，生成138行CSV/Markdown；拒绝未知或重复ID、缺失或越界证据路径。它不会根据文件一致性自动判为游戏通过。真实截图和多账号首次重写/重试限制见主报告；主要重启字段对应第一账号；后续Cloud对照只补齐第二账号四项子集，不代表第二账号全部55项重启通过。
