# Prtsc 项目交接备忘 — 2026-11-01（M6c 收官）

> 目标读者：下一位接手的 AI Agent。上一篇 `261101-handoff.md`（M6b 时写的）与更早的坑清单仍有效；本文只记增量 + 更新进度表。

## 1. 进度表更新

| M | 状态 |
|---|---|
| M1–M5 | ✅（见 261101-handoff §1 表） |
| M6a / M6b | ✅ |
| **M6c** | ✅（本会话，commit `5ec5b95`）：剪贴板双向（CF_DIBV5+CF_DIB）+ Copy/Paste + Auto-copy + QuickSave（Ctrl+Q） |
| **M6d** | ← **下一步**：非破坏 Crop + 常用宽高比 + Auto edge color（`EdgeColorExtractor` 骨架未建，参考 winshot extract-edge-color） |
| M7 | R2/GDrive 上传 / Library / Updater / Pin |
| M8 | CLI/打包（headless 导出直接复用 `ImageExporter`；Mosaic/Blur 采样语义见 261101-handoff §3） |
| M9 | v1.0 |

测试基线：**318/318**（Core 284 + App 34）。build 0 error（AVLN5001×1 可忽略）。

## 2. 本会话做了什么（M6c）

- **`Prtsc.Core/Clipboard/`**（新目录，Core 保持 net10.0 无 Win32）：
  - `DibCodec.cs`：SKBitmap ↔ DIB 字节纯转换。写出 CF_DIBV5（**masks 在 124 字节头内部** offset 40–55，像素紧从头后开始——没有 WinShot 式"头后 16 字节 palette 区"，GDI 读者按 biClrUsed=0 严格解析）；top-down（负 biHeight）；premul → opaque（unpremul 后 A=255）。读入 32bpp BGRA + 24bpp BGR（4 字节行对齐 stride），畸形输入返回 null。`FromDib(ReadOnlySpan<byte>)`。
  - `IClipboardService.cs`：`TrySetImage(SKBitmap)`（拷贝即快照）/ `TryGetImage()`；`FakeClipboardService` 内存实现供测试。
  - `QuickSave.cs`：`ResolveFolder`（空 = `~/Pictures/Prtsc`）+ `BuildFilePath`（走 `FilenamePatterns` 三模式 + 目录自动创建）+ `Save(config, doc, state)`（读 `Export.DefaultFormat/JpegQuality`）。
- **`Prtsc.Platform.Windows/Clipboard/WindowsClipboardService.cs`**：OpenClipboard → EmptyClipboard → SetClipboardData(CF_DIBV5+CF_DIB 双格式) → CloseClipboard；读取 V5 优先；GlobalAlloc 失败/SetClipboardData 失败都 GlobalFree 防泄漏。interop 加在 `User32.cs`（clipboard 常量 + 6 个函数）与 `Kernel32.cs`（GlobalAlloc/Lock/Unlock/Size/Free）。
- **App 层**：命令挂 **`MainWindowViewModel`**（不是 EditorViewModel——它要 StatusText 和 config）：`CopyCommand`/`QuickSaveCommand`（CanExecute = Editor 非空）/`PasteFromClipboardCommand`。构造函数签名变为 `(IClipboardService, IConfigStore?, ILogger?)`（AppServices 里手动工厂注册；无 Platform 时 IClipboardService 落 Fake）。
- **UI**：捕获态 "Paste from Clipboard (Ctrl+V)" 按钮；编辑器工具栏 Copy/Quick Save 按钮；热键 **Ctrl+C / Ctrl+V / Ctrl+Q**（MainWindow.OnKeyDown，编辑 Text 时禁用）。Ctrl+S 导出成功后按 `config.Export.AutoCopyToClipboard`（默认 true）自动 Copy，状态栏显示 "(also copied to clipboard)"。
- `ImageExporter.ParseFormat(string)`：config 格式串 → 枚举（未知回退 PNG）。

## 3. 本会话新踩的坑（实测验证）

