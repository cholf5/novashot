# Prtsc 交接备忘 — 2026-11-01（M7a 云上传数据层完成；下一步 M7b UI / Updater / Pin）

> 目标读者：下一位接手的 AI Agent。本文件取代前两版（261026 / 261101-m6cd-m7a-handoff）；历史坑清单已全部并入本文 §3。

## 0. 本会话结论（TL;DR）

- **M7a 完成**：SigV4 签名器 + R2 上传 + GDrive（token/multipart）+ ConfigUploadService 分发 + Cloud 凭据配置 + Library 数据层（library.json/原子写/损坏容错/ThumbnailWriter）。测试 **402/402**（Core 360 + App 42），build 0 error（AVLN5001×1 可忽略）。单 commit。
- **下一步 M7b**：上传 UI（编辑器导出入口/设置页）、Library 窗口（缩略图列表+再次打开/复制）、Pin 贴图窗；Updater 等 R-1 定 Velopack。
- 用户已确认 M7 方向 A：按原计划做云上传（R2/GDrive + Library + Updater + Pin）。

## 1. 进度表

| M | 状态 |
|---|---|
| M1–M6d | ✅ 全部完成（明细见 kanban Done 卡） |
| **M7a（数据层）** | ✅ **完成**：明细见 §5 与 kanban Done 卡 |
| M7b / M8–M11 | M7b=上传 UI/Library 窗口/Pin 窗；其余按 roadmap |

测试基线：**402/402**（Core 360 + App 42）。

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

**App DI**：`IUploadService → ConfigUploadService` 已注册（AppServices）。M7b 只需在 ViewModel/窗口层消费即可。

**TDD 顺序回顾**：Config 往返 → Null → R2（向量+FakeHandler）→ GDrive（token/multipart/MakePublic）→ 分发 → Library → DI。测试全绿后单 commit。

## 6. 提交清单（历史）

`658da39` M6d crop/ratio/edge-color → `83f6323` handoff → `9022a30` SigV4 地基（358/358）→ 本会话 M7a 单 commit（402/402）。

## 7. M7b 待办速记

- 上传入口：编辑器导出动作 + `Cloud.AutoUpload` 自动上传接线（数据层已就绪，`UploadRequest(fileName, bytes, contentType)`）。
- 上传成功后动作：状态提示 + URL 复制 + `LibraryStore.AddAsync` + `ThumbnailWriter.Write`（缩略图文件名建议 `thumb_{entryId}.png`）。
- Library 窗口：缩略图列表（时间/文件名/渠道标记），再次打开/复制/删除（`RemoveAsync`）。
- Pin 窗：无边框 Topmost，拖动/缩放/双击关闭，关闭不影响主编辑器。
- 设置页字段：渠道下拉（无/R2/GDrive）+ 对应凭据 + PublicUrl/Directory/FolderId + AutoUpload 开关（字段全部已在 config 就位）。
