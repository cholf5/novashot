# 右侧面板合并：Style / Canvas 两 tab → 单页直排（2026-11-22）

## 判断

2026-11-02 重设计引入两 tab 的动机是「标注时不用滚过 ~1400px 的控件」。如今两个面板合计约 1100px（Style ~420px + Composition ~640px），痛点却变了：

1. **tab 本身成了摩擦**。「改个描边色，再顺手加个阴影」这个最自然的动作链被拆成两页，每切一次 tab 丢一次上下文；而两组参数在心智上是同一件事——「让它好看」。
2. **画布功能被藏起来了**。背景 / 阴影 / 圆角是分享向产品的差异化卖点，却压在第二页，用户可能根本没点开过。
3. **合并是删代码不是加代码**：删一行 tab chrome 与切换逻辑，两个 UserControl 直排进一个 ScrollViewer。符合「画两笔就走」的判断标准。

与当年 ~1400px 单列的区别在于节顺序：高频项全部置顶，首屏（颜色/描边/字号）不进滚动区，滚轮只发生在调合成参数时。

## 备选与取舍

| 方案 | 说明 | 结论 |
|---|---|---|
| A 单页直排 | 删 tab，两面板按序直排，色板保持各节自带 | **采纳** |
| B 合并 + 统一色板（目标切换） | 四组重复色板收成一个颜色区 + 目标 chips；纵向最省，但改色多一次点击、VM 改动面大 | 否（违 YAGNI） |
| C 合并 + 可折叠分区 | 扫读最干净，但折叠状态要持久化或会话管理，复杂度最高 | 否（面板总共十来个控件） |

## 节顺序（自上而下）

1. **颜色**（色板 + 取色历史行）
2. **描边**
3. **字号**（仅文字工具可见）
4. **当前选中**（并入样式块尾部）
5. **背景**（三态 chip + 自动取边缘色 + 纯色板 + 渐变）
6. **边距 / 圆角**
7. **阴影 / 边框 / 内衬**
8. **裁剪比例**（一次性设置，垫底）
9. 最底部保留 `Editor.CanvasHint` 一行提示

排序逻辑：每笔画都可能动的高频项置顶、不进滚动区；「让它好看」的合成参数中段；用过一次就不再碰的（裁剪比例）和阅读型内容（hint）垫底。

## 改动清单（删为主）

| 位置 | 改动 |
|---|---|
| `src/Novashot.App/Views/MainWindow.axaml:466-490` | 删 Tab 行（`StyleTab`/`CanvasTab`）与双 ScrollViewer 结构 → 单 ScrollViewer 内直排 `StylePanel` + `CompositionPanel`；衔接处 margin 微调 |
| `src/Novashot.App/Views/MainWindow.axaml.cs:255` | 删 `OnPanelTabClick`（纯视图状态，无 VM 牵连；`SettingsWindow` 仅注释提及此模式，不动） |
| `src/Novashot.App/Localization/Loc.cs` | 删 `Panel.Style` / `Panel.Canvas` 两个 key，Zh/En 两表同步删 |
| `tests/Novashot.App.Tests/UiSnapshotTests.cs:89-99` | 删切页段；`03-editor-canvas-tab-en.png` 随 tab 消失（单页后 `02-editor-en.png` 一张覆盖全面板） |

## 明确不做

- **不合并 VM**：`StylePanelViewModel` / `CompositionPanelViewModel` 保持独立，`Editor.StylePanel` / `Editor.CompositionPanelViewModel` 原样。
- 不做统一色板、不做折叠分区、不做滚动位置记忆。
- **不触碰 Q-1 修复的绑定结构**：背景三态 chip 保持 OneWay 回显 + Click 携带状态，合并不改任何绑定方向。
- **不触碰 E-2 咽喉点**：CompositionPanel 的 `Apply` / `CanvasStateChanged` / 500ms 防抖写盘语义零改动。

## 验证

- `dotnet build Novashot.slnx` 全绿（警告即错误）；全量测试 Core + App×2TFM（windows 腿 `-f net10.0-windows10.0.19041.0`）+ Mac + Linux。
- `LocKeyConsistencyTests` 兜底两表同步删 key。
- `NOVASHOT_SNAPSHOT=1 dotnet test tests/Novashot.App.Tests -f net10.0 --filter UiSnapshot` 生成快照，人工目检 `02-editor-en.png` 全面板直排与选中态。
- QA-1 真机冒烟清单里「面板 tab」一项改写为「面板直排滚读」。
