# B-2 WindowPicker 缩略图

- **优先级**: P3 ｜ **复杂度**: S ｜ **状态**: Backlog ｜ **来源**: M6c 交接遗留 backlog
- **关联**: `src/Prtsc.App/Views/WindowPickerDialog.axaml.cs`

## 目标

窗口选择对话框加 96×54 实时缩略图（当前只有标题列表，认窗口靠猜）。

## 验收标准

- Picker 列表每项显示窗口缩略图，模糊/最小化窗口降级为占位图标不崩溃。

## 测试用例

- 缩略图获取失败降级逻辑单测；真机冒烟记入 QA-1。
