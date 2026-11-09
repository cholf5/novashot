# I-1 Handoff — M9 Ctrl+O 导入图片文件（2026-11-10）

## TL;DR

**I-1 整卡 Done**：从文件打开图片进编辑器标注（此前只能剪贴板粘贴进来）。

- **View 接线**（解码/所有权/失败提示全复用 `MainWindowViewModel.ImportImageFile`，零改动）：
  `MainWindow.ImportImageFromPickerAsync` —— `StorageProvider.OpenFilePickerAsync` 单选，
  `FileTypeFilter` 两档：图片组（`ImageFileTypes.Patterns`）+ All files；取消（空结果）静默返回。
- **入口两处**：编辑器 ⋯ 菜单「打开图片文件…」（`L.Editor.Import`，Click 风格对齐 Library）+
  `OnKeyDown` Ctrl+O。
- **过滤真值下沉 Core**：`Core/Utils/ImageFileTypes`（Extensions / Patterns / IsSupported 纯函数）；
  `WindowsClipboardService.IsImageExtension`（HDROP 粘贴判定）改委托同一份真值。
- **i18n**：`Editor.Import / ImportDialogTitle / ImportTypeImages / ImportTypeAll` 双语在册。

测试基线只增不减：Core 578→**592**，App 157→**159**×2TFM，Mac **82**，Linux **46**，
合计 **1038 全绿**；build 0 Error（windows TFM mac 交叉编译通过）。

## 文件地图

| 文件 | 内容 |
|---|---|
| `src/Prtsc.Core/Utils/ImageFileTypes.cs` | 扩展名真值：Extensions（png/jpg/jpeg/webp/bmp/gif，winshot 对齐）、Patterns（`*.png`…）、`IsSupported(ext)` 大小写不敏感 |
| `src/Prtsc.Platform.Windows/Clipboard/WindowsClipboardService.cs` | `IsImageExtension` 改为委托 `ImageFileTypes.IsSupported`（删本地重复清单，行为不变） |
| `src/Prtsc.App/Views/MainWindow.axaml` | ⋯ MenuFlyout 分隔线后加 `L.Editor.Import` MenuItem（Click=`OnImportClick`） |
| `src/Prtsc.App/Views/MainWindow.axaml.cs` | `OnImportClick` / `ImportImageFromPickerAsync`（internal，测试可 await）/ `ImportPickedFiles`；`OnKeyDown` 头部加 Ctrl+O 分发 |
| `src/Prtsc.App/Localization/Loc.cs` | 4 key ×双语 |
| `tests/Prtsc.Core.Tests/ImageFileTypesTests.cs` | +14：集合钉面 / Patterns 一一对应 / 大小写·未知·空拒绝 |
| `tests/Prtsc.App.Tests/ImportImagePickerTests.cs` | +2×2TFM：headless 取消无副作用（空闲态 / 编辑器态） |

## 关键裁决与坑

1. **Ctrl+O 分发位置**：简报说「与既有 Ctrl+V/Ctrl+Q/Ctrl+U 同款分发路径」，但那批都在
   `if (_vm.Editor is not { } editor) return;` **之后**——照搬的话空闲态 Ctrl+O 是死的（编辑器态才有图），
   与卡片目标「现在只能走剪贴板粘贴进来」矛盾。故放在 editor guard **之前**，空闲/编辑器两态可用
   （对齐 winshot）；编辑文本中 / TextBox 拿焦时沿用同款防抢输入条件，不接管。
   语义后果：编辑器态按 Ctrl+O = 用文件图**替换**当前编辑器（`EnterEditor` 先 Dispose 旧实例，与
   编辑器态 Ctrl+V 重入同路径）。
2. **文件类型过滤两档**：图片组 + All files（`*.*`）。只给图片组的话，macOS NSOpenPanel 会硬拦
   「任意扩展」；加 All files 才让「扩展名不拦、解码说了算（失败 `Status.UnsupportedFormat`）」跨平台成立。
3. **headless 测试碰不得真平台装配（本批最大坑）**：走 `AppServices.Initialize` + 参数less
   `new MainWindow()` 时，windows TFM 在 mac 上会解析 `WgcCaptureService`，其 ctor 探
   `GraphicsCaptureSession.IsSupported()`（WinRT 激活）→ dlopen `api-ms-win-core-com` 失败直接
   **abort 测试进程**（原生崩溃，try-catch 拦不住）。两个 headless 测试改走 MainWindow **参数化构造**
   + 测试双 `ICaptureService`/`IWindowService`（`NoCaptureService`/`NoWindowService`）。
   后续任何要在 headless 实例化 MainWindow 的测试都别经 AppServices。
4. **headless 的 StorageProvider = NoopStorageProvider**（无 IPlatformStorage 时 TopLevel 兜底），
   `OpenFilePickerAsync` 恒回空列表且同步完成——恰好等价「用户取消」，取消无副作用两例由此驱动。
   真文件对话框只能真机验（见下）。
5. 无本地路径的选中项（云盘/虚拟文件）：Warning 日志 + `Status.ImportFailed` 既有文案，不静默吞。

## 诚实清单（未经真机验证）

1. 文件对话框实弹：图片组过滤展示、All files 切换、选中后 PNG/JPEG/WebP/BMP/GIF 解码 → 标注 →
   导出全流程、来源标记（CaptureSource.File）。
2. 取消选择真机无副作用（headless 只驱动了 Noop 空结果分支）。
3. HDROP 粘贴回归（G-11 路径改共用真值后，资源管理器复制图片文件 → Ctrl+V）。
4. 编辑器态 Ctrl+O 替换当前图、文本编辑中 Ctrl+O 不触发。
5. Windows 真机整体（延续现状：windows TFM 只过了编译 + 单测）。

## QA-1 / 冒烟

- 冒烟清单追加 **§L（I-1 导入图片真机段）**：`docs/qa/261102-smoke-checklist.md`。
- QA-1 卡测试清单追加 M9 段（TC11/TC12）。
