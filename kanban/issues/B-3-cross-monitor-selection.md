# B-3 跨屏选框

- **优先级**: P2 ｜ **复杂度**: M ｜ **状态**: In Progress（2026-11-10 会话②认领执行中——**另一会话在做 B-8，请勿重复领 B-3**） ｜ **来源**: M6d 交接遗留 backlog
- **关联**: `src/Prtsc.App/Views/OverlayWindow.axaml.cs`、`src/Prtsc.App/Views/MainWindow.axaml.cs`（M2-3a 每屏一 overlay 已在，注释「跨屏选择留待 M2-3b」即本卡）、`src/Prtsc.App/Controls/SelectionAdorner.cs`/`SelectionGeometry.cs`、三平台 `CaptureRegion`
- **开工勘误（2026-11-10）**: 卡面「overlay 只覆盖主屏」已过时——M2-3a 起每屏一个 overlay（`MainWindow.CaptureRegionAsync` N 实例）；子任务 1（每屏 overlay 协同路线已选定）与子任务 3（Mac `MacDisplayGeometry.IntersectingDisplays` / Linux 同名 / Windows `WgcCaptureService.CaptureVirtualRect` 多屏合成 + GDI BitBlt 走虚拟屏 DC，三平台 CaptureRegion 均已支持跨屏 region 拼接）已在库。**本卡实际缺口 = 子任务 2 + 跨屏拖选的选框 UI 协同（M2-3b 共享坐标器）**。

## 目标

多显示器下 overlay/选框能跨屏（当前 overlay 只覆盖主屏，跨屏截图体验断裂）。

## 子任务

1. overlay 窗口铺满虚拟桌面包围盒（或每屏一个 overlay 协同）。
2. 选框坐标在跨屏 DPI 混合场景（125% + 100%）下的换算。
3. WGC/GDI 抓帧按最终选框合成跨屏位图。

## 验收标准

- 双屏（不同 DPI）从屏 A 拖选到屏 B，得到正确拼接快照。

## 测试用例

- 多屏 DPI 换算纯函数单测；真机双屏冒烟记入 QA-1。
