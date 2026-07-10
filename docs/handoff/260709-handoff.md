# Prtsc 项目交接备忘 — 2026-07-09

> 目标读者：下一位接手的 AI Agent。假设你没有前面会话的上下文，从此文档开始阅读。
> 项目根：`E:\dev\cholf5\prtsc`（win32，无 git）

---

## 1. 当前任务目标

**Prtsc** = Windows-only 桌面截图工具，融合 Flameshot（选框缩放 + 悬浮工具环 + 标注）与 WinShot（编辑器精修 + 云上传 + 库 + 自更新）。

**技术栈**：
- .NET 10.0.301 SDK, `.slnx` 新方案格式
- Avalonia **12.1.0**（Controls / Media / Skia / Themes.Fluent / Fonts.Inter / Desktop）
- SkiaSharp **3.119.4**（被 Avalonia.Skia 12.1.0 钉死此版本）
- CommunityToolkit.Mvvm 8.4.0，Microsoft.Extensions.* 10.0.0
- Serilog 4.2.0（File 6.0.0 + Console 6.0.0）
- Vortice.Direct3D11 3.8.3（M3-4 引入 WGC 抓帧后加）
- TargetFramework：`net10.0-windows10.0.19041.0`（Prtsc.App / Prtsc.Platform.Windows），Prtsc.Core 用 `net10.0`

**完成标准（按里程碑）**：M1 脚手架 → M2 选框 → **M3 工具环 + WGC + Window 捕获**（刚完成）→ M4 编辑器基础 → M5 补齐 Flameshot 工具 → M6 WinShot 精修 → M7 云上传/库/更新 → M8 CLI/打包 → M9 v1.0。详见 `.zcode/plans/plan-sess_0c970ac1-...md`（会话计划文件）。

**下一位 Agent 的直接目标**：进入 **M4（编辑器基础）**，或按用户指令处理其它优先级项。

---

## 2. 当前进展

### 已完成里程碑

| M | 状态 | 关键交付 |
|---|---|---|
| M1 | ✅ | 5 项目工程 / Avalonia 主窗口 / MVVM / DI / Serilog / TrayIcon / 全局热键 / 开机自启 / GDI 抓帧 / OverlayWindow 原型 / xUnit |
| M2 | ✅ | `SelectionAdorner` + `SelectionGeometry` / 8 handles + 边线 + 键盘微调 + Ctrl 锁比例 + Shift 中心对称 / 多屏 N-overlay 首确认首取消 / geometrySettled 400ms 去抖 / Enter+双击 + DIP→物理像素 |
| M2-3b | 🕐 pending（低优） | 跨屏选框 OverlayCoordinator |
| M3-1 | ✅ | `FloatingToolRing` + Flameshot ButtonHandler 5-候选放置算法（下→右→上→左→内嵌）|
| M3-3 | ✅ | `IWindowService` + `WindowEnumerator`（EnumWindows + DWM cloak/frame）+ `WindowPickerDialog` + `MainWindow.CaptureWindowAsync` + 托盘/热键联动 |
| M3-4 | ✅ | WGC 主路径（CaptureWindow/Display/Fullscreen/Region）+ 全部路径带 GDI 回退 + WARP fallback + Win11 22H2 border/cursor 反射设置 |

### 测试计数
- `Prtsc.Core.Tests`：12 / 12（`FilenamePatternsTests`）
- `Prtsc.App.Tests`：25 / 25（`SelectionGeometryTests`）
- 共 **37 / 37 通过**，`dotnet build` **0 warning / 0 error**

### 最后一次会话内构建/测试记录
```
dotnet build → 成功，0 warn 0 err
dotnet test --no-build → 12/12 + 25/25
```

---

## 3. 关键上下文

### 用户明确要求（贯穿整个项目）

