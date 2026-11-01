# X-1 反色工具（可选）

- **优先级**: P3 ｜ **复杂度**: S ｜ **状态**: Backlog ｜ **来源**: flameshot `invert` 工具
- **关联**: `PixelEffectAnnotation` / `RegionEffectTool` 基建现成（Mosaic/Blur 同族）

## 目标

flameshot 的小工具，选区内反色。基建齐全，实现成本低，随缘领取。

## 验收标准

- 区域反色效果可撤销、可导出，热键挂 i（若不与现有冲突）。

## 测试用例

- 反色像素语义单测（对齐 PixelEffectRenderTests 范式）。
