# B-6 工具栏按钮自定义

- **优先级**: P3 ｜ **复杂度**: M ｜ **状态**: Done（真机项 QA-1 §P 留人工冒烟）｜ **来源**: flameshot `src/config/buttonlistview`（面板按钮可见性配置）

## 目标

flameshot 式能力：用户可配置哪些工具/按钮出现在工具环与面板上（精简工作区）。

## 子任务

- [x] 1. config `Editor.VisibleTools`（默认全开）；设置页多选 UI（挂编辑器默认值分区）。
- [x] 2. 工具坞按 config 过滤渲染；热键不受影响（**设计裁决：隐藏 ≠ 禁用**，隐藏工具热键仍可用——与「设置页提示文案」一致，测试钉住）。

设计裁决补充：
- `Select` 恒可见且设置页禁改（默认工具必须有落点，热键 V 始终有 UI 锚点）。
- config 存 ToolKind 枚举名，**空表 = 全开**（向前兼容：日后新增工具自动出现）；全选时规范写空表。
- 分隔线按「组边界发射」：每个新组第一个可见工具点亮紧邻其前的分隔线，整组隐藏后夹中间的/首尾悬空的一律隐藏。

## 验收标准

- [x] 关掉一半工具后工具坞只剩启用的项（headless 接线测试 + 快照 06-editor-dock-filtered.png 实证：V/R/T/X + 3 条组边界线）。
- [x] 重置默认恢复全量（config 空表 = 全开；设置页全选保存写空表）。

## 测试用例

- [x] 可见性过滤纯函数单测（Core `ToolVisibilityTests` 13 例：空/未知/去重/大小写/Select 强制/规范序/ToConfig 全选空表/dock 决策表 4 例含中间组坍缩）。
- [x] config 往返序列化（PrtscConfig JSON round-trip + 缺省 `{}` 空表）。
- [x] App 接线（`ToolDockVisibilityWiringTests` 5 例：默认全可见/部分过滤+孤线/Changed 重算/当前工具隐藏回落 Select/隐藏工具热键仍可用）。
- [x] 设置页（`SettingsViewModelTests` +4 例：默认全勾 Select 禁改/部分 config 加载/部分保存/全选保存空表；`SettingsWindowXamlTests` +1 例：编辑器分区 15 勾选框且 1 个禁用）。
- [ ] 真机冒烟（设置页勾选 UI 视觉/保存即时生效）记入 QA-1 §P。

## 关联文档

- `docs/roadmap.md`（flameshot 差距来源）；实现：`src/Prtsc.Core/Annotation/Tools/ToolVisibility.cs`、`MainWindow.axaml(.cs)` ApplyToolVisibility、`SettingsViewModel` ToolItems、`SettingsWindow.axaml` 编辑器分区

## Development Log

### 2026-11-11

- Core：`ToolCatalog`（15 工具规范序 + 5 组）+ `ToolVisibility`（Resolve/ToConfig/ComputeDockChildVisibility）纯函数；config `EditorConfig.VisibleTools`（camelCase `visibleTools`）。TDD RED→GREEN，13 例全绿。
- App：MainWindow 工具坞 StackPanel 命名 `ToolDock`，构造时 + `_configStore.Changed` 时 ApplyToolVisibility（ RadioButton 过滤 + 分隔线发射 + 隐藏当前工具回落 Select）；设置页编辑器分区 WrapPanel 15 勾选框（Select IsEnabled=false）；Loc 双语 `Settings.Editor.ToolbarTools(+Hint)`。
- 快照：UiSnapshotTests 新增 `06-editor-dock-filtered.png`（真机目检通过：过滤后仅 V/R/T/X 三线，默认态 02 与基线一致）。
- 测试：Core 741→760? 实际 760→773（+13）；App 193→203×2TFM（+5 接线 +4 VM +1 XAML）；Mac 91 / Linux 46 全绿。
- **已知存量 flake（与本卡无关）**：双 TFM 并行时偶发测试宿主栈溢出——根因是 `CompositionPanel.axaml` BgKind 三枚 RadioButton `GroupName` + `IsChecked Mode=TwoWay` 的互踢环（组管理器取消兄弟 → TwoWay 回写 VM → 再发布），多进程并行调度加压下概率放大；stash 基线实测同样 abort（4 跑 1 abort）。单 TFM 运行恒稳（连续多次 203/203）。另有 SettingsWindowXamlTests 跨线程 Radio 孤例（板上 U-1 已记录同类）。本卡不修（改动面在 CompositionPanel VM 语义，超出 B-6 范围），建议另开 Q- 卡：把 BgKind 三枚改回 Click/代码置位或 OneWay+代码写。
- 真机项：QA-1 §P 工具栏自定义冒烟。

## Verification

- Related files: `src/Prtsc.Core/Annotation/Tools/ToolVisibility.cs`, `src/Prtsc.Core/Config/PrtscConfig.cs`, `src/Prtsc.App/Views/MainWindow.axaml(.cs)`, `src/Prtsc.App/ViewModels/SettingsViewModel.cs`, `src/Prtsc.App/Views/SettingsWindow.axaml`, `src/Prtsc.App/Localization/Loc.cs`
- How to run/verify: `dotnet test tests/Prtsc.Core.Tests --filter ToolVisibilityTests`；`dotnet test tests/Prtsc.App.Tests -f net10.0`；`PRTSC_SNAPSHOT=1 dotnet test tests/Prtsc.App.Tests -f net10.0 --filter UiSnapshot` → `artifacts/ui/06-editor-dock-filtered.png`
- Results: Core 773/773；App 203/203（net10.0，windows TFM 单独跑同样绿）；Mac 91/91；Linux 46/46