1. **不迁移** 现有 WinShot 用户的 config 或凭据。凭据键名前缀预留 `Prtsc_R2_*` / `Prtsc_GDrive_*`（M7 用）。
2. 主窗口沿用 **WinShot 单窗口**模型（捕获态 ↔ 编辑器态切换），不是 Flameshot 多浮层。
3. **选框放大缩小是核心差异化**——WinShot 缺，Flameshot 有；必须先落。
4. 两阶段流：overlay 只做选框 → 确认后 **关闭 overlay 再进入编辑器**。
5. 保留 WinShot 全部特性（GDrive/R2 上传、Library、Updater、24 渐变、编辑器合成管线顺序），补齐 Flameshot 差异（Pencil/Highlighter/Mosaic/Blur/Number-counter/Pin）。
6. 用户偏好：不要主动运行 `Prtsc.exe`；除非明确要求，编译通过即可。
7. 用户风格：多次直接说"继续"或"1"（选项编号）作为推进指令。

### 已做的关键设计决定

- **DIP vs 物理像素分层**：Adorner 内部全 DIP；`OverlayWindow.TryConfirmSelection` 出口做 `DipToPhysical(dip, display)`（乘 `display.ScaleFactor` + 加 `display.Bounds.X/Y`）。
- **选框会话首确认/首取消**：`MainWindow.CaptureRegionAsync` 里 `completed` 布尔哨；所有 overlay 共享 CloseAll，避免 N 个 overlay 各自触发。
- **移除 Deactivated → Cancel**：多 overlay 场景下会互相误触发，改为只 Esc / 工具环取消。
- **WGC 与 GDI 并存**：`WgcCaptureService` 是 primary `ICaptureService`，注入 `GdiCaptureService` 具体类型作为每层调用的 fallback。
- **Vortice 而非 SharpDX**：SharpDX 已停维护；Vortice.Direct3D11 3.8.3 是当前主流的 .NET D3D11 绑定。
- **WinRT 活化通过 RoGetActivationFactory**：不依赖 `WinRT.ActivationFactory.Get<T>()`（版本差异大），显式 `combase.dll` P/Invoke + `Marshal.GetObjectForIUnknown` 转 `GraphicsCaptureItem`。
- **Editor 状态管理**：M1 已经有 `MainWindowViewModel.EnterEditor(CapturedImage)` 骨架 + `CurrentImage` 属性 + 简单的 Image 预览。M4 要在这之上盖 `SkiaCanvas`。

### 环境约束

- Windows 10.0.26200（≈ Win11 24H2），x64
- .NET 10.0.301 SDK，`net10.0-windows10.0.19041.0` TFM 提供 CsWinRT WinRT projections
- Serilog 输出到 `%AppData%\Prtsc\logs\prtsc-yyyyMMdd.log`
- Config 在 `%AppData%\Prtsc\config.json`
- 全局热键：M1 阶段默认 `Alt+Shift+1/2/3` 分别对应 Fullscreen/Region/Window

---

## 4. 关键发现

### 编译/构建
- **`Key.Enter` 与 `Key.Return` 在 Avalonia 中同枚举值 6** → switch 里不能同时 case。已在 `OverlayWindow.axaml.cs` 处理。
- **`Window.SystemDecorations` 在 12.1 标 obsolete**（AVLN5001），但功能仍在；rename 到 `WindowDecorations` 是 12.2 的事。保持不动，忽略此 warning。
- **Vortice `MapFlags` 与 `Vortice.DXGI.MapFlags` 冲突** → 显式限定 `Vortice.Direct3D11.MapFlags.None`。
- **Avalonia 编译期绑定要求 DataType**：`WindowPickerDialog.axaml` 的 DataTemplate 需 `x:DataType="local:WindowPickerDialog+Row"`，Row 必须是 `public` 嵌套类。

