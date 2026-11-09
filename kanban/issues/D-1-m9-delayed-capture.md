# D-1 M9 延时截图

- **优先级**: P1 ｜ **复杂度**: S ｜ **开发者**: cholf5 ｜ **状态**: Done（2026-11-10）
- **依赖**: 无
- **关联**: flameshot `flameshot gui -d <ms>` / capture launcher；winshot capture launcher 延时选项；`PrtscConfig.Hotkeys`；G-26 Capture Launcher（同批合并实现）

## 目标

抓悬浮提示、下拉菜单、右键菜单这类「点别处就消失」的场景——winshot 与 flameshot 都有、prtsc 完全没有的能力。

## 子任务

- [x] 1. config：`CaptureConfig.DelaySeconds`（int，默认 0；合法值 0/3/5，其他值由 `CaptureDelayOptions.Clamp` 钳到最近合法值——钳制逻辑放 Core 纯函数）。
- [x] 2. 托盘菜单「延时截图」子项（3s/5s，走全屏模式）+ 设置页「默认延时」入口（S-1 捕获分区，草稿+Save 模式）。
- [x] 3. 触发路径：热键/菜单 → `DelayedCaptureCountdown`（Core 纯逻辑，可注入调度器）→ 小型无边框置顶倒计时角标窗（主屏 WorkArea 顶部居中）→ 角标关闭等 250ms → 快照 → 进入选框流程；**overlay 与倒计时 UI 不出现在快照里**（角标先关、延迟 ≥250ms 再抓帧，对齐 Hide+Delay(200) 防残影做法）。Window 模式时序：倒计时走完 → 再开 WindowPickerDialog。
- [x] 4. CLI 预留：倒计时状态机是 Core 纯逻辑（无 UI 依赖，L-1 可直接复用）；`--delay` 参数解析按裁决仍归 L-1 实装。

## 验收标准

- 3s 延时内能展开一个下拉菜单并被完整抓入选框；overlay 不污染快照。（待真机冒烟，见 Verification）
- 延时期间再次按热键可取消（已选定「可取消」语义：角标提示文案 `Delay.BadgeCancelHint` = 「按任意截图热键取消」；取消后主窗口状态栏提示 `Status.DelayCanceled`）。托盘/启动器再触发 = 取消旧轮 + 按新意图处理。

## 测试用例

- [x] 倒计时状态机纯逻辑单测（启动/取消/到点触发捕获回调/覆盖/零秒直通/空转 Cancel 安全/逐秒 Tick 序列）——`DelayedCaptureCountdownTests` 8 用例，虚拟调度器不碰真实时间。
- [x] `CaptureDelayOptions.Clamp` 钳制单测（等距取较小：4→3）+ `IndexOf` 下标映射。
- [x] config 往返（`delaySeconds` camelCase 锁定；旧 config 缺字段回默认 0）。
- [ ] 真机冒烟：抓下拉菜单（记入 QA-1）。

## Verification（2026-11-10）

- TDD 先 RED 后 GREEN：Core 新增 32 用例（`DelayedCaptureCountdownTests` 8 + `CaptureDelayOptionsTests` 14 + `RegionRectSpecTests` 10，G-26 区域解析/钳制见该卡）。全量 `dotnet test` 708/708 全绿（Core 483 + App 100×2TFM + Mac 80 + Linux 45）；`dotnet build Prtsc.slnx` 双平台腿编译通过。
- 关键设计：倒计时事件全部 marshal 回 UI 线程（App 层订阅处 `Dispatcher.UIThread.Post`，Core 保持单线程语义不做线程同步）；热键在倒计时期间 = 取消本轮（唯一明确取消手段），托盘/启动器再触发 = 取消旧轮按新意图处理（重启倒计时）——裁决写入 handoff。
- 快照：`artifacts/ui/40-launcher.png`（G-26）。
- **Review pass（同日独立审查后修复）**：C-1（250ms settle 间隙内新延时触发被旧 finisher 提前抓帧、角标入快照）以 Core `CountdownOrchestrator` 轮次裁决修复（pending 绑定轮次号，完成轮次不匹配即作废）；I-1（状态机 Start/Cancel 与线程池回调的并发契约）内部加 Monitor 锁。两处修复都有「去实现必挂、恢复即绿」的确定性回归用例（`CountdownOrchestratorTests` 7 + `DelayedCaptureCountdownConcurrencyTests` 3 + 轮次用例 4 + `Resolve` 纯函数用例，Core 共 +19）。修复后 727/727 全绿（Core 502 + App 100×2TFM + Mac 80 + Linux 45）。详见 handoff §5。
- 待真机冒烟：延时 3s 抓下拉菜单完整入框；倒计时期间按热键取消；角标不在快照中；Windows 真机行为（托盘子菜单、角标置顶、倒计时期间热键可达性）。
