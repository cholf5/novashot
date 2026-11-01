# Prtsc 交接备忘 — 2026-11-01（M6c + M6d 完成，M7a 已领取待新会话执行）

> 目标读者：下一位接手的 AI Agent。本文件取代 `261101-m6cd-handoff.md`（内容已并入）；更早坑清单 `261026-handoff.md` 仍有效。

## 0. 本会话结论（TL;DR）

- **M6c（剪贴板双向 + QuickSave）与 M6d（非破坏 Crop + 比例 + 边缘取色）完成**。
- 用户确认 **M7 方向 A：按原计划做云上传**（R2/GDrive + Library + Updater + Pin）。
- **M7a 已领取但未开工**——用户要求在**新会话里一次性做完**。本会话只提交了 M7a 的第一块地基（SigV4 签名器），其余全部留给新会话，规格见 §5。
- 看板/路线图已建：`docs/roadmap.md` + `kanban/board.md`（里程碑重编号 M7→M11，旧编号映射见 roadmap）。

## 1. 进度表

| M | 状态 |
|---|---|
| M1–M6d | ✅ 全部完成（明细见 kanban Done 卡与旧 handoff） |
| **M7a** | **地基已提交（`9022a30` SigV4），主体待新会话执行**——规格 §5，计划 `.zcode/plans/plan-m7a-cloud-library.md`，看板卡 `kanban/issues/C-1-m7-cloud-library-updater-pin.md` |
| M7b / M8–M11 | 按 roadmap；M7 的 UI（上传按钮/Library 窗口/Pin 窗）在 M7a 数据层之后 |

测试基线：**358/358**（Core 316 + App 42）。build 0 error（AVLN5001×1 可忽略）。

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

## 5. M7a 执行规格（新会话从这里开始）

**先跑**：`dotnet build --nologo`（0 error）+ `dotnet test --no-build --nologo`（358/358）。
**再读**：`kanban/issues/C-1-m7-cloud-library-updater-pin.md` + `.zcode/plans/plan-m7a-cloud-library.md`（本会话已写好完整计划与 TDD 顺序）+ `src/Prtsc.Core/Cloud/SigV4Signer.cs`（已就位，**勿重写**，测试 `tests/Prtsc.Core.Tests/Cloud/SigV4SignerTests.cs` 7 条已绿）。

**已有地基**：SigV4 纯函数（HashHex/HmacHex/UriEncode/BuildCanonicalRequest/BuildStringToSign/BuildSigningKey/BuildAuthorizationHeader，`EmptySha256` 常量）。注意 AWS 文档签名向量与其 2023 版日期**不自洽**（文档示例日期是 20130524 而正文写 2023），已改用 python 独立预计算的自洽向量锁定——不要"修正"回文档值。

**待做（TDD 顺序，全程 Core 无 Win32/无 Avalonia，HttpClient 可注入 FakeHandler）**：
1. Config 增字段 + 往返测试：`CloudConfig.Provider("none")/AutoUpload(false)`、`R2Config.AccessKeyId/SecretAccessKey`、`GDriveConfig.ClientId/ClientSecret/RefreshToken`（camelCase JSON，凭据明文存 config.json，与 WinShot 心智一致）。本会话已删掉红态测试占位 `tests/Prtsc.Core.Tests/Config/CloudConfigTests.cs`，新会话重建。
2. `IUploadService`/`UploadRequest`/`UploadResult` + `NullUploadService`（未配置→Fail 带可读消息）。
3. `R2Uploader`：PUT `https://{AccountId}.r2.cloudflarestorage.com/{Bucket}/{Directory}/{FileName}`，SigV4 头（host;x-amz-content-sha256;x-amz-date），成功后 `PublicUrl/Directory/FileName` 拼公开链接。
4. `GDriveTokenClient`（refresh_token→access_token）+ `GDriveUploader`（multipart related：metadata{name,parents}+bytes；可选 POST permissions anyone/reader 拿公开链接）。
5. `ConfigUploadService`：按 `Cloud.Provider` 分发（r2/gdrive/none），静态共享 HttpClient。
6. Library：`LibraryEntry` + `LibraryStore`（`%AppData%\Prtsc\library.json` 原子写、损坏容错）+ `ThumbnailWriter`（maxEdge 320 PNG）。
7. App DI 注册 `ConfigUploadService`；build+test 全绿 → 单 commit → handoff 增补。

**明确不做**：上传 UI 按钮/Library 窗口/Pin 窗（M7b）；Updater 实现（等 R-1 定 Velopack，见 C-1 卡风险注记）；凭据 DPAPI 加密；不迁移 WinShot 凭据。

## 6. 提交清单（本会话）

`658da39` M6d crop/ratio/edge-color → `83f6323` handoff → `9022a30` SigV4 地基（358/358）。