### 运行时
- **多 overlay 的 Deactivated 事件会互相触发** → 已改为只响应 Esc / 工具环。
- **PrintWindow 对某些窗口返回 0 或黑帧**（DRM/受保护/远程会话）→ `GdiCaptureService.CaptureWindow` 自动 BitBlt 回退。
- **WGC 无 CreateForVirtualScreen** → 全屏必须逐显示器抓帧 + Skia 合成，见 `WgcCaptureService.CaptureVirtualRect`。
- **`GraphicsCaptureSession.IsSupported()` 在 Win10 <1903 返回 false**，构造期一次判定 → `_wgcSupported` 缓存。
- **WGC 首帧延迟**：`FrameArrived` 通常 30-100ms 触发；预留 timeout **1500ms**（`WgcCaptureService.FrameTimeout`）。

### 未验证的运行时假设 ⚠️
- **`Marshal.GetObjectForIUnknown(itemPtr) as GraphicsCaptureItem` 是否在所有 CsWinRT 版本上工作**：理论上应该 OK（IInspectable 是 IUnknown 的超集），但如果失败会抛 `InvalidCastException`，此时已 catch 并回退 GDI。**下一位如遇 WGC 完全走不通，先看这里**。备选：切到 `WinRT.MarshalInspectable<GraphicsCaptureItem>.FromAbi(ptr)`（需引 `Microsoft.Windows.CsWinRT` 显式包）或 `GraphicsCaptureItem.FromAbi(ptr)`（若 CsWinRT projection 有此静态）。
- WGC 的 `IsCursorCaptureEnabled` / `IsBorderRequired` 用反射设置，Win11 22H2+ 生效，老平台默默失败。

---

## 5. 未完成事项

### 立即可做（按优先级）

| 优先级 | 项 | 描述 |
|---|---|---|
| P0 | **M4 编辑器基础** | SkiaCanvas 分层（背景/图片/标注/选择框）、6 种矢量标注（Rect/Ellipse/Arrow 直+curved/Line/Text/Number/Spotlight）、Select-Move-Delete-Transformer、命令模式 Undo/Redo（maxHistory=50）、工具热键 V R E O A L T N S |
| P1 | **WGC 运行时验证** | 用户手动跑一次，看 WGC 路径是否真的落地（当前只是"编译通过 + 有 GDI 回退"）。看 log 里 `WGC supported:` 与 `WGC ... failed` 频率 |
| P1 | **WindowPicker 缩略图** | 现在 picker 只显示标题+进程+尺寸；加 96×54 缩略图（复用 `WgcCaptureService.CaptureWindow` 或 `PrintWindow`）会显著提升 UX |
| P2 | **M2-3b 跨屏选框** | 当前一个 overlay 只处理本屏；跨屏拖拽需要 `OverlayCoordinator` 共享 mouse 全局位置 |
| P2 | **WGC 光标开关** | 目前反射设 `IsCursorCaptureEnabled = true`；应暴露到 config，config 里加 `bool CaptureCursor = true` |

### 后续里程碑（大件，按 plan 顺序）

- **M5**：Flameshot 补齐工具（Pencil / Highlighter / Mosaic / Blur / Number counter 共享状态 / Ctrl+Wheel）
- **M6**：WinShot 精修（24 渐变 / Padding / CornerRadius / Shadow / Inset / Border / Auto edge color / 9:16 aspect crop / 导出 PNG-JPEG-WebP / 剪贴板双向）
- **M7**：R2 (AWSSDK.S3) + GDrive (Google.Apis.Drive.v3) 上传 / Library 历史 / GitHub Releases 更新 / Pin 到桌面
- **M8**：CLI (System.CommandLine) / 通知 / 快捷键完全自定义 / Velopack 打包
- **M9**：性能压测 / bug bash / v1.0

---

## 6. 建议接手路径

### 首要阅读顺序

