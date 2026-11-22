# E-2 画布参数持久化（编辑器改动回写 config，下次截图恢复）

- **优先级**: P2 ｜ **复杂度**: S ｜ **开发者**: cholf5 ｜ **状态**: Done（2026-11-03）
- **依赖**: 无（G-13/G-14 编辑器默认值已就位，本卡是它的回写方向补全）
- **关联**: `NovashotConfig.EditorConfig`（config.json `editor` 段）、`MainWindowViewModel.ApplyEditorDefaults`、`CompositionPanelViewModel`

## 目标

用户在编辑器画布面板（Canvas 分页）调好的背景 / Padding / 圆角 / 阴影 / 内衬 / 边框，改动后持久化，下次截图恢复——「编辑器默认值」升级为「上次使用的画布参数」，单一事实源（仍是 config.json `editor` 段），Settings 页同步显示最新值。config 形状不变，旧配置照常加载。

## 子任务

1. Core 新增 `Editor/EditorConfigMapper.cs`（纯函数）：`FromConfig`（现 ApplyEditorDefaults 的映射逻辑下沉，含 StripBackground + Clamped）+ `ApplyTo`（state → config 反向回写：背景类型/颜色/渐变、padding、cornerRadius、shadowSize、inset+insetBackgroundColor、border 四件）。
2. 激活死字段 `showBackground`：`BackgroundKind.None ↔ false` 双向（设置页「显示背景」开关此前不生效）。
3. `CompositionPanelViewModel.Apply`（参数变更唯一咽喉点）后触发 `CanvasStateChanged` 回调；crop 走 EditorViewModel 直改路径不误触发。
4. `MainWindowViewModel`：EnterEditor 挂回调 → Update（同步内存）+ SaveAsync 防抖 500ms（滑杆不每帧写盘）；`ReturnToCapture` flush 立即落盘；ApplyEditorDefaults 改调 FromConfig 去重。

## 验收标准

- 调画布参数 → 返回 → 再截图：参数保持；重启进程后仍保持（config.json 已写盘）。
- Settings「编辑器默认值」页显示的即上次使用值；「显示背景」开关生效。
- 无 config store（单测路径）行为不变（EditorState.Default）。

## 测试用例

- Core：`EditorConfigMapperTests`——ApplyTo→FromConfig 外观字段 round-trip；None↔showBackground 双向；带 alpha 颜色 `#rrggbbaa`；边框/阴影/内衬关闭态（清零保色）；渐变 null 兜底。
- App：扩 `EditorDefaultsFromConfigTests`——面板改 Padding/背景 → `store.Current.Editor` 同步更新；ReturnToCapture 再 EnterEditor 参数保持。

## 已知限制

- SaveAsync 防抖 500ms + ReturnToCapture flush；进程在编辑器开启状态下被强杀可能丢失最后 500ms 内的改动（可接受）。

## Verification（2026-11-03）

- TDD 先 RED 后 GREEN：Core 新增 `EditorConfigMapperTests` 13 用例 + `GradientParserTests` 2 用例（8 位 CSS alpha / 垃圾输入），App 侧扩 `EditorDefaultsFromConfigTests` 3 用例（面板改 Padding/背景 → store 同步、ReturnAndReenter 保持、无 store 不抛）。
- 全量 `dotnet test` 651/651 全绿（Core 432 + App 94×2TFM + Mac 80 + Linux 45）。
- 实测发现并顺带修掉三个存量问题：`showBackground` 死字段激活（None↔false 双向）；`GradientParser.ParseColor` 对 8 位 hex 走 `SKColor.Parse` 原生 #aarrggbb 语义导致 CSS #rrggbbaa 通道错位（显式纠正）；`SKColor.Parse` 垃圾输入抛 `ArgumentException` 而原 catch 只接 `FormatException`（"#gg0000" 曾会炸穿配置接线）。
- 存量 flake（与本卡无关）：`SettingsWindowXamlTests` 在全解决方案并行跑时偶发 1–5 个用例失败，单跑/复跑恒绿；已用 `git stash` 干净树基线复现（同样 flake）。
- 待真机冒烟：截图 → 调参数 → 返回 → 再截图参数保持；重启进程仍保持；Settings「编辑器默认值」显示上次使用值。
