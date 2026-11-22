# 统一截图交互：拖拽 = 区域，悬停点击 = 窗口吸附

日期：2026-11-23（brainstorming 定稿）
状态：设计已逐节确认

## 背景与目标

现状把「窗口截图」做成了独立交互路径：`WindowPickerDialog` 列表挑窗 → 主窗隐藏 → 目标窗置前 → 等 250ms → `CaptureWindow(hwnd)`（PrintWindow 重捕获）。GUI 全屏则无独立交互（无选框 Enter 即回退本屏整屏）。

目标：**全产品交互只剩一种手势语言**——

- **拖动 = 区域**：按下后位移 ≥ 阈值进入现有拖拽建框逻辑，一切照旧。
- **悬停 + 点击 = 窗口**：未按下时指针所指顶层窗口实时高亮；原地释放（位移 < 阈值）确认该窗口，走 PrintWindow 重捕获语义（用户拍板选 B，完整窗口内容含被遮挡部分）。
- **不设全屏独立交互**：想全屏自己拖或 Enter（既有回退语义保留）；不设「桌面空白 = 整屏」吸附，空白处不高亮、点击无动作。
- `WindowPickerDialog` 退役删除；CLI `window` 模式转发后打开**同一个**带吸附的 overlay。

细节裁决（已确认）：

- 吸附预览只在「无选框」状态激活。已有选框时指针不触发窗口高亮，换抓窗口先 Esc 清框（既有语义）。避免选框操作与窗口点击互相打架。
- Enter / 双击 / Esc 语义全部不变；无选框时 Enter 的优先级：有吸附候选 → 提交窗口；无 → 整屏回退。

## 交互模型与数据流

**打开**：热键/托盘/CLI 转发触发 → 合并流程：主窗隐藏（若可见）→ 枚举显示器、每屏预抓桌面位图 → `EnumerateWindows()` 缓存一次 → N 个 OverlayWindow（共享 `SelectionSession` + 同一份窗口缓存）。

**悬停**：PointerMoved 经 adorner 转发 → OverlayWindow 把本屏 DIP 指针坐标换算成虚拟桌面物理坐标 → `SnapHitTester` 命中 → 结果映射回本屏 DIP 帧写 `SnapPreviewRect`。跨屏窗口在两屏 overlay 各自显示本屏切片。

**按下**：Press 时**冻结**当时命中结果为吸附候选（释放确认的是按下瞬间的窗口，不看释放点——避免手抖滑出边缘时确认到别的窗口）。候选存在则预览保持。

**拖拽**：Move 位移 ≥ 4 DIP → 候选作废、预览清除、`Session.Press` 建框。Session 既有语义完全不动，手势机只并行观察位移。

**确认（窗口）**：Release 未超阈且有冻结候选 → 抛 `OverlayCaptureResult.Window` → 任一 overlay 触发即 `CloseAll`（复用现有 completed 标志）→ 目标窗置前 → 250ms → `CaptureWindow(hwnd)` → 进编辑器。预抓位图 Dispose 时机沿用 E-1 M9「Background 延迟一跳」纪律。

**确认（区域）**：现状不变——Session 选框、工具环/Enter/双击提交、`CaptureRegion` 跨屏拼接。

**取消**：Esc 链不变（清框 → 再 Esc 整体取消）。

## 架构与组件改动

按「Core 管怎么算、App 管怎么接线、平台层不动」拆分：

**Core（新增，全部可纯单测）**

- `SnapHitTester`：输入 Z 序窗口列表（`WindowInfo` 物理矩形）+ 指针物理坐标 → 命中窗口（Z 序第一个矩形包含指针者；无命中/空列表返回 null）。
- 手势分类小状态机 `SnapGesture`：Press 记候选 → Move 位移 ≥ 阈值判「区域拖拽」并作废候选 → Release 未超阈且候选在 → 「窗口确认」。阈值 4 DIP 为常量参数。

**App（接线层）**

- `SelectionAdorner`：新增 `SnapPreviewRect`（渲染专用，非权威态）——高亮边框 + W×H 标签，复用现有标签画笔语言；指针事件照旧只转发，hit-test 结果由 OverlayWindow 写回。
- `OverlayWindow`：打开时缓存窗口列表（多屏 N 实例共享）；持 `SnapHitTester` 命中并写回 adorner；`Confirmed` 事件签名扩为 `OverlayCaptureResult`（`Region(PixelRect)` | `Window(WindowInfo)`）。
- `MainWindow`：`CaptureRegionAsync` / `CaptureWindowAsync` 合并为单个交互流程；窗口确认分支承接原「置前 → 250ms → `CaptureWindow`」时序；`WindowPickerDialog`（axaml + cs）删除；`HotkeyAction.Window` 路由到同一流程。
- CLI：`CliRequestRouter` 的 `Window` case 改触发同一 overlay 交互；`CliArgs` 帮助文案同步；无实例时 `window` 行为维持现状（报错不启动 GUI）。
- Loc：默认提示改文案（如「拖拽选区域 · 点击截窗口」），`Strings.Zh`/`Strings.En` 两表同步加 key。

**平台层零改动**——`IWindowService` 现有接口够用（Bounds 已去 DWM 阴影、Z 序返回、已过滤最小化窗口）。

## 错误处理（裁决点）

- **`CaptureWindow` 抛异常 → 回退裁屏**：置前已做过，直接 `CaptureRegion(窗口物理矩形)` 重抓裁剪兜底，日志记 warn。（偏离 B 语义的降级路径；若不接受降级可改为报 `Status.WindowFailed`。）
- **置前失败 / hwnd 失效**（窗口在确认瞬间被关掉/最小化）：`TryBringToForeground` 静默（既有语义）→ `CaptureWindow` 异常 → 走上面同一条回退。
- **窗口缓存过期**（overlay 存续期间弹出新窗口）：不刷新、不重枚举——截图是瞬时场景，最坏情况对新窗口不吸附，拖拽仍可用。YAGNI。
- **PrintWindow 黑图不做像素检测**：与现 window 模式同等风险，不新增检测逻辑。

## 测试

- **Core 纯单测**：`SnapHitTester`（Z 序优先、包含判定、空列表、屏外指针）；`SnapGesture`（Press 冻结候选、超阈作废、Release 分类、无候选释放无动作）。
- **App headless**（标 `[Collection("HeadlessUi")]`，断言引用 `Loc` 不写死文案）：hover 写回预览矩形、click 抛 Window 结果、drag 抛 Region 结果、Esc 链不回归；窗口确认分支的置前 → 250ms → `CaptureWindow` 时序用 mock `ICaptureService`/`IWindowService` 验证，含回退分支。
- **既有测试**：`WindowPickerDialog` 相关测试删除或改写到新流程；CLI 路由测试随 `window` 语义更新；新 Loc key 由 `LocKeyConsistencyTests` 自动兜底。

## 明确不做

- 不做「桌面空白 = 整屏」吸附。
- 不做窗口缓存刷新/重枚举。
- 不做 PrintWindow 黑图检测。
- 不改 `SelectionSession` 权威选框语义；不改 CLI `full` headless 路径与 `region --geometry` headless 路径。
- 不新增图层/模式切换 UI——交互只有一种。
