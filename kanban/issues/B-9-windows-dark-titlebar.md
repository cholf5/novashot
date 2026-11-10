# B-9 Windows 深色标题栏（DWM 沉浸式深色 + FRAMECHANGED）

- **优先级**: P2 ｜ **复杂度**: S ｜ **状态**: Done（真机项 QA-1 §Q 留人工冒烟）｜ **来源**: 用户反馈「本项目在 Windows 上标题栏是白的」，2026-11-11
- **关联**: `src/Prtsc.Platform.Windows/Windowing/WindowsCaptionTheme.cs`、`src/Prtsc.Platform.Windows/Interop/{DwmApi,User32}.cs`、`src/Prtsc.App/Windowing/CaptionTheme.cs`、`App.axaml.cs`

## 目标

Windows 上深色应用（`App.axaml` `RequestedThemeVariant="Dark"`）启动后，原生标题栏也是深色——夜间模式退出进程重开后，标题栏直接深色，不需要拖动/最大化/任何交互。

## 现象与根因

- Win32 **经典 caption 永远不跟随应用主题**：必须应用层向 DWM 声明 `DWMWA_USE_IMMERSIVE_DARK_MODE`（属性 **20**；1809–1903 回落 **19**）。
- **属性写入 ≠ 视觉生效**：DWM 把 caption 重组合成推迟到下一次 frame 事件。只写属性时表现为「启动白标题栏，拖动/最大化后才变黑」。
- Avalonia 的 `WindowImpl.SetFrameThemeVariant` 只在 build ≥ 22000（Win11）代写属性 20——**Win10 上必须 App 层全权负责**。
- macOS 无此问题（原生深色材质自动跟随）；Linux 视 WM 而定。

## 子任务

1. 平台层：`WindowsCaptionTheme.Apply(hwnd, dark)`——属性 20 失败回落 19；**Light 显式写 0**（防同 HWND 主题来回切残留）；写入成功后 `SetWindowPos(SWP_FRAMECHANGED | NOSIZE | NOMOVE | NOZORDER | NOACTIVATE)` 强制重算 frame。DWM/User32 缺席时静默降级。
2. App 层：`CaptionTheme.AttachAll()` 用 Avalonia 全局 `Window.WindowOpenedEvent` 类处理器统一挂接（主窗 / 设置 / 云设置 / Library / WindowPicker / 更新提示 / 角标全覆盖，新窗口不需要再手工写一行）；运行期 `ActualThemeVariantChanged` 补写。幂等注册、零句柄静默跳过。
3. 明暗裁决：Actual → 窗口 Requested → 应用 Requested → 浅色。不能只看 `ActualThemeVariant`（WindowOpened 时可能仍是 Default，会写错一次浅色且等不到 Changed）。
4. `App.OnFrameworkInitializationCompleted` 在建任何窗口之前 `AttachAll()`。

## 时序（Avalonia 12.1 反编译实证）

`Window.ShowCore` 第一行就 `RaiseEvent(WindowOpenedEvent)`，早于 `EnsureInitialized/ApplyStyling` 与首次合成；而 `Window()` 构造已调 `PlatformManager.CreateWindow()` 建出 HWND。所以钩子里取到的句柄真实可用，**声明早于首次合成**，不需要额外的「构造期首写」。

## 测试

- `CaptionThemeTests`：全局钩子对每个打开的窗口恰好触发一次 + 注册幂等；未 Show 窗口 Attach 静默；WindowOpened 前主题已解析为 Dark（深色应用路径前提）；`ResolveIsDark` 回退链决策表（Windows TFM）；零句柄不触 DWM。
- 全量：App 212×net10.0 + 218×windows TFM（本机 macOS 交叉编译/headless 跑），Core 不变。

## 验收（真机，QA-1 §Q）

- **最小验收动作 = 夜间模式退出进程 → 重开**：标题栏直接深色，无任何交互。
- 对话框逐个看（设置/云设置/Library/WindowPicker/更新提示）：各自 HWND 都有深色 caption。
- 改系统浅色主题 + 应用仍深色：标题栏保持深色；应用若切浅色，标题栏回白（显式清零生效）。
- 已知平台边界：Win10（本项目目标 17763+）由本卡负责；Win11 22000+ Avalonia 也会写同一属性，行为一致（幂等）。
