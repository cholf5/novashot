# B-3 跨屏选框

- **优先级**: P2 ｜ **复杂度**: M ｜ **状态**: Backlog ｜ **来源**: M6d 交接遗留 backlog
- **关联**: `src/Prtsc.Platform.Windows/Capture/VirtualDesktop.cs`、`Screens`（Avalonia）

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
