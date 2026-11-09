# Prtsc 交接备忘 — 2026-11-10 D-1 延时截图 + G-26 Capture Launcher（同批合并）

> D-1（P1）与 G-26（P3，backlog 明确「与 D-1 合并做」）一个 commit 交付；结构仿 `261102-s1-settings-handoff.md`。

## 0. TL;DR

- **D-1 完成**：延时截图全链路——config `Capture.DelaySeconds`（0/3/5，非法值 Core 钳制）→ 托盘「延时截图」子菜单（3s/5s 全屏）+ 设置页「默认延时」→ Core 倒计时状态机 `DelayedCaptureCountdown`（可注入调度器，TDD）→ 无边框置顶角标窗（剩余秒数 + 取消提示）→ 到点关角标等 250ms 再抓帧 → 走三种模式既有流程。**overlay 与倒计时 UI 不进快照**（角标先关 + ≥250ms 间隙）。
- **G-26 完成**：`CaptureLauncherWindow`（模式单选 + X/Y/W/H + 延时下拉 + 开始截图），Core `RegionRectSpec` 纯函数做解析与虚拟桌面钳制；区域模式走新的 `MainWindow.CapturePresetRegionAsync(PixelRect)` 直抓路径（不经 overlay）。入口 = 托盘「截图启动器…」（主窗口 ⋯ Flyout 未加，托盘为准）。
- **取消语义（验收裁决）**：倒计时期间按任意截图热键 = **取消本轮**（不启动新一轮），角标提示 + 主窗状态栏提示；托盘/启动器再触发 = 取消旧轮 + 按新意图处理（延时子项即重启倒计时，Launcher 选 0 秒即直接抓）。
- **CLI 预留（L-1 复用点）**：`Prtsc.Core.Capture.DelayedCaptureCountdown` + `ICountdownScheduler`（+ 生产实现 `CountdownScheduler`）是纯 Core、无 UI 依赖——headless CLI 只需 `new DelayedCaptureCountdown(CountdownScheduler.Instance)`，`Completed` 里直接调 `ICaptureService` 抓帧即可复刻 `--delay`；秒数钳制用 `CaptureDelayOptions.Clamp`（合法值表 `AllowedSeconds = {0,3,5}`）。
- **Review pass（2026-11-10 独立审查，见 §5）**：C-1（250ms 间隙竞态）以 Core `CountdownOrchestrator` 轮次裁决修复，I-1（状态机线程契约）加锁修复；两处修复均有去实现验证过的确定性回归用例。测试 727/727 全绿（Core 502 + App 100×2TFM + Mac 80 + Linux 45）。
- **CLI 预留（L-1 复用点）**：`Prtsc.Core.Capture.CountdownOrchestrator<TIntent>`（编排器：pending 轮次绑定 + settle 裁决 + 分发）+ `DelayedCaptureCountdown`（状态机，已加锁）——headless CLI 只需构造 orchestrator（dispatch = 抓帧落盘）、把 Completed marshal 到自己的上下文调 `OnCompletedAsync(round)` 即可复刻 `--delay`；秒数钳制用 `CaptureDelayOptions.Clamp/Resolve`。
- 测试 727/727 全绿（Core 502 + App 100×2TFM + Mac 80 + Linux 45）；快照新增 `artifacts/ui/40-launcher.png`。Windows/Linux 腿编译通过（`dotnet build Prtsc.slnx`），真机行为未验证（见 §3）。

## 1. 文件地图（本批增量）

