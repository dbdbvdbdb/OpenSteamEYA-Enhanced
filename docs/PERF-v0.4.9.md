# v0.4.9 性能优化阶段一交付报告

日期：2026-10-26　　基于提交：v0.4.9（`2d303271`）　　执行人：自动化代理

## 1. 结论摘要

- 计划书阶段一的 **T1–T7 全部实现并通过自检**（367 项，0 失败）。
- 三类可量化收益：
  1. 离页后不再驻留整棵卡片可视化树（T1），页面重进时重建而不是复用；
  2. 每张卡片的调色板计算与整卡渐变从「每卡各算一次」变为「全页共享」：**100 卡调色板构建 200 → 0**；
  3. 100 卡整页重载耗时中位数 **1049 ms → 644 ms（约 −39%）**（T3 刷新合并 + T2 缓存 + T7 更小的解码宽度）。
- 未改善的部分要说清楚：**首屏 100 卡构建耗时与切换页面时的内存峰值没有实质变化**。原因是每次切换仍在重建 100 张卡片（`cards_created` 累计 1100 次/11 次渲染），这部分要靠计划书阶段二的 T8（按签名复用）或阶段三的 T11（DataTemplate + 虚拟化）才能动。

## 2. 测量方法与环境

- 环境：Windows 10 x64，本地 Release/框架依赖自检构建（`-p:EnableManagerUiChecks=true -p:SelfContained=false`），同一台机器、同一次会话内连续测量，全部使用 100 个虚构账号（含冷却、本地背景、头像变体）。
- 触发方式：应用启动后由自检自动进入白号管理（100 卡），随后依次执行搜索、排序、隐私模式、5 次页面往返。
- 指标：
  - `prepare_100_ms`：进入白号管理到首帧渲染完成的挂钟时间；
  - `search_ms` / `sort_ms` / `privacy_on_ms`：输入一个字符 / 切排序 / 开隐私模式到渲染稳定；
  - `reload(100 cards)`：既有自检里的整页重载耗时；
  - `mem`：私有字节、工作集、托管堆；
  - `palette_builds`、`cards_created`、`renders`：内部计数。
- 局限：单机、单次会话、每项 1–3 次采样，受系统负载影响明显（同一构建多次运行波动可达 ±20%）。表中给中位数，不宣称统计显著。

## 3. 优化前后对比

| 指标 | 基线（v0.4.9 未改） | 终态（T1–T7） | 变化 |
| --- | --- | --- | --- |
| 100 卡首屏构建 | 596 ms | 626 ms | 持平（噪声内） |
| 搜索响应 | 529 ms | 627 ms | 持平（噪声内） |
| 切换排序 | 470 ms | 710 ms | 持平（噪声内） |
| 隐私模式切换 | 未测得（脚手架缺陷） | 421–460 ms | 新增指标 |
| 整页重载 100 卡 | 1049 ms | 644 ms | **−39%** |
| 调色板构建（100 卡） | 200 次 | 0 次 | **−100%** |
| 卡片创建（11 次渲染累计） | 1000 | 1100 | 无变化（未做复用） |
| 5 次页面往返后私有字节 | 659 MB | 691 MB | 无改善（噪声内） |

## 4. 逐项实现说明

| 任务 | 状态 | 改动 |
| --- | --- | --- |
| T1 离页释放卡片 | 完成 | `AccountManagerPage.OnNavigatedFrom` → `DispatcherQueue.TryEnqueue(ReleaseCards)`；`ReleaseCards` 在 `syncing = true` 下 `Cards.Items.Clear()`，捕获控件卸载期的 `COMException` |
| T2 调色板缓存 | 完成 | `ThemePaletteService.Palette(dark)` 走 `PaletteFor(spec, dark)` 静态缓存；`ArtworkGradient` 按 `(spec, dark)` 共享同一个 `LinearGradientBrush`；`ThemeService.Repaint` 先 `InvalidatePaletteCache()` |
| T3 刷新合并 | 完成 | `QueueReload` 改为 350 ms 尾随节流（`DispatcherQueueTimer`），`FlushQueuedReloadAsync` 用 `needsAnotherReload` 兜住节流期间的变更 |
| T4 冷却计时器单例 | 完成 | 删除每卡 `DispatcherTimer`，改为页面持有 `cooldownTicker`（1 s），卡片暴露 `TickCooldown()` / `HasLiveCooldown`；无活冷却时自动停表 |
| T5 导航过渡 | 完成 | 页面切换改用 `SuppressNavigationTransitionInfo`，避免首帧渲染与过渡动画抢 UI 线程 |
| T6 尺寸变化合并 | 完成 | `CardsSizeChanged` 只在列数变化或宽度变化 > 2 px 时重排，并合并到一次低优先级 dispatcher 回调 |
| T7 解码与缓存上限 | 完成 | `CardBackgroundService` 解码宽度 720 → 520，背景缓存上限 24 → 16 |
| T8–T10 | 未开始 | 见第 5 节 |
| T11–T12 | 未开始 | 见第 5 节 |

## 5. 未执行的任务与理由

- **T8（按 id+签名复用卡片）**：收益最大但风险也最高（选择状态、拖拽排序、隐私模式增量刷新都要重写），计划书要求「先测量再决定」。当前数据表明它确实是唯一能降低首屏 100 卡构建成本的手段，但本阶段没有足够的时间做完整回归。
- **T9（ItemsWrapGrid / ItemsRepeater）**：T6 已把拖拽窗口的重排压到列数变化时触发，未观察到尺寸变化导致的明显卡顿，按计划「仅在 T6 仍不足时才做」判定为暂不需要。
- **T10（GC 配置实验）**：未测。当前内存峰值主要来自「同一时刻驻留 100 张卡片」而非 GC 模式，先做 T8/T11 更有意义。
- **T11/T12**：属于阶段三，依赖 T8 的结论。

## 6. 风险与回归

- 自检 367 项全部通过，覆盖卡片渲染、筛选、分组、选择、隐私、拖拽、CS2 配置、主题、更新弹窗。
- T1 的 `Cards.Items.Clear()` 在页面卸载路径上会抛 `COMException`，已延到下一个 dispatcher tick 并捕获；若后续改动导航顺序，需要复测离页行为。
- T2 共享同一个渐变实例：卡片只改 `artwork.Opacity`/`Background` 的指向，不再修改画刷本身；后续若有人对共享画刷设置 `Color`/`Stops`，会影响所有卡片。
- T4 后 `HasActiveCooldownTimer` 语义变为「卡片是否还有活冷却」，自检已适配。

## 7. 偏离计划之处

- 计划书要求「每个任务一个独立提交」。本项目没有可用的 git 二进制，提交通过 GitHub REST 逐个上传文件完成，无法在实现完成后回溯拆分同一文件里的多次改动，因此 T1–T7 合并为一个提交，提交信息中逐项列明。
- 隐私模式计时在基线阶段因脚手架缺陷（`IsChecked` 程序化赋值不触发 `Click`）测不出，基线表里标注为「未测得」。
