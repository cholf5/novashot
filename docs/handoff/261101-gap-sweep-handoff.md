# GAP-1 交接：参考项目差距全量扫描与第一批搬运（2026-11-01）

## 1. 做了什么

用户要求重扫 `references/flameshot`（C++/Qt，76 项功能清单）与 `references/winshot`（Go/Wails，66 项）源码，列出 prtsc 未搬功能并全部搬过来。

- 扫描明细与逐项决策表：`.zcode/plans/261101-references-gap-sweep.md`
- 4 个 commit（批1 工具语义 → 批2 捕获/剪贴板 → 批3 配置接线 → 批4 云连接测试）
- 测试基线 **553/553**：Core 414 + Mac 76 + App 63×2TFM（分项目跑全绿）

## 2. 本次搬入的功能（G-编号）

| 批 | 功能 | 来源 | 关键文件 |
|---|---|---|---|
| 1 | 反色工具（热键 I，PixelEffect 家族） | flameshot invert | `Core/Annotation/Model/InvertAnnotation.cs`（Difference-with-white） |
| 1 | 序号=最小未用正整数（删号回收） | winshot | `Core/Annotation/Tools/NumberSequenceMinUnused.cs`（NumberTool 工厂已切到它） |
| 1 | Ctrl+滚轮改选中序号值（1..999） | flameshot circlecount | `NumberAnnotation.SetNumber` + `SkiaCanvas.OnPointerWheelChanged` |
| 1 | 角度吸附：Line/Arrow Ctrl=八方向、Shift=正交；Rect/Ellipse Ctrl=对角 | flameshot drawMoveWithAdjustment | `Core/Annotation/Tools/AngleSnap.cs`（Snap45/SnapOrtho/SnapDiagonal）+ `SkiaCanvas.SnapPreviewPoint` |
| 1 | Esc 取消进行中拖拽（画布级，先于 Deselect） | 两家 | `SkiaCanvas.OnKeyDown` |
| 1 | Backspace 删除选中标注（与 Del 等价） | winshot 快捷键矩阵 | `MainWindow.axaml` KeyBinding |
| 2 | QuickSave 成功自动复制文件路径 | winshot | `MainWindowViewModel.QuickSave`（StatusText 带 "(path copied)"） |
| 2 | CF_HDROP 文件粘贴（资源管理器复制文件→Ctrl+V 进编辑器） | winshot clipboard.go | `IClipboardService.TryGetImageFilePath` + Win `DragQueryFileW`；mac 恒 null（无对应跨应用类型） |
| 2 | `ImportImageFile` 公共入口（I-1 Ctrl+O 已可直接用） | winshot | `MainWindowViewModel.ImportImageFile`（所有权随 CapturedImage 移交，勿 using） |
| 3 | CloseToTray（✕ 隐藏到托盘）/ MinimizeToTray（启动即隐藏） | winshot | `MainWindow.OnMainWindowClosing` / `App.OnFrameworkInitializationCompleted` |
| 3 | 窗口尺寸持久化（关闭写 config.Window，启动恢复） | winshot | 同上两处 |
| 3 | EnterEditor 应用编辑器默认值（背景/padding/圆角/阴影/内衬/边框全部从 config 进） | winshot editor 15 字段 | `MainWindowViewModel.ApplyEditorDefaults` + `GradientParser.ParseColor`（新 public） |
| 3 | IncludeBackground 接线（false=剥离背景板，保留圆角/裁剪） | winshot 假配置转真 | `Core/Editor/EditorStateStripping.StripBackground` |
| 3 | Library：Del/Backspace 删除（带确认框）、复制路径按钮 | winshot | `LibraryWindow.axaml(.cs)` |
| 3 | 托盘 tooltip 带版本号 | winshot | `App.SetupTrayIcon`（InformationalVersion） |
| 4 | 云连接测试按钮（R2=签名 HeadBucket，200/403=通；GDrive=换 token） | winshot HeadBucket/账号 | `Core/Cloud/CloudConnectionTester.cs` + CloudSettingsWindow「测试连接」 |

## 3. 决策不抄（理由见 plan 文档 C 节）

DBus/守护进程（Linux 专有）、上传历史窗口（Library 覆盖）、UI 颜色编辑器、网格吸附、放大镜选框（E-1 会带）、winshot 滚轮缩放画布（上游自己禁用）、winshot showNotification 假配置（不搬 bug）、全屏=光标屏（prtsc 保留整虚拟桌面语义）。

## 4. 坑与注意

1. **既有测试基线污染**：GAP-1 后 `EnterEditor` 会应用 config 默认值 → `CropFlowTests`/`EditorClipboardTests` 的 NewVm 必须显式清零 Editor.Padding 等，否则 ContentWidth 断言差 80。以后新建 VM 测试同样处理。
2. **solution 级并行跑测试有既有 flake**：`OverlayWindow_DragCreatesSelection` 偶发（渲染循环竞争，MAC-1 handoff 已登记 DisableParallelization collection）；分项目跑稳定。本次未引入新 flake。
3. **AngleSnap 吸附语义**：保留主分量（非保长度）——水平/垂直取较大位移分量，对角取 max(|dx|,|dy|)；容差 10°（`MaxSnapDegrees`）。
4. **Invert 渲染**：先 `DrawBitmap(_source, region)` 再 Difference 白矩形——测试需先 Clear 白底模拟管线（标注画在文档之上）。
5. **NumberAnnotation.Number 由 get; 改为 private set + SetNumber(clamp 1..999)**：构造函数同样钳位。
6. **mac TryGetImageFilePath 恒 null**：NSPasteboard 无跨应用文件列表类型可贴图，语义不同不支持。
7. **Avalonia VisualRoot 是 protected**：对话框按钮回调里直接闭包捕获 `Close()`（LibraryWindow 实例），不要 `b.VisualRoot`。

## 5. 剩余差距（未搬，已看板化）

Backlog：B-1（PNG 剪贴板格式）、B-2（WindowPicker 缩略图）、B-3（跨屏选框）、B-4（crop reshape）、B-6（工具栏自定义）、G-26（Capture Launcher，与 D-1 合并）。
To Do：C-1 剩 Updater、S-1、D-1/E-1/N-1/I-1（I-1 的 ImportImageFile 已备好）、L-1/L-2、R-1、QA-1（P0）。

## 6. 验证

- `dotnet build Prtsc.slnx`：0 警告 0 错误
- 分项目 `dotnet test`：Core 414 ✓ / Mac 76 ✓ / App 63×2 ✓
- 未真机启动（约定：build+test 绿即可）