```
src/Prtsc.Core/Capture/DelayedCaptureCountdown.cs  状态机（Start/Cancel/Started/Tick/Completed(带轮次)/Cancelled；内部加锁，review I-1）+ ICountdownScheduler/ICountdownToken + CountdownScheduler(System.Threading.Timer)
src/Prtsc.Core/Capture/CountdownOrchestrator.cs    编排器：pending 意图轮次绑定 + settle 间隙裁决 + 分发回调（review C-1 修复主场，Core 单测钉死）
src/Prtsc.Core/Capture/CaptureDelayOptions.cs      合法值表 {0,3,5} + Clamp（等距取较小）+ IndexOf + Resolve（review M-2 抽出的延时解析纯函数）
src/Prtsc.Core/Capture/RegionRectSpec.cs           G-26：TryParse（整数/W·H>0）+ ClampToBounds + VirtualDesktopBounds
src/Prtsc.Core/Config/PrtscConfig.cs               CaptureConfig +DelaySeconds（默认 0）
src/Prtsc.App/App.axaml.cs                         咽喉点 TriggerCaptureAsync 加 source/delaySeconds/region 参数；倒计时接线（事件全 marshal 回 UI 线程）；_pendingCapture 意图挂起；LaunchCaptureAsync（Launcher 公开入口）；托盘子菜单 + Launcher 单实例入口
src/Prtsc.App/Views/CountdownBadgeWindow.axaml(.cs) 角标窗：主屏 WorkArea 顶部居中（WorkArea 已排除任务栏/菜单栏）
src/Prtsc.App/Views/CaptureLauncherWindow.axaml(.cs) 启动器窗：代码后置直连（无 VM）；关窗后 200ms 再触发
src/Prtsc.App/Views/MainWindow.axaml.cs            +CapturePresetRegionAsync（预置矩形直抓）+ShowStatus（App 层状态提示）
src/Prtsc.App/ViewModels/SettingsViewModel.cs      +CaptureDelayIndex（Load 用 IndexOf 钳历史值；Save 写 AllowedSeconds[index]）
src/Prtsc.App/Views/SettingsWindow.axaml           捕获分区 +「默认延时」下拉
src/Prtsc.App/Localization/Loc.cs                  +Tray.DelayedCapture* / Tray.CaptureLauncher / Launcher.* / Delay.BadgeCancelHint / Status.DelayCanceled / Status.RegionFailed / Settings.Capture.Delay*（×2 语言表）
tests/Prtsc.Core.Tests/Capture/                    DelayedCaptureCountdownTests(12) + DelayedCaptureCountdownConcurrencyTests(3, I-1) + CountdownOrchestratorTests(7, C-1) + CaptureDelayOptionsTests(20 含 Resolve) + RegionRectSpecTests(10)
tests/Prtsc.App.Tests/SettingsViewModelTests.cs    +延时下标映射/加载 3 用例
tests/Prtsc.App.Tests/UiSnapshotTests.cs           +40-launcher.png
kanban/issues/D-1-m9-delayed-capture.md            Done + Verification
kanban/issues/G-26-capture-launcher.md             新建（Done）
```

## 2. 关键坑（新会话必读）