1. **SkiaSharp `SKBitmap.FromImage` 跨段内存**：macOS 上由 SKImage 得到的 SKBitmap 调 GetPixel 会 0xB 崩溃（sk_bitmap_get_info SIGSEGV）。复制路径已改：渲染 SKImage → 立即 `SKBitmap.FromImage` 后只做批量像素拷贝；如果以后在 mac 测试里直接对 `FromImage` 产物逐像素操作又崩了，先怀疑这个。**Paste 路径的位图所有权归 CapturedImage，切勿 `using var`**（本会话真踩过：using 提前 Dispose → 编辑器渲染时 use-after-free 段错误）。
2. **windows TFM 测试项目不传递 macOS Skia 原生库**：`Prtsc.App.Tests`（net10.0-windows）的 deps.json 只有 NativeAssets.Win32/Linux，`libSkiaSharp.dylib` 缺失 → 测试主机 TypeInitializationException。已在 csproj 加 `<PackageReference Include="SkiaSharp.NativeAssets.macOS" Version="3.119.4" Condition="$([MSBuild]::IsOSPlatform('OSX'))" />`。Windows 机器不受影响，但若 mac 上新加 windows-TFM 测试项目要记得这条。
3. **`Erase(SKColor)` 会先 premul 再写内存**：`Erase(new SKColor(128,0,0,128))` 的内存 R≈64；要构造 premul 值 (128,0,0,128) 应 `Erase(new SKColor(255,0,0,128))`。
4. **BITMAPV5HEADER masks 位置**：offset 40（R）44（G）48（B）52（A），在头内部；像素 offset = 124。加了 `bV5CSType = 'sRGB'`（0x73524742）防部分读者按校准色板解释全 0。
5. CommunityToolkit `RelayCommand` 类在 `CommunityToolkit.Mvvm.Input`（8.4.0 有 `(Action, Func<bool>)` 构造）；`[RelayCommand(CanExecute=...)]` 源生成 `XxxCommand` 属性 + `NotifyCanExecuteChanged` 要在 `partial void OnEditorChanged` 里手动调。
6. 本机（Mac）交叉编译用 `EnableWindowsTargeting=true` 环境变量；仓库 SDK 无 global.json（Windows 机器 10.0.301，Mac 10.0.101 也能编）。

## 4. 用户要求不变

1. 不主动运行 Prtsc.exe；build+test 绿即可（人工冒烟仍欠着，重点新增：真机剪贴板与 Word/微信/Chrome 互通、Ctrl+Q 落盘目录）。
2. "继续"即推进；一个子里程碑一个 commit + build/test。
3. Core 无 Avalonia/无 Win32；Win32 进 Platform.Windows。
4. 不迁移 WinShot 凭据。

## 5. 未完成事项（按优先级）

1. **M6d 裁剪 + Auto edge color**：非破坏 crop overlay（编辑态画不导出）、9:16 等比例、`EdgeColorExtractor`（用截图边缘主色做背景纯色，与 EditorState.BackgroundColor 打通）。config `Editor.OutputRatio`/`AutoBackground` 字段已备好。
2. backlog：导出质量滑杆/格式 UI（config 字段已就位，QuickSave 已在用）；WindowPicker 96×54 缩略图；跨屏选框；WGC 光标开关进 config。
3. 人工冒烟（用户侧）：M4–M6c 全部 UI 只验证过编译+单测。
4. 粘贴互操作扩展：当前只认 DIB；浏览器/Electron 常只放 PNG 格式，M6d 可评估 `RegisterClipboardFormat("PNG")` 读取。

## 6. 建议接手路径

```
dotnet build --nologo             → 0 error（Mac 加 -p:EnableWindowsTargeting=true）
dotnet test --no-build --nologo   → 318/318
```

阅读顺序：`src/Prtsc.Core/Clipboard/DibCodec.cs`（字节布局注释全在头部）→ `tests/Prtsc.Core.Tests/Clipboard/DibCodecTests.cs`（字节序锁定范式）→ `src/Prtsc.Platform.Windows/Clipboard/WindowsClipboardService.cs` → `MainWindowViewModel.cs` 尾部三个命令 → `MainWindow.axaml.cs` OnKeyDown / ExportCurrentAsync。config 无新增字段（`ExportConfig.AutoCopyToClipboard/DefaultFormat/JpegQuality` + `QuickSaveConfig.Folder/Pattern` 全部预置已用上）。

M6d 起步：先写 `EdgeColorExtractor` 的失败测试（输入 SKBitmap → 边缘像素主色；参考 winshot extract-edge-color 思路，winshot 仓库在本项目 Windows 机的 references/ 下，Mac 上没有），再做 crop overlay（注意 SkiaCanvas 三层坐标 DIP/content/image，动视口映射三层都要过）。
