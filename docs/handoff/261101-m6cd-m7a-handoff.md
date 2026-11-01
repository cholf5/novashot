# Prtsc 交接备忘 — 2026-11-01（M7a 数据层 + M7b UI 完成；剩 Updater 等 R-1）

> 目标读者：下一位接手的 AI Agent。本文件取代前两版（261026 / 261101-m6cd-m7a-handoff）；历史坑清单已全部并入本文 §3。

## 0. 本会话结论（TL;DR）

- **M7a（数据层）+ M7b（UI）完成**：R2/GDrive 上传、Cloud 凭据配置、Library（索引+缩略图）、工具栏 Upload/Library/Pin/Cloud… 按钮、Ctrl+U/L/P 热键、PinnedWindow 贴图、LibraryWindow、CloudSettingsWindow、导出后自动入历史、AutoUpload、剪贴板文本能力。测试 **427/427**（Core 374 + App 53），build 0 error 0 warning（除 OverlayWindow 旧 AVLN5001）。
- **剩余（C-1 卡）**：Updater —— 等 R-1 定 Velopack 后用 vpk UpdateManager，勿提前自研轮询。
- 用户已确认 M7 方向 A：按原计划做云上传（R2/GDrive + Library + Updater + Pin）。

## 1. 进度表

| M | 状态 |
|---|---|
| M1–M6d | ✅ 全部完成（明细见 kanban Done 卡） |
| **M7a（数据层）** | ✅ **完成**：明细见 §5 |
| **M7b（UI）** | ✅ **完成**：明细见 §5b；C-1 卡仅剩 Updater（等 R-1） |
| M8–M11 | 按 roadmap |

测试基线：**427/427**（Core 374 + App 53）。build 0 warning（除 OverlayWindow 旧 AVLN5001）。

## 2. 近两个里程碑要点（只记增量）

**M6c**：`Prtsc.Core/Clipboard/`（DibCodec：BITMAPV5HEADER masks 在 124 字节头内部 offset 40–55，top-down，premul→opaque；IClipboardService；QuickSave）+ `WindowsClipboardService`（写 CF_DIBV5+CF_DIB 双格式，读 V5 优先）+ Copy/Ctrl+C、Paste/Ctrl+V、QuickSave/Ctrl+Q、Auto-copy（`Export.AutoCopyToClipboard`）。

**M6d**：`EditorState.CropRect`（可空）进 Composition 管线——**裁显示不裁数据**（越界标注 clip，撤销即恢复）；`CropRects.SnapToRatio`（TopLeft 锚定内缩）；`CropTool`（X 热键，`OnPointerUp` 恒返 null，结果经 `Committed` 由 SkiaCanvas 调 `editor.ApplyCrop`）；`EdgeColorExtractor`（边缘 2px 带量化直方图众数，面板 "A" 格）；三层坐标换算唯一实现在 `EditorViewModel.ImageToContent/ContentToImage/ImageRectToContent`（crop 激活时 content 原点 = crop 原点 + padding），SkiaCanvas 的重复 helper 已删。

## 3. 关键坑（新会话必读）

1. **`SKRect` 构造是 Left/Top/Right/Bottom**，不是 (x,y,w,h)；断言宽高用 `.Width/.Height`。
2. **`SKBitmap.FromImage` 产物在 macOS 逐像素访问可能 SIGSEGV**；Paste 的位图所有权归 `CapturedImage`，**不要 `using var`**。
3. windows-TFM 测试项目不传递 macOS Skia 原生库 → App.Tests 已加条件包 `SkiaSharp.NativeAssets.macOS`（仅 OSX condition）。
4. `SKBitmap.Erase(SKColor)` 先 premul 再写内存：要造 premul 值 (128,0,0,128) 应 `Erase(new SKColor(255,0,0,128))`。
5. `SKRect` 无 `.TopLeft`，用 `.Location`。
6. Mac 交叉编译：`EnableWindowsTargeting=true`（本机 SDK 10.0.101，Windows 机 10.0.301，无 global.json）。
7. CommunityToolkit `RelayCommand` 在 `CommunityToolkit.Mvvm.Input`；`[RelayCommand(CanExecute=…)]` 需在 `OnXxxChanged` partial 里手动 `NotifyCanExecuteChanged`。
8. BITMAPV5HEADER 语义见 §2 M6c；WinShot 式"头后 16 字节 palette"布局是错的（GDI 读者按 biClrUsed=0 严格解析）。

