# B-4 crop 后 reshape 手柄

- **优先级**: P3 ｜ **复杂度**: S ｜ **状态**: Backlog ｜ **来源**: M6d 交接遗留 backlog
- **关联**: `src/Prtsc.Core/Annotation/Editing/BoxHandles.cs`（现成）、`EditorState.CropRect`

## 目标

现在 crop 只能重新拖一个新框（X 工具），不能拖角微调已有 crop 框。复用 BoxHandles 做 crop 框的 reshape。

## 验收标准

- crop 激活时显示 8 手柄，拖动实时更新 `CropRect`（比例吸附仍生效），Esc 取消。

## 测试用例

- 手柄拖拽 → CropRect 更新纯逻辑单测（含比例吸附边界）。
