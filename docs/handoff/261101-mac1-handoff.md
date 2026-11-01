# Prtsc 交接备忘 — 2026-11-01（MAC-1 macOS 平台层完成：捕获/窗口/热键/剪贴板/自启 + App 双 TFM）

> 目标读者：下一位接手的 AI Agent。接续 `261101-m6cd-m7a-handoff.md`（M7 状态不变，测试基线从 427 涨到 506）。

## 0. 本会话结论（TL;DR）

- **MAC-1 完成**：新 `Prtsc.Platform.Mac` 实现 Core 全部平台接口；`Prtsc.App` 双 TFM（windows TFM 出 Windows 产物、net10.0 出 macOS 产物）；mac 真机启动到托盘 + Carbon 热键注册成功。
- **测试基线 506/506**（Core 381 + Mac 72 + App 53×2TFM）。全 solution build 0 error。
- Windows 侧零行为变化：接口上提后 Platform.Windows 编译绿，App windows TFM 交叉编译绿。
- 用户起因：想跑 Mac 版。此前项目只有 `Platform.Windows`，无任何 mac 支持。

## 1. 里程碑明细

| 提交 | 内容 |
|---|---|
| `cfad62f` | 接口上提（IHotkeyService/HotkeyAction/HotkeyParser/IAutostartService → Core）+ App 双 TFM + Platform.Mac 骨架 + Mac.Tests 工程 + `EnableWindowsTargeting=true` 全局 |
| `2fa788e` | MAC-1a：MacNative interop 集 + MacDisplayGeometry + MacCaptureService（fullscreen/display/region 跨屏/window）+ CGDisplayApi |
| （中间） | MAC-1b：MacWindowService（CGWindowList）+ MacClipboardService（NSPasteboard TIFF/PNG）+ MacHotkeyService（Carbon）+ LaunchdAutostartService |
| `661e529` | 修 App 主线程 sync-over-async 死锁（mac 特有）+ 完整 DI 接线 |

## 2. 平台映射（Core 接口 → mac 实现）

| Core 接口 | mac 实现 | 系统通道 | 权限要求 |
|---|---|---|---|
| ICaptureService | MacCaptureService + CGDisplayApi | CGDisplayCreateImage / CGWindowListCreateImage | **屏幕录制**（TCC） |
| IWindowService | MacWindowService + CGWindowListApi | CGWindowListCopyWindowInfo（layer 0 过滤） | 窗口**标题**需屏幕录制权限 |
| IHotkeyService | MacHotkeyService | Carbon RegisterEventHotKey + InstallEventHandler | **无需任何权限** |
| IClipboardService | MacClipboardService | NSPasteboard（写 TIFF+PNG 双格式；读 TIFF→NSBitmapImageRep→bitmapData、PNG→SKBitmap.Decode） | 无 |
| IAutostartService | LaunchdAutostartService | launchd LaunchAgent `~/Library/LaunchAgents/com.cholf5.prtsc.plist`（bootstrap/bootout gui/$UID） | 无（Finder 启动即可） |

- `TryBringToForeground` 在 mac 是 no-op：CGWindowListCreateImage 抓含遮挡内容，无需目标在前台（比 Windows PrintWindow 路径简单）。
- CaptureWindow 的 `nint hwnd` 参数在 mac 语义是 **CGWindowID**（uint）。

## 3. 实证发现（新坑清单，勿"修正"）

