# B-4 crop 后 reshape 手柄

- **优先级**: P3 ｜ **复杂度**: S ｜ **状态**: Done（2026-11-11） ｜ **来源**: M6d 交接遗留 backlog
- **关联**: `src/Prtsc.Core/Annotation/Editing/BoxHandles.cs`（现成）、`EditorState.CropRect`、`src/Prtsc.App/Controls/SkiaCanvas.cs`

## 目标

现在 crop 只能重新拖一个新框（X 工具），不能拖角微调已有 crop 框。复用 BoxHandles 做 crop 框的 reshape。

## 验收标准

- crop 激活时显示 8 手柄，拖动实时更新 `CropRect`（比例吸附仍生效），Esc 取消。

## 测试用例

- 手柄拖拽 → CropRect 更新纯逻辑单测（含比例吸附边界）。

## Verification（2026-11-11 本会话收尾）

- **落点**：新 Core 纯逻辑 `CropReshape`（`HitTest` = BoxHandles 8 手柄拓扑距离命中，slop 由 DIP 半径经视口换算，同 SelectTool 口径；`DragTo` = 手柄钳制[图像内 + 不越对边 ≥ MinPx=4]→ `BoxHandles.ApplyHandle` → 比例吸附[ratioW/H>0 时**锚定对边/对角**——区别于 `SnapToRatio` 的 TopLeft 锚定，拖哪边哪边跟手；边手柄可用 span 不足 → 收窄保比例，极端几何下比例让位于图像钳制] → MinPx 下限 + `ClampToImage` 兜底）。SkiaCanvas 接线：press 命中手柄即进 reshape（不新建框、不建 IAnnotationTool）、move 每步实时 `ApplyCrop`（不进 Undo，与既有 crop 语义一致）、up 点精确收尾、Esc/`PointerCaptureLost` 恢复拖拽起始框（`CancelCropReshape` 幂等共用）、`OnEditorChanged` 清拖拽态。渲染门控 = 无预览拖拽 + crop 工具激活 + 已应用框时 overlay（暗遮罩+白框）常驻 + 8 手柄（`DrawOp` 增 `cropToolActive` 参数）；预览拖拽（新框）期间不画手柄。
- **设计裁决**：① reshape 仅在 X 工具 + 已应用框时启用，其余工具不受影响（验收「crop 激活时」的口径）；② Esc 恢复拖拽起始框而非仅退出（可反复重试）；③ 比例吸附锚定对边/对角，使拖拽手感正确（复用 SnapToRatio 会拖哪边哪边不动）；④ `MinPx=4` 与 `CropTool.MinAreaPx` 同值不同义（提交阈值 vs reshape 下限），互引备注防单边调参分叉。
- **独立 review**：1 Critical + 0 Important + 4 Minor，**全处置**：C1 应用态框可被 SnapToRatio/细条拖拽压到 MinPx 以下（如 3:4 吸附 200×5 细条 → 宽 3.75），对边钳制区间反转 → `Math.Clamp` 抛异常崩 dispatcher → `ClampOrdered`（hi<lo 收敛到 lo）修复 + sub-MinPx 回归用例；M1 二次 press 守卫（防 `_cropReshapeOriginal` 被覆写成中间态）；M2 `OnEditorChanged` 清 reshape 态（防旧起始框写入新 editor）；M3 边手柄溢出分支注释改两级 fallback 口径；M4 MinPx↔MinAreaPx 互引备注。review 同时验证通过：逐帧 ApplyCrop 反馈回路为压缩映射无发散、Esc 链与 MainWindow Deselect 无冲突、release 先清 flag 再 Capture(null) 的 CaptureLost 幂等。
- **测试**：`CropReshapeTests` 16 例（命中 8 手柄/超 slop 未命中/容差、自由拖/锚定对角/单边、图像钳制、越对边 MinPx、sub-MinPx 无崩溃回归、比例 1:1/4:3/2:1/1:10 锚定、溢出收窄居中、贴边竖比例不变量、比例下 MinPx 精确值）。全量 Core 741 + App 190×2TFM + Mac 91 + Linux 46 全绿。
- **真机**：QA-1 TC17（手柄常驻/实时更新/比例吸附/边界钳制/Esc 恢复/新框覆盖回归）。