21. **`PixelRect` 在 App 工程有歧义**：`Avalonia.PixelRect`（DIP）vs `Prtsc.Core.Common.PixelRect`（物理像素）。App.axaml.cs / CaptureLauncherWindow.axaml.cs 用 `using PixelRect = Prtsc.Core.Common.PixelRect;` 别名——捕获几何永远用 Core 的那个。
22. **倒计时线程模型**：Core 状态机是单线程语义（无锁）；`CountdownScheduler` 的 `System.Threading.Timer` 回调在线程池，App 在订阅处统一 `Dispatcher.UIThread.Post` 回 UI 线程。谁复用（L-1 CLI）都要保证 Start/Cancel 与回调同上下文串行，否则 IsRunning 防线只兜「迟到回调」不兜并发写。
23. **`Start` 覆盖 vs `Cancel`**：覆盖前一轮**不触发** `Cancelled`（App 逻辑靠自己区分来源）；`Cancelled` 只在显式 Cancel 且 Running 时发。已销毁 token 的迟到回调由状态机 `IsRunning` 守卫；每 tick 的 token 用完即 Dispose（测试里抓过一次泄漏）。
24. **到点后的 250ms 间隙竞态（review C-1，已修）**：settle 间隙内的新触发有三条路，各自的防线——① delay=0 直抓：`TriggerCaptureAsync` else 分支先 `ClearPending()`，旧 finisher 恢复后 pending 为空作废；② 带延时开新一轮（覆盖 pending、角标重现）：finisher 入口 `IsRunning` 防线（不误关新轮角标）+ 恢复后**轮次号比对**（pending.Round ≠ 完成轮次即作废，C-1 的正解）；③ 间隙内热键取消：`Cancel()` 清 pending。轮次号由 Core 状态机 `Start` 递增、`Completed` 事件携带，绑定逻辑在 `CountdownOrchestrator`（Core，单测钉死）。不要试图在 App 层手工维护这套状态。
25. **NativeMenuItem 子菜单**：`new NativeMenuItem(title) { Menu = new NativeMenu() }`；Text 是普通字符串，切语言靠 `RebuildTrayMenu` 整表重建（既有机制，新增项自动跟随）。
26. **角标定位用 WorkArea 不是 Bounds**：macOS 顶部有菜单栏、Windows 任务栏可能在顶部，`DisplayInfo.WorkArea` 两边都已排除；物理像素坐标直接给 `Window.Position`（PixelPoint 就是物理像素）。
27. **Launcher 关窗时机**：`Close()` 后 `Task.Delay(200)` 才触发捕获（本窗残影入快照的风险与 Hide+Delay(200) 同源）；delay>0 时反正还有倒计时+250ms，双保险。
28. **`Dispatcher.UIThread.Post(async () => …)`** 是 async void——异常会炸进程，DispatchCaptureAsync 各分支内部已有 catch 兜底（review pass 后 `FinishCountdownAndCapture` 已删除，倒计时完成路径同样收敛到 DispatchCaptureAsync）。

## 3. 诚实清单（未经真机验证）

1. **macOS 真机**（本机为 darwin，只跑了单测）：延时 3s 抓下拉菜单完整入框；倒计时期间按全局热键取消是否即时；角标置顶显示与 250ms 后真正从画面消失。
2. **Windows 真机从没跑过**（延续 S-1 现状）：托盘子菜单形态、WGC 抓帧时角标是否真的不在画面里（250ms 对 DWM 合成是否足够）、全局热键在倒计时期间可达性。
3. **Linux**：托盘菜单子菜单支持度（appindicator 对 nested menu 一般没问题）未验证。
4. 多屏混合 DPI 下角标居中估算（EstimatedWidthDip=150）可能略偏，功能无碍。
5. 设置页「默认延时」改 3s 后，**所有**热键/托盘捕获都带延时（这是设计：全局默认）——用户可能误以为只影响「延时截图」子项，文案已写进 DelayHint，真机体验再看。
6. Region 模式倒计时结束才开 overlay：倒计时期间屏幕上的角标不在 overlay 抓帧里（overlay 抓帧发生在角标关闭后），已由时序保证，真机确认一遍。

## 4. 下一步

- QA-1 真机冒烟补 D-1 项（延时抓菜单 / 热键取消 / 角标不入快照）与 G-26 项（越界钳制 / 多屏负坐标）。
- L-1 CLI：复用点见 §0；`--delay` 语义 = 秒（0/3/5 之外的值按 `CaptureDelayOptions.Clamp` 钳）。
- 设置页「默认延时」与托盘子项的关系（子项显式覆盖全局默认）已在实现中成立，无需再动。

## 5. Review pass（2026-11-10 独立审查后修复，`fix(app): D-1 review pass` commit）

独立审查对 `429dbb5` 给出 1 Critical + 1 Important + 4 Minor。修复情况：

### C-1（Critical）：250ms 间隙竞态 → 提前抓帧 + 角标入快照

