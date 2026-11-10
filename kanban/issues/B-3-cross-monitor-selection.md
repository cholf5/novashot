# B-3 跨屏选框

- **优先级**: P2 ｜ **复杂度**: M ｜ **状态**: Done（2026-11-10） ｜ **来源**: M6d 交接遗留 backlog
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

- 多屏 DPI 换算纯函数单测；真机双屏冒烟记入 QA-1（TC15/TC16，2026-11-10 登记）。

## Verification（2026-11-10 本会话收尾）

- **落点**：新 Core 层纯几何协调器 `SelectionSession`（权威选框 = 活动帧 DIP，输入按上报屏换算物理像素再映射；渲染按目标屏各自 ScaleFactor 映射出可越界矩形；钳制边界 = 虚拟桌面 union；确认输出物理 region）+ `SelectionGeometry.ClampAndNormalizeTo`（任意原点 bounds，原点可为负）+ 命中常量上提（GripHitHalfPad/EdgeHitSlop/MinSize 单一出处）。`SelectionAdorner` 状态机整体迁入 Session，收敛为渲染+事件转发+光标；`OverlayWindow` 事件转发/广播写回/确认走 `ConfirmRegion() ?? 本屏整屏` 回退；`MainWindow` 每会话构造一个 Session 注入全部 overlay。保留 M2-3a 每屏一 overlay，不改 CaptureRegion。
- **独立 review**：0 Critical / 0 Important（帧切换重表达顺序、跨窗事件一致性、订阅生命周期、装配点唯一性等危险面逐项验证成立）+ 4 Minor **全处置**：① 环显隐门改会话物理尺寸（原用本屏映射宽，高缩放屏上 4–7px 窄选框误失环）；② MixedDpi 测试改端点量化语义并钉住与旧口径可差 1px 的有意行为（渲染同口径，所见=所得）；③ Press 锚点同过物理网格量化（与 Drag 路径同源，缩放屏首段 dx 无网格差）；④ 跨屏 headless 用例补 B 屏环可见断言（原只有 A 屏隐的一半）。
- **测试**：`SelectionSessionTests` 15 例（单屏等价守卫=旧本地数学逐位、跨屏创建/接管/混合 DPI 往返、union 钳制、键盘微调跨屏、环归属、防重复 Press、幂等 Clear）+ `ClampAndNormalizeToTests` 4 例 + `OverlayWindowInputTests` 跨屏双 overlay 广播/确认 headless 用例 + 改造原选框摆放用例走真实拖拽（直接赋 adorner.SelectionRect 不再进会话态）。App 190/190×2TFM、Core 725/725、Mac 91/91 全绿。
- **真机**：双屏混合 DPI 冒烟入 QA-1 TC15（跨屏拖选/切片渲染/环归属/窄选框环存活）+ 单屏回归 TC16；headless 已覆盖广播与确认数学，真机项仅验窗口层与实屏拼接。