## 4. 用户要求（贯穿）

1. **不主动运行 Prtsc.exe**；build+test 绿即可。
2. "继续"即推进；一里程碑一 commit；handoff 入库 `docs/handoff/`；计划入 `.zcode/plans/`（gitignore）。
3. Core 无 Avalonia/Win32；Win32 进 Platform.Windows。
4. 人工冒烟持续欠着（QA-1 卡，P0）：X 裁剪/比例/A 取色、剪贴板与 Word/微信/Chrome 互通、Ctrl+Q 等。

## 5. M7a 完成明细（供 M7b 接手）

**`Prtsc.Core/Cloud/`**（全程无 Win32/无 Avalonia，HttpClient 可注入 FakeHandler 测试）：
- `SigV4Signer`：纯函数（HashHex/HmacHex/UriEncode/BuildCanonicalRequest/BuildStringToSign/BuildSigningKey/BuildAuthorizationHeader，`EmptySha256`）。⚠ AWS 文档签名向量与其日期**不自洽**，已改用 python 独立预计算的自洽向量锁定——不要"修正"回文档值。
- `IUploadService`/`UploadRequest`（构造时校验非空）/`UploadResult`（Success/Fail，不抛异常返回失败）+ `NullUploadService`（未配置→中文可读 Fail）。
- `R2Uploader`：path-style PUT `https://{AccountId}.r2.cloudflarestorage.com/{Bucket}/{Key}`（bucket 在 URI 路径里，签名 host=子域），SigV4 头 `host;x-amz-content-sha256;x-amz-date`，region=`auto`/service=`s3`；成功后拼 `{PublicUrl}/{Key}`（PublicUrl 空→成功但 Url=null）；缺凭据→可读 Fail 且不发请求。`BuildUploadTarget` 公开供向量测试。Authorization 向量同样 python 预计算锁定。
- `GDriveTokenClient`：refresh_token→access_token（oauth2.googleapis.com/token），缓存至过期前 60s，expires_in 缺失不缓存；凭据缺失/HTTP/解析错误抛异常（由上传器转 Fail）。
- `GDriveUploader`：multipart/related（metadata {name,parents?} + bytes，Bearer）；FolderId 空=根目录省略 parents；可选 MakePublic（POST permissions anyone/reader → `https://drive.google.com/file/d/{id}/view`）；**公开化失败不算失败**（上传已成功→Success(null)）。`BuildMetadata` 公开供测试。
- `ConfigUploadService`：每次上传读 `IConfigStore.Current.Cloud.Provider` 分发（r2/gdrive/none，大小写不敏感，未知值可读报错）——config 改动即时生效；HttpClient 共享静态实例。

**Config**：`Cloud.Provider("none")/AutoUpload(false)`、`R2.AccessKeyId/SecretAccessKey`、`GDrive.ClientId/ClientSecret/RefreshToken`（camelCase 明文存 config.json）。⚠ JSON 节点名钉成 `"gdrive"`（camelCase 策略默认会产生 `gDrive`，已 `[JsonPropertyName]` 覆盖）；旧 config.json 无新字段→默认值，反序列化有测试锁定。

**`Prtsc.Core/Library/`**：`LibraryEntry`（Id/CreatedAt/FileName/LocalPath/RemoteUrl/Provider/Width/Height/SizeBytes，显式 JsonPropertyName 小驼峰）；`LibraryStore`（`%AppData%\Prtsc\library.json`，.tmp 原子替换，Add 空Id自动生成+查重，Remove 按 Id，Load 缺失→空且**不建文件**，损坏→空+下次 Add 重建）；`ThumbnailWriter`（maxEdge 320 等比 PNG，小图不放大，目录自动创建）。

**App DI**：`IUploadService → ConfigUploadService`、`LibraryStore`（单例，默认 %AppData% 目录）已注册；`MainWindowViewModel` 构造已接 (clipboard, configStore, upload, library, logger)。

