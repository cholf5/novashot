# U-1 透明背景预览要看得见（棋盘格占位）

- **优先级**: P1 ｜ **复杂度**: S ｜ **开发者**: cholf5 ｜ **状态**: Done（2026-11-03）
- **依赖**: 无（M6b 合成管线 / E-2 画布参数持久化之后暴露的可见性问题）
- **关联**: `Prtsc.Core.Editor.TransparencyChecker`、`SkiaCanvas.DrawOp`、`CompositionPanel.axaml`（None 项 ToolTip）

## 现象（用户报告）

> Background 选 None 时，预览里应该显示透明图层那种灰黑格子，现在什么都不显示，不知道那里有什么，结果存出来的图又有一圈透明。

根因是两层同色叠加：编辑器衬底是固定暗板 `#1A1A1A`，`BackgroundKind.None` 下内容区的透明部分（padding 环、圆角外角、透明源图）在预览里也呈现为同一块暗色，
所以画布边界不可见；而导出 PNG 保留 alpha，于是"预览什么都没有"的地方在成品里成了透明边框——用户是**导出后**才发现。

默认配置（padding 40 / 圆角 12）下几乎必然踩到：从渐变背景切到 None 时，提示环就出现了。

## 目标

`Background=None` 时，预览用暗色棋盘格标出"导出后是透明的"区域；占位格是编辑器 chrome，**永不进导出**。

## 子任务

1. Core 新增 `TransparencyChecker`：2×2 瓦片 bitmap shader 平铺，格宽取整（不出半像素灰线）、图案原点对齐矩形左上角（矩形移位时格子不"滑动"）、空矩形直接跳过。
2. `SkiaCanvas.DrawOp`：`!state.HasBackground` 时在 DIP 空间、内容区矩形内铺棋盘，画在文档合成之下（不透明内容自然盖住它）；`BackgroundKind` 为纯色/渐变时不画，避免 clip 亚像素导致背景边缘漏出细缝。
3. 面板 `None` 项加 ToolTip（`L.Panel.BackgroundNoneTip`，zh/en），点明"棋盘格 = 导出透明"以及"内边距归 0 就没有透明边框"。

## 验收标准

- Background=None：padding 环、圆角外角显示灰黑格子；纯色/渐变背景时预览与改动前一致。
- 导出 PNG 像素与改动前完全一致（占位格不进导出）。
- 格子为不透明中性灰，比衬底 `#1A1A1A` 亮一档（浅格 ≥ +0x18、深格 ≥ +0x08），既不刺眼也不回到"看不见"。

## 测试用例

- Core：`TransparencyCheckerTests`（6 例）——深浅交替、矩形外不落色、图案原点对齐矩形左上角、格宽生效、空矩形 no-op、默认配色相对衬底的亮度下限。
- App：`UiSnapshotTests` 新增 `05-editor-transparent-bg.png`（headless 快照，`PRTSC_SNAPSHOT=1`）供人工核对棋盘格观感。

## Verification（2026-11-03）

- TDD：先 RED（`TransparencyChecker` 不存在）→ 实现 → GREEN；`dotnet test tests/Prtsc.Core.Tests --filter TransparencyChecker` 6/6。
- 全量：Core 438/438、App 97/97 ×2TFM、Mac 80/80（单跑）、Linux 45/45。
- 快照核对：`PRTSC_SNAPSHOT=1 dotnet test --filter UiSnapshot`（net10.0），`artifacts/ui/05-editor-transparent-bg.png` 中 padding 环与圆角外角呈棋盘格，正文区域不受影响。
- 配色定稿过程：先 `#2E2E33 / #222226`，3× 放大后对比度偏低（用户原话是"什么都看不见"，淡纹理等于没修），改 `#38383F / #232329` 后 1:1 观感清晰且不抢画面；亮度下限用单测钉死。
- 存量 flake（与本卡无关）：`SettingsWindowXamlTests` 在全解决方案并行跑时偶发失败（单跑恒绿）；`MacClipboardServiceTests+ManualRoundtrip.Image_Roundtrip` 并行时偶发失败（单跑恒绿，真粘贴板争用）。
- 待真机冒烟：截图 → Canvas 页选 None → 确认 padding 环/圆角外角出现棋盘格 → Save 出 PNG 后仍是透明（无格子）→ Padding 拖到 0 后透明边框消失。
- **验收通过（2026-11-03，用户真机确认）**。
