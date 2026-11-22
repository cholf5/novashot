# S-1 M8 设置中心 GUI

- **优先级**: P1 ｜ **复杂度**: L ｜ **开发者**: agent（2026-11-02）｜ **状态**: Done（2026-11-02；真机项在 QA-1 §I/§J 与 `docs/qa/261102-smoke-checklist.md` ⚑ 段）
- **依赖**: 无（与 C-1 可并行）
- **关联**: `src/Novashot.Core/Config/NovashotConfig.cs`（字段全部就位）、`JsonConfigStore.cs`、winshot `internal/config`、flameshot `src/config/`

## 目标

消灭「改 JSON 才能配置」——这是当前相对两个参考项目最大的一块体验缺口。config schema 已全备，缺的只是 UI 层。

## 子任务

1. SettingsWindow（Avalonia）+ ViewModel：分区导航（常规 / 启动与托盘 / 热键 / QuickSave / 导出 / 编辑器）。
2. 热键录制控件（捕获按键组合 + 冲突检测 + 保存后经 `IHotkeyService` 重注册；`HotkeyParser` 已有）。
3. 启动项开关（`RegistryAutostartService` 已有，接 UI）；托盘行为（MinimizeToTray/CloseToTray）。
4. QuickSave 目录选择器 + 命名模式；导出默认格式 + JPEG 质量滑杆（吸收 backlog「导出质量滑杆 UI」）。
5. 编辑器默认值（Padding/圆角/阴影/背景/OutputRatio/AutoBackground/边框 + WGC 光标开关进 config——吸收 backlog 项，需先在 `EditorConfig`/捕获 config 加字段）。
6. 保存走 `IConfigStore`，schema 向后兼容。
## 验收标准

- 所有 `NovashotConfig` 用户可感字段都有 UI 入口，改动即存（或显式保存）并即时生效（热键重注册、托盘行为切换）。
- 热键冲突有提示，非法组合不可保存。
- 旧版本 JSON 无迁移损失，新增字段有默认值。

## 测试用例

- 热键录制 ViewModel 纯逻辑（组合解析/冲突）单测。
- 设置→config 往返序列化测试（含未知字段容忍）。
- 真机：改热键后旧键失效新键生效（记入 QA 冒烟清单）。

## 备注 / 风险

- `ShowNotification` 开关 UI 放本卡，实际通知逻辑在 N-1（可先做空挂）。

## Development Log

### 2026-11-02（交付，本卡状态此前漏改）

- 六分区设置窗口（常规 / 热键 / QuickSave / 导出 / 编辑器默认值 / 捕获），整窗草稿 + 保存/取消语义（取消不落盘）。
- 热键录制控件：`KeyCombo` → `HotkeyParser` spec 串，Esc/纯修饰键/NumPad 拒绝；保存一刻原子发生（自启切换 → config Update → 热键重注册 → 语言全局刷新）。
- 语言入口从托盘搬进设置（托盘语言子菜单删除）；新 Core 字段 `Capture.IncludeCursor`（WGC 光标开关）。
- 独立 review：1 Critical + 4 Important 全修（`19bfde1`：Windows 泵线程权威 `_registeredIds` 防重注册竞态、Linux 补 `XUngrabKey`、重复热键提示、`Loc.ApplyStoredPreference`、IncludeCursor 复制）；测试 635+91 全绿。
- 真机项：`docs/qa/261102-smoke-checklist.md` ⚑ 段 I/J；handoff `docs/handoff/261102-s1-settings-handoff.md`。

## Verification

- Related files: `src/Novashot.App/Views/SettingsWindow.axaml(.cs)`、`src/Novashot.App/ViewModels/SettingsViewModel.cs`、`src/Novashot.Core/Config/NovashotConfig.cs`
- Results: 交付时全量 635 + 91 绿；本卡真机验收并入 QA-1（未清零）。
