# 代码审查：画布元素选中与 Inspector 属性面板（2026-11-23）

> 审查方式：com-requesting-code-review 标准的单人全量审查（用户离席，交互式逐条确认不可行；
> 用户醒来后可按本文档逐条讨论或否决）。
> 范围：`docs/plans/2026-11-23-inspector-element-list-review-scope.md` 列出的 18 个文件
> （14 处修改 + 9 个新建；会话开始时用户的在制工作已由其本人提交为 `dd7c61e`，工作区只剩本次改动）。

## 统计

- 已审查文件：23（14 modified + 9 new）
- 发现问题：P0 × 0，P1 × 2（均已当场修复），P2 × 4（记录不修）
- 已确认非问题：见文末清单

## P1（已修复）

### P1-1 headless 会话下 ElementListPanel 滚动重试无限自续 → 测试挂死

- 位置：`src/Novashot.App/Controls/ElementListPanel.axaml.cs`（ScrollSelectionIntoView）
- 现象：windows TFM 腿单测挂死（testhost 零 CPU 空转）。选中行容器未 realize 时
  `Dispatcher.UIThread.Post` 无界自续，headless 会话的 Dispatch 永远等不到队列排空。
- 修复：重试上界 2 次，超界放弃（选中态一变会自然重试）；`net10.0-windows` 腿 567ms 通过。

### P1-2 Inspector 几何口径错误风险：BoundingBox 是描边外扩口径，手柄在逻辑矩形空间

- 位置：`AnnotationBase` / `StylePanelViewModel.SelX/SelY/SelW/SelH`
- 后果：若直接用 BoundingBox，Inspector 显示与手柄位置差半个 stroke，且每次宽高编辑漂移累积。
- 修复：Core 新增 `AnnotationBase.GeometryRect` 虚属性（默认 = BoundingBox），盒状族
  （Rectangle/Ellipse/Spotlight/PixelEffect）override 返回未膨胀形状矩形；Inspector 全部走该口径。
  Core 测试锁定两种口径差（`GeometryRect_Rectangle_IsLogicalNotInflated`）。

## P2（记录，不修）

1. `Summarize` 12 字截断可能切开代理对（emoji 文本摘要尾部出现替换符）——纯显示层，无功能影响。
2. `SelX` 等 setter 里的 `RefreshInspector()` 会触发一轮冗余 PropertyChanged——数值收敛、无死循环，成本可忽略。
3. 层序组「置顶 ⤒ / 上移 ↑」分别复用 BringToFront/BringForward 命令，语义靠 tooltip 区分——与元素列表按钮同路径，可接受。
4. `RebuildElementList` 全量重建——截图标注量级小（几十个封顶），简单可靠优于增量维护。

## 已确认非问题

- `ReorderAnnotationCommand` 索引漂移：与既有 `RemoveAnnotationCommand` 的 z-index 捕获同款限制，构造器已校验边界，文档注明。
- `ExecuteAdd` 设 Selection 后 Text 工具的 BeginTextEdit 路径：`DrawOp` 以 `ReferenceEquals(_selection, _editingText)` 跳过编辑中标注的 overlay，Cancel/空 Commit 的 Undo 会经 `OnDocumentChanged` 清掉悬空 Selection——`EditorSelectionRulesTests` + 既有 Text 流测试全绿。
- `Selection` 跨工具存活后 Ctrl+滚轮（Mosaic BlockSize / Blur Sigma / 描边宽）行为：只是命中面变大，语义不变。
- NumericUpDown(decimal?) ↔ float 绑定与 setter 内 RefreshInspector 的回写环：数值相同即收敛，无循环。
- 面板事件退订：DataContextChanged 先 Unsubscribe 再订阅，DetachedFromVisualTree 兜底；EditorViewModel.Dispose 走 StylePanel.Dispose 同款路径。
- `mc:Ignorable` 的 `markup-compatibility/2008` URI 会触发 AVLN3000（XamlX 只注册 2006 的指令 emitter）——已改 2006；这也是本次「No precompiled XAML」连锁的根因。
- 既有三腿全量出现过的 2 例并行 flake（OverlayWindowInputTests / BackgroundKindChipTests）：隔离运行恒过、全量重跑即过、失败用例在两次运行间漂移——AGENTS.md 已知的 HeadlessUi 并行时序问题，非本次引入。

## 验证记录

- `dotnet build Novashot.slnx`：0 警告 0 错误（TreatWarningsAsErrors）
- Core.Tests：822/822；App.Tests net10.0：259/259；App.Tests windows TFM：265/265
- 快照：`artifacts/ui/07-editor-elements.png`（三栏 + Inspector 各组 + 完整选中框）、
  `08-editor-soft-selection.png`（绘制工具下淡虚线框、无手柄）、`10-panel-style.png`（默认态只有公共组）
- 手动验证清单（计划任务 8 步骤 4 的 8 条）需要真机 `dotnet run --project src/Novashot.App` 走一遍，留待用户醒来确认

## 遗留说明

- Dash 虚线、透明度、多选集、列表拖拽重排等按设计文档「不做清单」未实现。
- CODE_MAP：仓库未启用 docs/CODE_MAP.md，本次跳过 com-update-code-map。
