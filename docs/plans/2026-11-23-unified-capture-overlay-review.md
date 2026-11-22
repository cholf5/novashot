# 统一截图交互 · 代码审查记录

日期：2026-11-23（com-requesting-code-review）
设计稿：[2026-11-23-unified-capture-overlay-design.md](2026-11-23-unified-capture-overlay-design.md)

## 统计

- 审查文件：13（Core 新增 2 + App 改 7 + 测试 3/删 4，含两份新测试文件全读）
- 问题：P0 = 0，P1 = 0，P2 = 1（已修复）

## 问题列表

### F-1（P2 · 复用性）坐标映射公式双份 → 已修复

`OverlayWindow.LocalToPhysical` / `PhysicalToLocalDipRect` 与 `SelectionSession` 私有静态是同一
公式的两份拷贝。B-3 教训：物理↔DIP 帧映射是本项目最易漂移的数学，且单屏开发机测不出跨屏错位。

**修复**：提取为 `SelectionGeometry.LocalToPhysical` / `SelectionGeometry.PhysicalToLocalDipRect`
（`SelectionGeometry` 本就是选框几何单源），Session 与 OverlayWindow 全部改调，公式只剩一份。

## 已核对的非问题（审查中确认，不修）

- **点击确认与区域确认互斥性**：手势阈值 = `MinSize × scale`，与 Session「<MinSize 清框」门限
  对齐——拖出有效框必已越阈（区域），没成框的释放仍是点击（窗口/无动作）。
- **冻结候选的时序**：Press 时冻结命中、释放确认按下点所在窗口；越阈作废；捕获丢失（Alt-tab）
  走 `SnapCaptureLost` 复位，不误判成点击。
- **多屏竞态**：任一 overlay 确认即 CloseAll（复用 completed 标志）；窗口缓存多屏共享一份。
- **死 hwnd 回退**：确认瞬间窗口被关/最小化 → TryBringToForeground 静默 → CaptureWindow 抛 →
  回退 `CaptureRegion(窗口矩形)`（设计稿错误处理节）。
- **取色模式互斥**：EnterPickMode 清吸附态，adorner IsHitTestVisible=false 时无指针事件流。
- **工具环按钮点击**：ring 按钮处理 click 后事件不冒泡到 adorner，且环只在有选框时可见。

## 验证

- 构建 0 error 0 warning（TreatWarningsAsErrors 腿）。
- Core 802/802 ✓；App windows TFM 231/231 ✓；App net10.0 225/225 ✓（复跑绿）。
- Mac 27 失败 / Linux 1 失败：干净 main 基线（stash 对照）完全一致——Windows 宿主跑 mac/linux
  腿的预存平台语义（CoreFoundation 加载 / Path 分隔符），与本次改动无关。
- 已知预存 flake：`HeadlessUnitTestSession.Dispose` 内偶发 NRE（Avalonia headless），受害测试
  随机轮换、无断言失败；干净 main 基线 6 跑 2 败，与本改动无关（AGENTS.md 已记载同源问题）。
