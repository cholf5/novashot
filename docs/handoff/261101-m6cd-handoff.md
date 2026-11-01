# Prtsc 项目交接备忘 — 2026-11-01（M6c + M6d）

> 目标读者：下一位接手的 AI Agent。前一篇 `261026-handoff.md`（M5/M6b 时代，坑清单仍有效）。
> 本文覆盖 M6c 与 M6d 两个里程碑（同一会话连续完成）。

## 1. 进度表

| M | 状态 |
|---|---|
| M1–M5 | ✅（M5 = Pencil/Highlighter/Mosaic/Blur） |
| M6a / M6b | ✅ 合成管线 + 导出；背景/Padding/圆角/阴影/内衬/边框 + 面板 |
| **M6c** | ✅（`5ec5b95`）：剪贴板双向（CF_DIBV5+CF_DIB）+ Copy/Paste + Auto-copy + QuickSave(Ctrl+Q) |
| **M6d** | ✅（`658da39`）：非破坏 Crop + 比例预设 + Auto edge color |
| **M7** | ← **下一步**：R2/GDrive 上传 / Library / Updater / Pin（凭据键 `Prtsc_R2_*` / `Prtsc_GDrive_*`，不迁移 WinShot 凭据） |
| M8 / M9 | CLI/打包（headless 导出复用 `ImageExporter`）；v1.0 |

测试基线：**351/351**（Core 309 + App 42）。build 0 error（AVLN5001×1 可忽略）。

## 2. M6c 要点（剪贴板/QuickSave）

- `Prtsc.Core/Clipboard/`：`DibCodec`（SKBitmap↔DIB 字节。**BITMAPV5HEADER 的 masks 在 124 字节头内部** offset 40–55，像素紧随其后；top-down 负 biHeight；premul→opaque；读 32bpp/24bpp，畸形返回 null）；`IClipboardService`+Fake；`QuickSave`（空目录=~/Pictures/Prtsc）。
- `WindowsClipboardService`：写 CF_DIBV5+CF_DIB 双格式，读 V5 优先；GlobalAlloc 失败全路径 GlobalFree。
- App：命令挂 `MainWindowViewModel`（构造 `(IClipboardService, IConfigStore?, ILogger?)`）；Ctrl+C/V/Q + Auto-copy（`Export.AutoCopyToClipboard` 默认 true）。
- `ImageExporter.ParseFormat(string)`：config 格式串→枚举，未知回退 PNG。

## 3. M6d 要点（crop/比例/edge color）

- **非破坏 crop = EditorState.CropRect**（image 空间可空矩形）：`Composition.Measure/RenderScene` 里 crop 尺寸替代文档尺寸、内容渲染前 `ClipRect(crop 尺寸) + Translate(-crop 原点)`。**裁剪裁显示不裁数据**：越界标注被 clip，撤销 crop 即恢复。`Default_state_matches_bare_rendering_pixel_for_pixel` 锚点不破。
- `EditorState.Clamped(SKSizeI imageSize)` 新重载：crop 钳回图像内；App 的 `ApplyCrop` 在写入前先 `CropRects.ClampToImage`。
- `CropRects.SnapToRatio`：TopLeft 锚定沿最长维内缩；越界再整体缩小钳回。`CropTool`（`ToolKind.Crop`，热键 **X**）：拖矩形、`OnPointerUp` 恒返 null，结果经 `Committed` 由 SkiaCanvas 写 `editor.ApplyCrop`（带 `PendingCropRatio` 吸附）。
- **三层坐标唯一实现在 EditorViewModel**（`ImageToContent/ContentToImage/ImageRectToContent`，crop 激活时 content 原点 = crop 原点 + padding）——SkiaCanvas 的重复 helper 已删，DrawOp 内另有一份同式私有实现（持 state 快照）。
- 面板：Crop ratio ComboBox（auto/1:1/4:3/3:2/16:9/5:3/9:16/3:4/2:3，选中立即对当前/全图 Snap）；"A" swatch = `EdgeColorExtractor.Extract`（四边 2px 带量化直方图众数）设背景纯色。
- crop overlay（暗遮罩+白框）画在 DrawOp 里 DIP 空间，**只编辑器可见，永不导出**。

## 4. 本会话新踩的坑

1. `SKRect` 构造是 **Left/Top/Right/Bottom**（不是 size）——测试断言宽高要用 `.Width/.Height` 或比较 Right/Bottom。
2. `SKBitmap.FromImage` 产物在 macOS 直接逐像素访问可能 SIGSEGV（`sk_bitmap_get_info`）；Paste 的位图所有权归 `CapturedImage`，**不要 `using var`**。
3. windows-TFM 测试项目不传递 macOS Skia 原生库 → App.Tests 已加条件包 `SkiaSharp.NativeAssets.macOS`（仅 OSX）。
4. `Erase(SKColor)` 先 premul 再写内存：要造 premul 内存值 (r,g,b,a) 应 `Erase(new SKColor(R,G,B,a))`（RGB 分量给未预乘值）。
5. SKRect 无 `.TopLeft`，用 `.Location`。
6. Mac 交叉编译：`EnableWindowsTargeting=true`（本机 SDK 10.0.101 vs Windows 机 10.0.301，无 global.json 也能编）。

## 5. 用户要求（不变）

1. 不主动运行 Prtsc.exe；build+test 绿即可。
2. "继续"即推进；一里程碑一 commit；交接文档入库 `docs/handoff/`。
3. Core 无 Avalonia/Win32；Win32 进 Platform.Windows。
4. 人工冒烟持续欠着（本轮新增：X 拖框裁剪、比例 ComboBox、A 边缘取色、剪贴板与 Word/微信/Chrome 互通、Ctrl+Q 落盘）。

## 6. 建议接手路径

```
dotnet build --nologo             → 0 error（Mac 加 -p:EnableWindowsTargeting=true）
dotnet test --no-build --nologo   → 351/351
```

**M7 起步**：`Prtsc.Core/Cloud/` 定义 `IUploadService`（R2 = S3 兼容 PUT + `Prtsc_R2_*` config；GDrive = REST multipart）；Library = `%AppData%\Prtsc\library.json` + 缩略图目录；Updater 参考 `UpdateConfig.CheckOnStartup/SkippedVersion`。winshot 参考代码在 Windows 机的 `references/winshot/`（本 Mac 没有，upload 相关逻辑对齐其 config.go 字段名即可）。

遗留 backlog：粘贴只认 DIB（可加 RegisterClipboardFormat("PNG")）；crop 后再微调框（reshape 手柄）未做；导出质量滑杆 UI；WindowPicker 缩略图；跨屏选框；WGC 光标开关进 config。
