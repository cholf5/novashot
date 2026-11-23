# 设计：画布元素选中与 Inspector 属性面板（2026-11-23）

> 状态：设计已与用户逐节确认。
> 关联：`docs/plans/2026-11-23-unified-capture-overlay-design.md`（上一代交互重构）；kanban 里 M4c 规划过 z-order / 选择集。

## 背景与问题

Select 工具（V）与右侧 StylePanel 已存在：选中后可拖动/缩放/删除/改颜色与描边宽。
但实际使用暴露两个断层：

1. **画完没法直接改**：绘制工具 commit 后工具不切走，用户想点刚画的箭头时只会再画一个，
   必须手动切 V 才能选中——这是「无法选中并修改样式」的真实来源（机制在，交互断了）。
2. **属性面板太薄**：右侧只有颜色/描边宽/字号（文本专属）；无几何（X/Y/宽/高）、
   无类型专属参数（马赛克颗粒度）、无层序操作，也看不到画布上有哪些元素。

用户选择**方案 A（全套）**：画完即选中 + 左侧元素列表（Flameshot 式，Unity Hierarchy 隐喻）
+ 右侧 Inspector 双态面板（Unity Inspector 隐喻）。

## 交互规则（第 1 节，已确认）

- **画完即选中**：任何绘制工具 commit 成功后，新标注自动成为 Selection；工具不切走，
  连续绘制不受影响；右侧面板立即反映新标注属性。
- **Selection 跨工具存活**：删除 `OnCurrentToolChanged` 的切工具清空逻辑。选中态只在
  三种情况消失：Select 工具下点空白处 / Delete 删除 / 新一笔 commit 覆盖。
- **手柄与选中框只在 Select 工具下渲染**；绘制工具下选中的元素画**淡色虚线选中框**
  （选中色 ~60% 透明、1.5px、纯视觉），不参与命中测试，画笔落点不受影响。
- **列表点击某行 = 选中该标注 + 自动切到 Select 工具**（「我要操作它」的强意图）。
- 砍掉：Alt+点选穿透、Esc 清选择（Esc 在编辑器另有全局语义，不碰）。

## Core 层改动（第 2 节，已确认）

全部落在 `Novashot.Core`，不引 Avalonia，纯单测覆盖。

- **`ReorderAnnotationCommand`**（Annotation/History/）：annotation 从 fromIndex 移到 toIndex，
  Do/Undo 对称；配套 `AnnotationDocument.Move(int from, int to)`（内部 Remove+Insert，
  触发 Changed）。
- **几何数值编辑复用既有命令**：
  - X/Y 输入 → delta = 新值 − BoundingBox 左上 → `MoveAnnotationCommand`
  - W/H 输入 → SE 手柄移到目标点 → `ResizeAnnotationCommand`
  - 宽/高仅盒状族：Rectangle / Ellipse / Spotlight / Mosaic / Blur / Invert（8 向盒子
    手柄语义）；Text / Number 只有 Center 手柄（实施期核实）、开放形状（Line / Arrow×2 /
    Pencil / Highlighter）是端点/路径语义——这些类型 W/H **只读**，X/Y 仍可编辑
    （MoveBy 全类型通用）。
  - Clamp：位置限制在画布范围内，宽高下限 4px。
- **类型专属属性**（不进 Undo，与颜色/描边同策略，`NotifyMutated` 触发重绘）：
  - Mosaic / Blur：`BlockSize` 滑条（4-48，setter 已存在）
  - Text：字号（已有）、双击画布改内容（已有）
- **`EditorViewModel`**：删切工具清空；commit 后设 Selection；新增 `ElementList`
  集合（Document.Changed 驱动增删、Selection 驱动高亮）。
- **Dash 虚线不做**：`AnnotationStyle.Dash` 渲染端 0 处消费，接线要动十余个 Render
  实现，另立任务。

## UI 组件与布局（第 3 节，已确认）

- **MainWindow 三列 Grid**：左列表 200px | 画布 * | 右面板 264px（右侧列不动）。
- **新控件 `ElementListPanel`**：
  - 标题「元素」+ 折叠按钮；折叠态 28px 图标条；文档无标注时整栏隐藏，画布占满。
  - 行 = 类型图标（复用 `Icon.*`）+ 摘要（Text→内容截 12 字、Number→#序号、其余→类型名）
    + hover 露出上移/下移小按钮。
  - 双向联动：画布选中→行高亮+滚动可见；点行→Selection+切 Select。
  - 列表顺序 = z-order（栈顶在顶），按钮语义与视觉一致。
- **StylePanel → Inspector 化**（控件名与既有绑定不变，改内容结构）：
  - Header：`SelectionKindLabel` + 选中态徽标。
  - 分组（默认全展开）：公共组（颜色/描边宽）｜几何组（X/Y/宽/高 NumberBox，
    开放形状只读提示）｜效果组（Mosaic/Blur 颗粒度、Text 字号）｜层序组
    （上移/下移/置顶/置底，与列表同命令路径）。
  - 未选中：只显示公共组（编辑画笔默认样式，现状行为），其余组隐藏。
- **i18n**：新增 keys（元素/上移/下移/置顶/置底/几何/宽/高/颗粒度/只读提示等），
  Zh/En 两表同步，`LocKeyConsistencyTests` 兜底。
- **视觉**：左栏沿用 `Bg.Panel`/`Line` token，不引入新颜色。

## 测试（第 4 节，已确认）

- **Core.Tests**：ReorderAnnotationCommand Do/Undo/边界；`Document.Move` 栈顶栈底与
  Changed；几何映射（X/Y→delta、W/H→SE 目标点、Clamp）。
- **App.Tests**（headless、语言无关断言）：commit 后 Selection=新标注且工具不变；
  切工具不清 Selection；Select 空点/Delete 清；列表↔画布双向联动；几何 NumberBox
  写值→bbox 变化→Undo 恢复；StylePanel 双态。
- **UiSnapshot**（`NOVASHOT_SNAPSHOT=1`，windows TFM 腿）：三栏布局快照进 `artifacts/ui/`。

## 不做清单（YAGNI）

多选集、Dash 虚线接线、透明度、列表拖拽重排（用按钮）、行内重命名、图层隐藏/锁定、
Alt+点选穿透、绘制工具下手柄、Pencil/Highlighter 几何缩放。

## 已知风险

- 切工具不清 Selection 后，`SkiaCanvas` 手柄渲染必须同步条件化，否则绘制工具下手柄漏显
  ——UiSnapshot + headless 双盖。
- Text 工具 commit 流程自带 `BeginTextEdit → Selection=ta`，与即选中规则并存，需确认
  不产生双重设置（实现时核对路径）。
- Pencil/Highlighter 的 MoveHandle 是否支持等比缩放未核实——几何组对它们一律只读，
  不依赖其缩放能力。
