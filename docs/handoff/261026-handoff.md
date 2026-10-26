# Prtsc 项目交接备忘 — 2026-10-26

> 目标读者：下一位接手的 AI Agent。假设没有前面会话的上下文，从此文档开始阅读，
> 再按需翻上一篇 `260709-handoff.md`（M1–M3 时代的细节，坑清单仍有价值）。
> 项目根：`E:\dev\cholf5\prtsc`（win32，git 仓库，main 分支）

---

## 1. 当前状态

**Prtsc** = Windows-only 桌面截图工具（.NET 10 + Avalonia 12.1 + SkiaSharp 3.119.4），
融合 Flameshot（选框缩放 + 工具环 + 标注）与 WinShot（编辑器精修 + 云上传 + 库 + 自更新）。

### 里程碑进度

| M | 状态 | 关键交付 |
|---|---|---|
| M1–M3 | ✅ | 脚手架 / 选框 8-handles / 工具环 / WGC+GDI 抓帧 / Window picker |
| M4a–M4d | ✅ | SkiaCanvas 分层渲染、Rect/Ellipse/Line/Arrow(直+曲)/Text/Number/Spotlight、Select 工具、Undo/Redo(50)、样式面板、浮动文本编辑、工具热键 V R E O A L T N S、Ctrl+滚轮描边宽、Number max+1 重排、曲箭头控制点手柄 |
| **M5** | ✅（本次） | **Pencil / Highlighter（手绘矢量）+ Mosaic / Blur（采样型区域效果）**，热键 P H M B，Ctrl+滚轮调效果强度 |
| M6 | ← 下一步 | WinShot 精修：24 渐变背景 / Padding / CornerRadius / Shadow / Inset / Border / Auto edge color / 9:16 裁剪 / 导出 PNG-JPEG-WebP / 剪贴板双向 |
| M7 | pending | R2 + GDrive 上传 / Library / Updater / Pin 到桌面 |
| M8 | pending | CLI / 通知 / 快捷键自定义 / Velopack 打包 |

### 最后一次验证（2026-10-26）

```
dotnet build --nologo   → 0 error（AVLN5001 两条为已知可忽略）
dotnet test --no-build  → App 25/25 + Core 221/221 = 246/246 全绿
```

---

## 2. M5 的关键设计决定（重要，M6 导出要用）

### Mosaic / Blur 是"采样型标注"，不是"烤入位图"

总体方案（`.zcode/plans/plan-sess_0c970ac1-*.md` §4.4）原设想把马赛克/模糊栅格化写进图像副本
并用差分位图做 Undo。**实际实现走了另一条路**，M6 做导出/合成管线时务必按此理解：

- `PixelEffectAnnotation`（`src/Prtsc.Core/Annotation/Model/`）持有**非拥有**的
  `AnnotationDocument.Source` 引用 + 一个 `SKRect Region`。
- `Render` 时对 Source 的 Region 现场采样：
  - `MosaicAnnotation`：降采样到 ceil(w/BlockSize) 小图（缓存 key = Region+BlockSize，
    Source 会话内不可变所以安全）→ 最近邻拉伸回。
  - `BlurAnnotation`：Region 外扩 3σ apron → clip Region + `SKImageFilter.CreateBlur` 1:1 画回。
- 因此 Undo/Redo/移动/缩放直接复用 `AddAnnotationCommand` / `Move` / `Resize` 命令与
  `BoxHandles`，**零位图差分代码**；导出时走同一渲染管线效果自动包含。
- 已知取舍：两个效果区域重叠时，后画的采样原始 Source（不叠加），与 Flameshot 一致。

### SkiaSharp 3.119 API 注意

- **sampling 重载只在 `DrawImage` 上**：`DrawBitmap(bitmap, src, dest, SKSamplingOptions)` 不存在；
  需要控制采样时用 `SKImage.FromPixels(info, bmp.GetPixels(), bmp.RowBytes)` 非拥有包装后 `DrawImage`。
- `SKSamplingOptions` **没有** `Linear` 静态属性；用 `new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None)`，
  `default(SKSamplingOptions)` = 最近邻。
