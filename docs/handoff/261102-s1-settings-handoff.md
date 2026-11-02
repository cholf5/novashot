# Prtsc 交接备忘 — 2026-11-02 S-1 设置中心（接 261102 UI-1 handoff）

> 本文只记 S-1 增量；视觉层结构与 Avalonia 12 坑位以 `261102-ui-redesign-handoff.md` 为准。

## 0. TL;DR

- **S-1 完成**：独立设置窗口（左栏 6 分段：常规/热键/QuickSave/导出/编辑器默认值/捕获），整窗草稿 + 保存/取消语义；**语言入口从托盘搬进设置**（托盘语言子菜单已删）；热键重注册、自启切换、语言全局刷新都在保存一刻原子发生。
- 新 Core 字段：`CaptureConfig.IncludeCursor`（WGC 光标，仅 Windows）。
- 测试 635+91（Core 419 / Linux 45 / Mac 80 / App 91×2TFM）全绿；快照 9 张（新增 `30-settings.png`）。
- **独立 reviewer 过了一轮全分支**：1 Critical（Windows 热键重注册竞态）+ 4 Important 已全部修复（`19bfde1`）；Linux 侧还顺手补了 `XUngrabKey`（之前 Stop 都不解绑）。
- 未真机验证项全部在 `docs/qa/261102-smoke-checklist.md`（⚑ 标注 + 新增 Windows/Linux 重注册段）。

## 1. 文件地图（S-1 增量）

```
src/Prtsc.App/
  Views/SettingsWindow.axaml(.cs)   六分区窗口；OnKeyDown 录制热键；ShowSection 泛化 OnPanelTabClick 模式
  ViewModels/SettingsViewModel.cs   草稿副本 + Save()（validate→自启→一次 Update）；语言不走 VM
  Hotkeys/KeyCombo.cs               Avalonia Key → HotkeyParser 词表 spec 串（Esc/纯修饰键/NumPad 拒绝）
  Localization/Loc.cs               +ApplyStoredPreference（合法且不同才切换）；+60 条 Settings.* ×2 表；-Tray.Language
src/Prtsc.Core/Config/PrtscConfig.cs   +Capture.IncludeCursor；+HotkeyConfig.SameSpecsAs（同值跳过重注册的判断）
src/Prtsc.Platform.Windows/Hotkeys/WindowsHotkeyService.cs  +_registeredIds（泵线程权威集合，见 §2 C1）
src/Prtsc.Platform.Windows/Capture/WgcCaptureService.cs     注入 IConfigStore，CaptureItem 传 IncludeCursor
src/Prtsc.Platform.Linux/...                +XUngrabKey interop；RegisterAll/Stop 先解上一轮抓键
```

入口：托盘 `Tray.Settings` + 主窗口 ⋯ Flyout `Editor.Settings`（Library 之下）。

## 2. 关键坑（新会话必读）

15. **WindowsHotkeyService 二次 RegisterAll 的竞态**（review C1）：解绑必须按泵线程自己的 `_registeredIds`（注册成功才进集合），不能按 `_idToAction`——后者在调用线程被 Clear/重建，泵侧按它解绑会把旧 OS 注册永久留下，新注册全撞 already registered，三个热键整体死掉且全局吞键。App 层另有 `SameSpecsAs` 同值跳过兜底（关窗写尺寸也触发 Changed，不能逢 Update 就重注册）。
16. **Linux XGrabKey 无 Ungrab**（review I3）：改热键重注册前必须先解上一轮，否则旧组合继续触发到进程退出。interop 之前根本没有 XUngrabKey，现补（对称解 NumLock/CapsLock 变体）。
17. **Avalonia 12 Grid 没有 `Spacing`**（StackPanel 才有）；`Window` 不实现 `IDisposable`（测试里用 `Close()`）。
18. **Pause 键不在 HotkeyParser 词表**：录制器产不出它，测试数据别用。
19. **语言副作用集中在 `Loc.ApplyStoredPreference`**（store.Changed → 调用）；`SetPreference` 是托盘旧链路保留。测试语言行为直接测 Loc 方法（headless + SnapshotBootstrap 宿主），别测 App lambda。
20. `DynamicResource` 文案在切语言后**会热刷**（包括设置窗自身）；只有普通 string 属性（窗口标题）不热刷——QA 清单里曾写反，已修。

## 3. 诚实清单（S-1 增量，未经真机验证）

1. Windows 泵侧重注册修复（C1）无 Windows 可执行测试：逻辑靠代码审查 + Core 的 SameSpecsAs 单测兜着，**真机必须过 QA 清单 I 段「保存两次热键仍工作」**。
2. `IncludeCursor=false` 实际效果（D3D）只有管道参数正确性审查；真机项在 I 段。
3. 自启开关真写注册表/LaunchAgent 未验证（I 段）。
4. Linux 改热键重注册（I3）有 FakeX 层单测，真机未过（J 段）。
5. 录制时按「当前已注册的组合键」会真触发截图（录制不暂停全局热键）——体验怪但无害，未处理。
6. 多开设置窗口是 last-save-wins（沿用云设置行为）。
7. Windows 真机依旧从没跑过（含设置窗）。

## 4. 下一步

- QA-1 真机冒烟（清单已含 S-1 项 + J 段）；S-2 视觉收尾小项（slider 填充段等）不变。
- M7 Updater 仍卡 R-1（Velopack 拍板）。