1. **`.zcode/plans/plan-sess_0c970ac1-fc46-432c-ad13-16a3a2daadb2.md`** — 总体方案（含项目结构、里程碑、风险表）。
2. **`src/Prtsc.App/ViewModels/MainWindowViewModel.cs`** — 主状态机（Capture ↔ Editor）。
3. **`src/Prtsc.App/Views/MainWindow.axaml(.cs)`** — 三种捕获入口 + 编辑器切换。
4. **`src/Prtsc.Core/Capture/ICaptureService.cs` + `CapturedImage.cs`** — 抓帧协议。
5. **`src/Prtsc.Platform.Windows/Capture/WgcCaptureService.cs`** — WGC 主路径 + GDI 回退结构。
6. **`src/Prtsc.App/Controls/SelectionAdorner.cs` + `SelectionGeometry.cs`** — 选框实现范例，M4 的标注工具可以参考这里的交互层/几何层分离。
7. **`tests/Prtsc.App.Tests/SelectionGeometryTests.cs`** — 25 个测试；M4 的每个几何工具都应有类似的纯函数单测。
8. **`src/Prtsc.App/AppServices.cs` + `Prtsc.Platform.Windows/WindowsServiceCollectionExtensions.cs`** — DI 组合。

### 先验证什么

1. `cd /d E:\dev\cholf5\prtsc && dotnet build --nologo` 应该 `0 warning 0 error`。
2. `dotnet test --no-build --nologo` 应该 `37 / 37 passed`。
3. 如果 build 挂在 Vortice 包上（网络不可用），检查 `%UserProfile%\.nuget\packages\vortice.direct3d11\3.8.3\` 是否存在。
4. 手动确认 `%AppData%\Prtsc\config.json.tmp` **不存在**（应为原子写完成后的正常状态）。

### 推荐 M4 起步动作

1. 在 `src/Prtsc.App/Controls/` 建 `SkiaCanvas.cs`（继承 `Control`，用 `ICustomDrawOperation` 拿 `SKCanvas`）。
2. 在 `src/Prtsc.Core/Annotation/Model/` 建 `AnnotationBase`（record 或 class）+ `RectangleAnnotation` / `EllipseAnnotation` / `ArrowAnnotation` / `LineAnnotation` / `TextAnnotation` / `NumberAnnotation` / `SpotlightAnnotation`。
3. 在 `src/Prtsc.Core/Annotation/Tools/` 建工具状态机接口 `IAnnotationTool` + 各具体工具。
4. 在 `src/Prtsc.Core/Annotation/History/` 建 `Command` 模式的 `UndoRedoStack`（maxHistory=50）。
5. `EditorViewModel` 分离出来，独立于 `MainWindowViewModel` 但由主 VM 拥有。
6. 让 `MainWindow` 的编辑器态用 `SkiaCanvas` 替代当前简单的 `Image` 预览。

**M4 时长估算**：2 周业余 / 1 周全职。子里程碑建议：M4a SkiaCanvas 骨架、M4b 6 种矢量标注、M4c Undo/Redo 命令模式、M4d 工具切换 + 热键 + 属性面板。

---

## 7. 风险与注意事项

### 已踩过的坑，勿重蹈

1. **不要用 `Key.Return`** 与 `Key.Enter` 同 case——重复标签编译错误。
2. **`Bitmap.Save(Stream, int?)` 已 obsolete** → `AppIconFactory.EncodePng()` 已改成 `SKImage.Encode`；新代码沿用。
3. **Avalonia 编译期绑定 + private class** → AVLN2000。DataTemplate 用私有嵌套类必须 public 化，并加 `x:DataType`。
4. **不要把 Deactivated 用作 Overlay 取消**——多屏 N-overlay 之间会互相 deactivate。
5. **`internal User32` 不能在 Prtsc.App 直接 P/Invoke**——只能透过 `IWindowService` 抽象。类似情况：其它 `internal` Interop 类都是同样限制。
6. **Vortice 与 DXGI 有同名 `MapFlags`** → 全限定 `Vortice.Direct3D11.MapFlags`。
7. **Adorner 的坐标别混 DIP/物理像素**——Adorner 内全 DIP，出口一次转换。M4 的 SkiaCanvas 应遵循同规则。

### 已验证过，不需重复调查

- Avalonia 12.1 的 `TrayIcon` + `NativeMenu` 可用，`Icon` 属性接受 `WindowIcon`。
- `RegisterHotKey` + 消息专用窗口方案已跑通，见 `WindowsHotkeyService`。
- Registry `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\Prtsc` 开机自启方案已跑通。
- `PerMonitorV2` DPI awareness 已在 `app.manifest` 声明。
- `Shcore.GetDpiForMonitor` 返回每屏物理 DPI，无需 fallback。
- `GDI CaptureRegion` 用 `SRCCOPY | CAPTUREBLT` 能抓分层窗口。
- `WinShot` 的 24 渐变预设已硬编码进 `Prtsc.Core.Utils.GradientPresets`（M1 已放置骨架，M6 补齐使用逻辑）。
- `FilenamePatterns` 三种模式（timestamp / date / increment）已单测覆盖，M6 复用即可，不用重写。

### 容易走偏的方向

- **不要引 SharpDX**——已停维护，Vortice 是替代。
- **不要在 Prtsc.Core 引 Windows-specific 依赖**——Prtsc.Core 是 `net10.0`（无 windows TFM），任何 Win32 都得在 Prtsc.Platform.Windows。
- **不要把 Annotation 模型放到 Prtsc.App**——应该放 Prtsc.Core.Annotation，让 M8 CLI 也能复用（headless 渲染）。
- **不要用真 Avalonia 透明窗口做暗化**——已改为绘制预抓帧位图 + 暗色矩形（Flameshot 策略），换回来会在部分 GPU 上闪烁。
- **不要给 OverlayWindow 加 Deactivated 取消**——见上文踩坑。
- **不要在 M4 一次做完所有工具**——先落一个（Rect），验证 SkiaCanvas + Adorner 交互 + Undo 通路，再扩其它工具。

### 已知但暂不处理

- Avalonia AVLN5001 `SystemDecorations` obsolete → 等 Avalonia 12.2 API 尘埃落定。
- WGC 首帧偶尔黑帧 → 目前 1500ms 内一定收到帧才算成功，未做多帧丢帧策略。
- `MarshalInterface<T>` 的 helper 是简化实现，遇 CsWinRT 特殊 projection 可能失败 → 有 catch。

---

## 下一位 Agent 的第一步建议

**Step 1 · 环境验证（≤ 2 分钟）**
```
cd /d E:\dev\cholf5\prtsc
dotnet build --nologo                    # 期望 0/0
dotnet test --no-build --nologo          # 期望 37/37
```
如果失败，先看 Vortice 3.8.3 是否在 NuGet 缓存里；如果编译错误全是 CS0xxx，说明基础已崩，先修基础再谈 M4。

**Step 2 · 与用户确认下一里程碑**
用户上次说的推进方向是 **M4（编辑器基础）**。可以直接问："上次收官了 M3，下一步进 M4 编辑器基础可以吗？" 得到确认（或 "继续"/"1"）后启动 M4。

**Step 3 · M4a 起手**
按 §6 的推荐动作，从 `SkiaCanvas.cs` 开始，第一个可跑的形态是 **"编辑器加载 CapturedImage，SkiaCanvas 渲染，能用鼠标画一个 Rectangle 并 Undo"**。这是 M4 的最小闭环，落成即证明分层架构成立，剩下 5 种标注是模板化工作。

**Step 4 · 保持"每子里程碑跑一次 build+test"节奏**
用户默认希望每完成一个子任务就 build/test 一次并同步进度报告。TodoWrite 用来跟踪子任务，先规划再执行。

**心态提示**：这个项目本质上是把 Flameshot（C++/Qt）+ WinShot（Go/Wails/React）两个成品的能力叠加到 Avalonia + SkiaSharp。**遇到设计抉择时优先参考这两个原项目的解决方案**——它们都是跑了多年的成品，用户对它们的行为很熟悉。
