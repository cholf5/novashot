# I-1 M9 Ctrl+O 导入图片文件

- **优先级**: P2 ｜ **复杂度**: S ｜ **开发者**: batch6 (ZCode) ｜ **状态**: Done
- **依赖**: 无
- **关联**: winshot `Ctrl+O` 导入；M6c 交接坑 §4.2（SKBitmap.FromImage macOS SIGSEGV、CapturedImage 所有权）

## 目标

从文件打开图片进编辑器标注（winshot 已有；现在只能走剪贴板粘贴进来）。

## 子任务

1. `StorageProvider.OpenFilePickerAsync`（单选，图片扩展名过滤），入口：编辑器 ⋯ 菜单按钮 + Ctrl+O 热键。
2. 文件 → `SKBitmap`（解码路径与 Paste 对齐，所有权交给 `CapturedImage`，**不要 `using var`**）。
3. 导入后进入与截图一致的编辑流程（来源标记可后续给 Library 用）。

## 验收标准

- PNG/JPEG/WebP 文件导入后标注/导出全流程正常；取消选择无副作用。

## 测试用例

- 扩展名过滤/解码失败提示逻辑单测；真机冒烟记入 QA-1。

## 完成备注（2026-11-10，batch6）

小卡按简报执行，解码/所有权路径零改动（复用 `MainWindowViewModel.ImportImageFile`，G-11 时共建）：

1. **选择器**：`MainWindow.ImportImageFromPickerAsync`——`StorageProvider.OpenFilePickerAsync`
   单选；`FileTypeFilter` 两档：图片组（`Core.Utils.ImageFileTypes.Patterns`：png/jpg/jpeg/webp/bmp/gif）
   + All files（`*.*`，让「任意扩展也能试解码」跨平台可达，解不动走 `Status.UnsupportedFormat` 既有提示）。
   取消（空结果）静默返回无副作用；无本地路径（云盘等）→ Warning 日志 + `Status.ImportFailed` 既有文案。
2. **入口两处**：编辑器 ⋯ 菜单 `L.Editor.Import`（Click 风格对齐 Library）；
   `OnKeyDown` 加 Ctrl+O——**分发在 editor guard 之前**，空闲/编辑器两态都可用（空闲态是「无图进
   编辑器」的主路径，对齐 winshot；既有 Ctrl+V/C/Q/U/L/P 都在 guard 之后、编辑器态专属），
   编辑文本中/TextBox 拿焦时不接管（同款防抢输入）。来源标记 `CaptureSource.File` 已有。
3. **过滤真值下沉 Core**：`Core/Utils/ImageFileTypes`（Extensions/Patterns/IsSupported 纯函数）；
   `WindowsClipboardService.IsImageExtension`（HDROP 粘贴）改为委托同一份真值，删除本地重复清单。
4. **i18n**：4 key 双语在册（`Editor.Import` / `ImportDialogTitle` / `ImportTypeImages` / `ImportTypeAll`），
   LocKeyConsistencyTests 守卫通过。
5. **测试**：Core +14（Extensions 集合钉 winshot 面、Patterns 一一对应、IsSupported 大小写/未知/空拒绝）、
   App +2×2TFM（headless 取消无副作用：空闲态不进编辑器/状态栏不动、编辑器态不动当前编辑器；
   headless 的 StorageProvider 落 NoopStorageProvider 恒回空，恰好等价「取消」分支）。
   基线 1020 → **1038 全绿**（Core 592 / App 159×2TFM / Mac 82 / Linux 46）。
6. **坑**：headless 测试必须走 MainWindow 参数化构造——经 AppServices 解析真平台腿时，
   windows TFM 在 mac 上 `WgcCaptureService` ctor 探 `GraphicsCaptureSession.IsSupported()`
   （WinRT 激活）直接 abort 进程（api-ms-win-core-com 无法加载，try-catch 拦不住原生崩溃）。
7. 真机项（文件对话框 PNG/JPEG/WebP/BMP/GIF 全流程、All files 选非图 → UnsupportedFormat、
   取消无副作用、HDROP 粘贴回归）→ QA-1 + 冒烟清单 §L。
