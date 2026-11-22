# Q-1 App 测试宿主栈溢出（RadioButton 组 ↔ TwoWay 绑定互踢环）

- **优先级**: P2 ｜ **复杂度**: S ｜ **状态**: Done（2026-11-10；真机项 QA-1 §D/F 顺带覆盖面板背景切换）｜ **来源**: B-6 审查轮发现的存量 flake（当时「本卡不修，建议另开 Q- 卡」），2026-11-10 补实证并修复

## 目标

把「App 测试偶发 abort」这条存量噪音清掉：它会让任何一次全量测试绿/红都不可信（崩溃发生在随机用例之间，报告里只看到 `Passed: 186/187` + `Test Run Aborted`）。

## 现象

- `dotnet test tests/Novashot.App.Tests -f net10.0` 偶发整进程崩溃：`Test host process crashed : Stack overflow`，退出前已跑过的用例全绿（观察到 186 / 187 / 188 / 192，总数 206）。
- 崩溃栈稳定落在同一条链上：

```
RadioButtonGroupManager.OnCheckedChanged
  → ToggleButton.get_IsChecked
  → EffectiveValue.SetAndRaiseCore / NotifyValueChanged / OnPropertyChangedCore
  → UntypedBindingExpressionBase.PublishValue → WriteTargetValueToSource
  → CompiledBindings.InpcPropertyAccessor.SetValue → SendCurrentValue
  → PropertyAccessorNode.WriteValueToSource        （← 环）
```

栈里还夹着 `ExpressionNode.SetError` / `BindingExpression.OnNodeError`，说明环里混着绑定错误重发布。

## 根因

- 位置：`src/Novashot.App/Controls/CompositionPanel.axaml` 背景三态 chip —— 共用 `GroupName="BgKind"`，`IsChecked` 各自 `Mode=TwoWay` 绑 VM 的 `IsBgNone` / `IsBgSolid` / `IsBgGradient`。
- 机制：组管理器取消兄弟 → 兄弟的 TwoWay 回写 VM → VM 属性变更通知（`OnStateChanged` 用 `OnPropertyChanged(string.Empty)` 整表刷新，发布面最大）→ 绑定重新发布 → 再次进入 `IsCheckedChanged`，直到栈耗尽。栈溢出是**进程级崩溃**，xunit 的 try/catch 兜不住，所以表现为 abort 而不是失败。
- 为什么偶发：环能否被打破取决于通知是否被调度器批次边界切断（`RunJobs` 分批、UI 线程调度）。headless 下高频开关 CompositionPanel（快照 / 设置页 / XAML 测试）概率放大。
- 已知基线：B-6 审查轮用 `git stash` 干净树复现过同样 abort（4 跑 1 abort）——**不是新代码引入的**。

## 修复（已落地）

`IsChecked` 降级为 **OneWay 回显**，输入改走 `Click` + `Tag`，code-behind 调 VM 既有咽喉点 `Apply`：

- `CompositionPanel.axaml`：三枚 chip 命名 `BgNoneChip` / `BgSolidChip` / `BgGradientChip`，加 `Tag="None|Solid|Gradient"` 与 `Click="OnBgKindClick"`，`IsChecked="{Binding IsBg…, Mode=OneWay}"`。
- `CompositionPanel.axaml.cs`：`OnBgKindClick` 按 Tag 调 `vm.IsBgNone/IsBgSolid/IsBgGradient = true`（setter 只在 true 时 `Apply`，语义不变）。
- 保留 `GroupName="BgKind"`：与工具坞 `GroupName="Tool"` + OneWay 的既有口径一致；环的结构前提（目标侧写回源）已不存在。
- 顺查全仓 `GroupName=` 组合：只有这三枚曾是 TwoWay；`SettingsWindow`（语言）与 `CaptureLauncherWindow`（模式）本来就是 Click + 无绑定，`MainWindow`（工具坞）是 Click + OneWay —— 无需改动。

未采用的方案：`IsChecked` OneWay 但不加 Click（输入会丢）、换 `ItemsControl`+自定义单选（改动面大，收益小）。

## 验收（全部实测）

- [x] 新增回归 `tests/Novashot.App.Tests/BackgroundKindChipTests.cs`（3 例）：
  - `ChipsAreDisplayOnly_TargetChange_DoesNotWriteBackToVm` —— 直接改目标侧不回写 VM（修复前会递归爆栈）；
  - `Click_SwitchesBackground_KeepsSingleSelection_AndAppliesOnce` —— 点击切态 + 三态互斥回显 + `CanvasStateChanged` 每次点击恰一次（E-2 契约）；
  - `RapidStateFlips_DoNotRecurse_AndStayConsistent` —— 100 次点选 + 程序侧 `SetGradient` 回显。
- [x] A/B 压测（同机同命令，`-f net10.0` 全量）：**修前 15 跑 3 abort（各停在 186/187/187）；修后 15 跑 0 abort + 另 10 跑 0 abort**（共 25 连跑无 abort）。
- [x] 全量：Core 785 + App 209（206+3）×net10.0 + Mac 91 + Linux 46 全绿；`dotnet build` 0 warning / 0 error。
- [x] 快照回归：`NOVASHOT_SNAPSHOT=1 … --filter UiSnapshot` 通过，`03-editor-canvas-tab-en.png` 目检面板 Gradient 选中态正常（选中环/互斥正确）。

## 踩坑记录（测试侧，值得记住）

写回归用例时把 `CapturedImage` 放进 `Build()` 的 `using` 里 → 返回后位图已被释放，Skia 在布局/测量阶段直接 abort 宿主进程（无异常、无堆栈、日志空白，极易误判成 Q-1 复发）。正解：`CapturedImage` 必须活到窗口关闭之后，由调用方 Dispose。已写进测试注释。

## 关联

- `kanban/issues/B-6-toolbar-customization.md`（首次记录，含 stash 基线复现）、`kanban/issues/U-1-transparent-preview-checker.md`（同源孤例）
- 代码：`src/Novashot.App/Controls/CompositionPanel.axaml(.cs)`、`src/Novashot.App/ViewModels/CompositionPanelViewModel.cs`（`Apply` 咽喉点 / `OnStateChanged`）
