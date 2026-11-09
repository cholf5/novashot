# G-26 Capture Launcher（flameshot 式独立启动器窗口）

- **优先级**: P3 ｜ **复杂度**: M ｜ **开发者**: cholf5 ｜ **状态**: Done（2026-11-10）
- **依赖**: 无（与 D-1 合并做，复用其延时咽喉点）
- **关联**: flameshot launcher；`ICaptureService.CaptureRegion`（物理像素、虚拟桌面坐标）；D-1 `DelayedCaptureCountdown`

## 目标

flameshot 式启动器窗口：X/Y/W/H 数值框 + 延时 + 模式单选 + 「开始截图」按钮——给精确区域截图一个不用拖选框的入口。

## 子任务

- [x] 1. 新窗口 `CaptureLauncherWindow`（axaml + 代码后置直连，PinnedWindow 风格）：模式单选（全屏/区域 Radio）；X/Y/W/H 四个数值框（物理像素、虚拟桌面坐标；W/H 必须 >0，非法输入内联红字提示不弹窗不崩溃；越界钳进虚拟桌面）；延时秒下拉（0/3/5，预填 config 默认）；「开始截图」→ 关窗 → 走与 D-1 相同的延时+捕获咽喉点（`App.LaunchCaptureAsync`）。
- [x] 2. 入口：托盘菜单「截图启动器…」一项（主窗口 ⋯ Flyout 未加，托盘为准）。
- [x] 3. 语义对齐 flameshot launcher 精神，不过度设计：区域模式直接 `CaptureRegion` 预置矩形路径（`MainWindow.CapturePresetRegionAsync`），不进 overlay 选框流程。

## 验收标准

- [x] 区域模式输入合法矩形 → 倒计时（若设）→ 快照直接进编辑器；非法输入内联红字；越界自动钳回（Core `RegionRectSpec.ClampToBounds` 纯函数）。
- [x] 全屏模式 → 与托盘全屏截图同路径（可带延时）。
- [ ] 真机冒烟：多屏负坐标区域、越界钳制、非法输入提示。

## 测试用例

- [x] Core：`RegionRectSpecTests` 10 用例（四字段解析/非整数/W·H≤0/负坐标/钳制：界内不动、负界位移、右下越界位移回拉、超尺寸截到整屏、负原点 bounds、空 rect/bounds 兜底、虚拟桌面并集）。
- [x] config 往返与预填映射（`CaptureDelayOptions.IndexOf`）。
- [x] 快照：`artifacts/ui/40-launcher.png`（zh-CN，区域模式默认，主屏尺寸预填）。

## Verification（2026-11-10）

- 与 D-1 同批实现、同 commit；测试数据见 D-1 卡 Verification（全量 708/708 绿）。
- 设计决策：预填主显示器 bounds（不持久化「上次值」——launcher 是精确输入场景，主屏尺寸是最合理起点）；关窗后 200ms 再触发捕获（防本窗残影入快照，与 MainWindow Hide+Delay(200) 同源）；全屏/区域单选默认区域（launcher 的存在意义）。
- 待真机项：多屏负坐标、Windows 托盘子菜单交互、headless 之外的真机显示枚举。
