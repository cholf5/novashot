# Q-1 App 测试宿主栈溢出（RadioButton 组 ↔ TwoWay 绑定互踢环）

- **优先级**: P2 ｜ **复杂度**: S ｜ **状态**: Backlog ｜ **来源**: B-6 审查轮发现的存量 flake（当时「本卡不修，建议另开 Q- 卡」），本会话 2026-11-10 补实证并立卡

## 目标

把「App 测试偶发 abort」这条存量噪音清掉：它会让任何一次全量测试绿/红都不可信（崩溃发生在随机用例之间，报告里只看到 `Passed: 187` + `Test Run Aborted`）。

## 现象

- `dotnet test tests/Prtsc.App.Tests -f net10.0` 偶发整进程崩溃：`Test host process crashed : Stack overflow`，退出前已跑过的用例全绿（观察到 187 / 188 / 192，总数 206）。
- 崩溃栈稳定落在同一条链上（2026-11-10 本机实测）：

```
RadioButtonGroupManager.OnCheckedChanged
  → ToggleButton.get_IsChecked
  → EffectiveValue.SetAndRaiseCore / NotifyValueChanged / OnPropertyChangedCore
  → UntypedBindingExpressionBase.PublishValue → WriteTargetValueToSource
  → CompiledBindings.InpcPropertyAccessor.SetValue → SendCurrentValue
  → PropertyAccessorNode.WriteValueToSource        （← 环）
```

栈里还夹着 `ExpressionNode.SetError` / `BindingExpression.OnNodeError`，说明环里混着绑定错误重发布。

## 根因判定

- 位置：`src/Prtsc.App/Controls/CompositionPanel.axaml` L25–31 三枚 `RadioButton`，共用 `GroupName="BgKind"`，`IsChecked` 各自 `Mode=TwoWay` 绑 VM 的 `IsBgNone` / `IsBgSolid` / `IsBgGradient`。
- 机制：组管理器取消兄弟 → 兄弟的 TwoWay 回写 VM → VM 属性变更通知 → 绑定重新发布（`CompositionPanelViewModel.OnStateChanged` 用 `OnPropertyChanged(string.Empty)` 整表刷新，发布面最大）→ 再次进入 `IsCheckedChanged`，直到栈耗尽。栈溢出是**进程级崩溃**，xunit 的 try/catch 兜不住，所以表现为 abort 而不是失败。
- 触发条件未完全钉死：VM 三个 setter 都是 `if (value) Apply(...)`（写 false 不做事），单看本不该发散；实际栈里出现 `OnNodeError` 路径，说明是「绑定错误 ↔ 重发布」把环接上了。这一点留给实现者用最小复现钉死（例如把 CompositionState 替换与 RadioButton 勾选交叉驱动的那一支单独抽出来跑）。
- 为什么偶发：环能否被打破取决于通知是否被调度器批次边界切断（`RunJobs` 分批、UI 线程调度）。headless 下高频开关 CompositionPanel（快照 / 设置页 / XAML 测试）概率放大；单跑 20 次里有 3 次 abort（本次实测 8 跑 3 abort，另 5 跑带详细日志的连跑全绿）。
- 已知基线：B-6 审查轮用 `git stash` 干净树复现过同样 abort（4 跑 1 abort）——**不是新代码引入的**，是存量缺陷。

## 修复候选（实现者选一，记录裁决理由）

1. **三枚改 `Click` / 代码置位**：XAML 去掉 `IsChecked` 绑定，由 code-behind 在点击时调 VM（`Apply` 是既有咽喉点），选中态用 OneWay 绑定回显。爆炸半径最小，语义与现状一致。
2. **`IsChecked` 保持 OneWay + 代码写**：绑定只做回显，输入走 `Click` 事件 → 不可回写的绑定不入环。
3. 换控件族：`ItemsControl` + 单个 `ToggleButton` 单选组（自管选中态），彻底绕开 `RadioButtonGroupManager`。改动最大，除非 1/2 都压不住。

**同时检查**：仓库里其它「`GroupName` + `IsChecked` TwoWay」的组合（`grep -rn "GroupName=" src/Prtsc.App`）应一并按同一口径收敛——设置页 `SettingsWindowXamlTests` 的跨线程 Radio 孤例（U-1 记录）很可能同源。

## 验收标准

- [ ] 连续 20 次 `dotnet test tests/Prtsc.App.Tests -f net10.0` 无 abort（当前基线约 1/3 概率 abort）。
- [ ] 双 TFM 并行合跑（`dotnet test Prtsc.slnx`）连续 20 次无 abort。
- [ ] 面板背景切换行为零变化：点 None/Solid/Gradient 三态互斥、选中环正确、`CanvasStateChanged` 仍触发（E-2 的 config 回写不能被这次改动打断）。
- [ ] 不引入 `CompositionState` 整表刷新（`OnPropertyChanged(string.Empty)`）之外的新路径。

## 测试用例

- [ ] 最小复现：headless 反复「切三态 + 替换 CompositionState」，断言不抛栈溢出（修复前应能稳定 abort，修复后恒绿）。
- [ ] 行为回归：三态互斥 + 当前态回显（现有 `CompositionPanel` 相关测试跑通）。
- [ ] `SettingsWindowXamlTests` 并行压力跑（观察跨线程 Radio 孤例是否一并消失）。

## 关联文档

- `kanban/issues/B-6-toolbar-customization.md`（首次记录，含 stash 基线复现）
- `kanban/issues/U-1-transparent-preview-checker.md`（同源孤例记录）
- 代码：`src/Prtsc.App/Controls/CompositionPanel.axaml` L25–31、`src/Prtsc.App/ViewModels/CompositionPanelViewModel.cs`（`Apply` 咽喉点 / `OnStateChanged`）

## Verification

- How to run/verify: `for i in $(seq 1 20); do dotnet test tests/Prtsc.App.Tests -f net10.0 --no-build | tail -2; done`（修前应出现 `Test Run Aborted` + `Stack overflow`）
- 崩溃证据留存：2026-11-10 本机 macOS，`Passed: 187/188/192 of 206` + `Stack overflow`，栈顶 `RadioButtonGroupManager.OnCheckedChanged`
