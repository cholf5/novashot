# I-1 M9 Ctrl+O 导入图片文件

- **优先级**: P2 ｜ **复杂度**: S ｜ **开发者**: 待领取 ｜ **状态**: To Do
- **依赖**: 无
- **关联**: winshot `Ctrl+O` 导入；M6c 交接坑 §4.2（SKBitmap.FromImage macOS SIGSEGV、CapturedImage 所有权）

## 目标

从文件打开图片进编辑器标注（winshot 已有；现在只能走剪贴板粘贴进来）。

## 子任务

1. `StorageProvider.OpenFilePickerAsync`（单选，图片扩展名过滤），入口：工具环按钮 + Ctrl+O 热键。
2. 文件 → `SKBitmap`（解码路径与 Paste 对齐，所有权交给 `CapturedImage`，**不要 `using var`**）。
3. 导入后进入与截图一致的编辑流程（来源标记可后续给 Library 用）。

## 验收标准

- PNG/JPEG/WebP 文件导入后标注/导出全流程正常；取消选择无副作用。

## 测试用例

- 扩展名过滤/解码失败提示逻辑单测；真机冒烟记入 QA-1。
