# Done 汇总卡：M1–M6d（2026-11-01 收官）

- **状态**: Done（用户验收随 QA-1 真机冒烟补账）
- **测试基线**: 351/351（Core 309 + App 42），build 0 error（AVLN5001×1 可忽略）

## 覆盖里程碑

| 里程碑 | 内容 | commit |
|---|---|---|
| M1–M3 | 脚手架、选框 8-handles、工具环、WGC+GDI 抓帧、Window picker | `ef195a9`（init 基线） |
| M4a–M4d | SkiaCanvas 分层渲染、Rect/Ellipse/Line/Arrow(直+曲)/Text/Number/Spotlight、Select+Undo/Redo、样式面板、工具热键 | `ef195a9`→`4b86720` |
| M5 | Pencil / Highlighter / Mosaic / Blur | `26f8b29` |
| M6a/M6b | 合成管线 + 图像导出；背景/Padding/圆角/阴影/内衬/边框 + 面板 | `4b86720`/`711bd94` |
| M6c | 剪贴板双向（CF_DIBV5/CF_DIB）+ Copy/Paste/Auto-copy + QuickSave | `5ec5b95` |
| M6d | 非破坏 Crop + 比例预设 + Auto edge color | `658da39` |

## 备注

- 逐里程碑细节与踩坑见 `docs/handoff/`（260709 / 261026 / 261101-m6cd）。
- 真机人工冒烟统一走 QA-1 卡。