- **场景**：round1 到点 → finisher 进入 settle（250ms）→ 间隙内用户再次触发**带延时**捕获（热键 + config 默认延时>0 / 托盘延时子项 / Launcher）→ 新轮覆盖 pending 并显示角标 → 旧 finisher 恢复后读到**新轮意图**立即抓帧——捕获提前数秒，且此刻新轮角标在屏幕上 → **角标入快照**，违反验收标准。config 默认延时设为 3s 后，倒计时结束双击热键即可复现（非边缘）。
- **修复**：pending 意图绑定**轮次号**（审查建议的「更稳方案」）。轮次号由 Core 状态机维护（每次 `Start` 递增，`Completed` 事件携带所属轮次）；新 `CountdownOrchestrator<TIntent>`（Core）接管 pending 绑定 + settle 裁决 + 分发。三重防线：finisher 入口 `IsRunning` 检查（新轮已接管 → 直接作废，不误关新轮角标）→ settle → `pending.Round == 完成轮次` 才分发。App 层不再手工持有 pending（`FinishCountdownAndCapture` 整体删除）。
- **验证（有牙）**：`CountdownOrchestratorTests.SettleGap_OverwrittenByNewDelayedRound_OldFinisherDoesNotDispatch` 以可控门闩决定性复现该竞态；**临时删掉轮次比对重跑 → 用例失败（Assert.Empty 失败）→ 恢复 → 绿**，证明用例钉得住。App 层已无可测竞态逻辑残留（全部下沉 Core），符合审查「App 防护逻辑尽量抽纯函数测」的要求（比抽纯函数更进一步：整体下沉 + 全量单测）。

### I-1（Important）：状态机线程契约自相矛盾（Start/Cancel 在 UI 线程、OnTick 在线程池）

- **修复**：`DelayedCaptureCountdown` 内部加一把 Monitor 锁——Start/Cancel/OnTick 的状态迁移与事件触发全部原子；属性 getter 同样走锁。可依赖的契约：Cancel 返回后本轮零事件；Completed 每轮至多一次；事件处理方不得阻塞等待依赖本锁的线程（Monitor 可重入，handler 内再入 Start/Cancel 安全）。`ICountdownScheduler` 注释改为如实描述（回调在任意线程池线程）。
- **验证（有牙）**：`EventHandlers_RunUnderLock_CancelWaitsForInFlightHandler`——慢 Tick handler + 并发 Cancel，断言 Cancel 必须等 handler 完成（锁内事件触发 ⇒ 状态迁移原子）；**临时去锁重跑 → 必挂 → 恢复 → 绿**。注意：锁被删的另一种情形（仅靠 IsRunning 防线）在纳秒级碰撞窗口下无法经验复现（无内部 hook），由该原子性断言 + 代码结构（全部迁移共用一把锁）覆盖；并发敲打用例 `Cancel_ConcurrentWithTicks_*` 能钉住「IsRunning 防线被删」这类回归。

### 顺手项（M-2 的可测性部分，审查欢迎、未扩大范围）

- 延时解析抽成 Core 纯函数 `CaptureDelayOptions.Resolve(explicit, configured)`，App 咽喉点改用；用例 +6。
- Minor 四条（M-1 角标 Windows 黑角 / M-2 咽喉点全量覆盖 / M-3 Loc key 风格 / M-4 冗余防御）按裁决由 PM 记录，本批未修（M-2 的「finish 竞态可测」与「延时解析纯函数」两个点已随 C-1/顺手项完成）。

### Review pass 后测试

`dotnet build Prtsc.slnx` 0 Error；Core 502 + App 100×2TFM + Mac 80 + Linux 45 = 727/727 全绿。存量 flake（`SettingsWindowXamlTests`、`OverlayWindowInputTests`，headless collection 并行偶发）复现过两次，单跑恒绿、干净基线同样偶发，与本批无关（S-1 handoff 已有记录）。