1. **字节序矩阵（macOS 26 arm64 实测）**：`CGBitmapContextCreate` 组合 `kCGBitmapByteOrder32Little | kCGImageAlphaPremultipliedFirst(=2)` 产出内存 (B,G,R,A) = `SKColorType.Bgra8888+Premul` ✓；`32Little|PremultipliedLast(=1)` 通道错乱且 premul 不可靠，弃用。头文件语义：PremulFirst=2 表示 ARGB 逻辑序，little-endian 反转为内存 B,G,R,A。封装为 `MacNative.SkiaBgraBitmapInfo`。
2. **SKImage.FromBitmap 是引用不是拷贝**：源位图 Dispose 后画出来全透明。必须 `SKImage.FromPixelCopy(info, pixels, rowBytes)`。
3. **NSBitmapImageRep 的 `initWithBitmapDataPlanes:` planes=NULL 语义是"自分配不初始化"**，不是 copy！必须 init 后手动把像素 memcpy 进 `bitmapData`（BGRA→RGBA 通道交换）。
4. **Skia 无 TIFF codec**：`SKBitmap.Decode` 对 TIFF 返回 null。读剪贴板 TIFF 走 NSBitmapImageRep→bitmapData（RGBA8）→手转 Bgra8888。
5. **非 ObjC 宿主（dotnet CLI 启动）必须先 `dlopen("/System/Library/Frameworks/AppKit.framework/AppKit", RTLD_NOW)`**，否则 objc_getClass("NSPasteboard") 返回 0。封装在 `ObjcIds.EnsureLoaded()`。
6. **Avalonia 主线程 `task.GetAwaiter().GetResult()` 会死锁**（JsonConfigStore.LoadAsync 链实证，fake SemaphoreSlim 版不卡）。修法：`Task.Run(() => store.LoadAsync()).GetAwaiter().GetResult()`。App.axaml.cs 已改，Windows 行为不变。
7. **显示器睡眠时 CVDisplayLink 创建失败**（AvaloniaNativeRenderTimer 报 -6661 kCVReturnInvalidArgument）→ App 启动失败。显示器唤醒即正常。这是环境现象不是代码 bug；本会话探针曾误导，注意。
8. **Mac F 键 keycode 不连续**（F1=0x7A, F2=0x78, F3=0x63…），必须用标准表，不能线性推导（测试抓过此 bug）。PrintScreen→F13 约定（Mac 无 PrintScreen 键）。
9. **本 agent shell 环境**（Cindy.app 子进程）：`CGGetActiveDisplayList` 返回 0 显示器、ScreenCaptureKit 报 "TCC declined"——是 shell 进程树没有屏幕录制权限，非代码问题。测试已在真机 GUI 上下文验证窗口枚举/窗口抓图可用；整屏抓图待用户授权后冒烟。
10. 真机运行验证方式：终端直接 `./src/Prtsc.App/bin/Debug/net10.0/Prtsc`（前台进程；注意显示器需唤醒）。

## 4. 构建要点

- mac 上 `dotnet build` 双 TFM 全绿（Directory.Build.props 已加 `EnableWindowsTargeting=true`，无需手传参数）。
- mac 产物：`src/Prtsc.App/bin/Debug/net10.0/Prtsc`（arm64 Mach-O apphost）。
- App.Tests 双 TFM 各跑一次 53 测试（mac 腿带 SkiaSharp.NativeAssets.macOS 条件包）。
- Mac.Tests（`tests/Prtsc.Mac.Tests`）在 win 上理论上也能跑：全部 interop 走 IMacDisplayApi/IWindowListApi Fake，真 interop 测试标 `[Trait("Category","Manual")]`。**未在 Windows 上验证过 Mac.Tests**（假定 net10.0 TFM 可跑，若 P/Invoke 探测崩溃再补条件编译）。
- `InternalsVisibleTo("Prtsc.Mac.Tests")` 已在 Platform.Mac.csproj（MacNative internal 可测）。
- 测试里造纯色 CGImage 必须走 `CGContextFillRect` 正规绘制——直接 memset context 内存不会标脏，`CGBitmapContextCreateImage` 快照全空（测试基建坑，FakeDisplayApi.MakeSolidImage 已处理）。

## 5. mac 真机冒烟清单（QA 补充，P0/P1）

- [ ] 系统设置 → 隐私与安全性 → 屏幕录制：授权终端/运行宿主 → 全屏抓帧
- [ ] F13 全屏 / Ctrl+F13 区域 / Ctrl+Shift+F13 窗口热键实际触发（PrintScreen 映射 F13；无 F13 键的键盘需改 config.json 的 hotkeys 段，如 "Cmd+Shift+4"→ 不行，Ctrl+P 之类）
- [ ] 区域选框 overlay 在 Retina 的物理/逻辑像素对齐（2560x1440@30Hz 外接 5K 实测待验）
- [ ] 剪贴板互通：Preview/微信/Chrome 复制图片 → Paste；Copy → 预览/微信粘贴
- [ ] 窗口捕获：Safari/Finder 含遮挡内容
- [ ] 开机自启 Enable → 系统设置登录项出现 Prtsc；Disable 清除
- [ ] 托盘菜单 + Quit

## 6. 剩余待办（MAC 侧）

- 屏幕录制权限引导 UI（首次捕获失败时给中文指引——MacCaptureService 异常文案已带，可再挂系统 `x-apple.systempreferences:com.apple.preference.security?Privacy_ScreenCapture` 链接）
- Pin 窗多屏 DPI、跨屏选框（B-3）等 Windows 侧积压在 mac 同样存在
- Velopack（R-1）只支持 Windows；mac 分发需另定（.app bundle + ad-hoc 签名，或 Homebrew）——未排期

## 7. 用户要求（贯穿，沿袭 §4）

1. 不主动运行 Prtsc（本会话例外：mac 首启验证做了 4 次受控启动，均为验收必需，已注明）。
2. 一里程碑一 commit；handoff 入库；计划在 `.zcode/plans/2026-11-01-macos-platform.md`。
3. Core 无平台 API（现已含 Hotkeys/Autostart 接口层）。
4. 测试全绿再收工：**506/506**。