## 5b. M7b 完成明细（UI 层，接手 Updater 前必读）

**`Prtsc.Core` 新增**：
- `UploadCoordinator`（Cloud/）：编排 = 渲染编码（走 config.Export.DefaultFormat/JpegQuality）→ FilenamePatterns 命名 → 上传 → LibraryStore.Add + ThumbnailWriter（从上传字节解码生成 thumb_{id}.png）。Library 写失败不吞成功（Entry=null，Url 仍返回）。`NormalizeProvider`/`ContentTypeFor` 公开可测。
- `AppDataPaths`：%AppData%\Prtsc 统一路径（Root/Thumbs/LibraryFile）。
- `IClipboardService.TrySetText`：新增文本能力（上传后复制 URL）；Win32 实现 = CF_UNICODETEXT（EmptyClipboard+SetClipboardData，与图片同套路）；FakeClipboardService 记字段。

**`Prtsc.App` 新增/改动**：
- `PinnedWindow`：无边框置顶贴图（WindowDecorations="None"，注意别用回 SystemDecorations——已废弃）。拖动（Pointer capture）/滚轮缩放（0.1~5x，Ctrl 粗调）/双击/Esc 关闭；悬停 2s 提示条；位置逐张错开（PinWindowStatics.NextOffset）。**运行时加载器要求无参公共构造**（AVLN3001），参数构造为真正入口；位图所有权归窗体，Closed 时释放。
- `LibraryWindow` + `LibraryViewModel`：ListBox 缩略图列表（CreatedAt 倒序），复制链接（TrySetText）/打开（本地优先，否则 LaunchUriAsync 远程）/删除（RemoveAsync）；缩略图 thumb_{id}.png 异步补载。
- `CloudSettingsWindow` + `CloudSettingsViewModel`：渠道 ComboBox（none/r2/gdrive）+ 各凭据表单（Secret 打点）+ AutoUpload；保存走 Update+SaveAsync，缺项校验给中文提示。
- `MainWindow`：工具栏 Upload/Library/Pin/Cloud… 按钮；Ctrl+U 上传 / Ctrl+L 历史 / Ctrl+P Pin（走 OnKeyDown，与 Ctrl+S/Q/C/V 同层）；导出成功 → `RecordLibraryAfterExportAsync`（入历史+缩略图，失败只警告）→ AutoUpload 时 `UploadCurrentAsync`。ShowLibrary 是 internal（托盘复用，单例窗口）。
- `App`：托盘新增「截图历史…」「云上传设置…」。

**TDD 注意**：App.Tests 不能引用 Core.Tests 的 FakeHandler（不同程序集）——内联了 StubHandler；`UploadCommand.ExecuteAsync(null)` 是 xunit 亲生的无阻塞 API，别用 `.GetAwaiter().GetResult()`（xUnit1031）。

**未做（下会话候选）**：Pin 窗多屏 DPI 精化（现在按 96 DPI 基准）；Library 窗口「再次复制图片」（只做了链接与打开）；上传重试；AutoUpload 只在手动导出后触发，QuickSave/Copy 路径未接。

**TDD 顺序回顾**：Config 往返 → Null → R2（向量+FakeHandler）→ GDrive（token/multipart/MakePublic）→ 分发 → Library → DI。测试全绿后单 commit。

## 6. 提交清单（历史）

`658da39` M6d → `83f6323` handoff → `9022a30` SigV4 地基（358）→ `b7a7102` M7a 数据层（402）→ 本会话 M7b UI 单 commit（427）。

## 7. 剩余待办（C-1 收尾 + 后续）

- **Updater**：等 R-1 定 Velopack（vpk UpdateManager），勿自研轮询——R-1 卡风险注记。
- 人工冒烟（QA-1，P0）新增项：Upload（R2/GDrive 真凭据）、Ctrl+U/L/P、Pin 拖动/缩放/关闭、Library 三按钮、Cloud 设置保存往返、上传后 URL 粘贴到浏览器可用。
- M8 设置中心（S-1）做 GUI 时可吸收 CloudSettingsWindow 或保持独立窗口（二选一，先问用户）。