- `DrawBitmap(bitmap, srcRect, destRect, paint)` 存在（Blur 用它）。
- 光栅测试可直接 `new SKCanvas(SKBitmap)`，无需 SKSurface/GPU；SkiaSharp 原生库在测试进程正常加载（已验证）。

### 手绘标注（Pencil/Highlighter）

- `FreehandAnnotationBase`：点列 + 折线命中（`PolylineGeometry`，与 LineAnnotation 共享）；
  **无 reshape 手柄**（空数组），只能整体移动/删除——对齐 Flameshot。
- `HighlighterAnnotation`：`Multiply` 混合 + 宽度加成 `WidthBonus=14`；有效宽参与 BoundingBox/HitTest。
- `FreehandToolBase`：预览与提交同一实例就地生长；最小步长 1.5px 去抖；路径长 < 2px 不提交。

### 本轮新增热键

V R E O A L T N S + **P H M B**（Pencil/Highlighter/Mosaic/Blur）。
Ctrl+滚轮：选中 Mosaic → BlockSize 4..48；选中 Blur → Sigma 2..40；否则描边宽 1..16。

---

## 3. 新增/修改文件清单（M5）

**新建（Core，7）**：`Editing/PolylineGeometry.cs`、`Model/FreehandAnnotationBase.cs`、
`Model/PencilAnnotation.cs`、`Model/HighlighterAnnotation.cs`、`Model/PixelEffectAnnotation.cs`、
`Model/MosaicAnnotation.cs`、`Model/BlurAnnotation.cs`、`Tools/FreehandTools.cs`、`Tools/RegionEffectTool.cs`

**新建（测试，5）**：`PolylineGeometryTests` / `FreehandAnnotationTests` / `FreehandToolTests` /
`PixelEffectAnnotationTests` / `PixelEffectRenderTests` / `RegionEffectToolTests`（+42 测试）

**修改（App，4）**：`EditorViewModel.CreateActiveTool`（4 case）、`MainWindow.axaml`（4 RadioButton）、
`MainWindow.axaml.cs`（MapKeyToTool +4）、`SkiaCanvas.OnPointerWheelChanged`（效果强度分支）、
`StylePanelViewModel.SelectionKindLabel`（+4 标签）

---

## 4. 给下一位的起步建议（M6）

1. **先读**：`src/Prtsc.Core/Annotation/`（Document/模型/工具/History 四层）+
   `src/Prtsc.App/Controls/SkiaCanvas.cs` 的 `DrawOp.Render`——M6 的合成管线就是把它
   从"编辑器视图"抽成"可离屏复用的 Composition"（padding/shadow/radius/border/背景渐变
   都在这一层加，标注层顺序：背景 → inset → shadow → clip+原图+效果标注 → border → 矢量标注 → spotlight 暗化）。
2. **导出**：`SKSurface` 离屏 → 同一渲染顺序 → Encode PNG/JPEG/WebP（注意 `SKImage.Encode`，
  `SKBitmap.Save(Stream,int?)` 已废弃）。`FilenamePatterns` 与 `GradientPresets` 骨架 M1 就绪。
3. **剪贴板**：`Prtsc.Platform.Windows` 加 `CF_DIBV5` 写入（参考 winshot clipboard.go）；
   App 层用 Avalonia Clipboard。
4. 每子里程石 build + test 一次；`TreatWarningsAsErrors=false` 但保持 0 新警告。
5. 用户偏好不变：不主动跑 `Prtsc.exe`；编译 + 测试绿即可；一次子里程碑一个 commit。

---

## 5. 仍然有效的旧坑（摘自 260709-handoff，未过时）

- `Key.Enter` == `Key.Return`（同值 6），switch 不能双 case。
- Overlay 不要用 Deactivated 做取消（多屏互相触发）；Esc/工具环取消。
- Adorner/SkiaCanvas 坐标：内部全 DIP 或全 image-pixel，出口一次转换。
- `internal` Interop 类不能在 Prtsc.App 直接 P/Invoke，走 `IWindowService` 等抽象。
- Prtsc.Core 保持 `net10.0`（无 Windows TFM）；Win32 一律进 Prtsc.Platform.Windows。
- AVLN5001（SystemDecorations obsolete）等 Avalonia 12.2，先忽略。
