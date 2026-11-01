# C-1 M7 云上传 / Library / Updater / Pin

- **优先级**: P1 ｜ **复杂度**: L ｜ **开发者**: M7a 数据层已完（2026-11-01），M7b/Updater/Pin 待领取 ｜ **状态**: In Progress
- **依赖**: 无（与 S-1 可并行）
- **关联**: `docs/handoff/261101-m6cd-handoff.md` §6（M7 起步建议）、`docs/roadmap.md`、参考代码 `references/winshot/internal/{upload,library,updater}`（Windows 机）

## 目标

补齐 winshot 的云端/效率三件套 + flameshot 招牌的 Pin 贴图。

## 子任务

1. ~~`Prtsc.Core/Cloud/`：`IUploadService` + R2 实现（S3 兼容 PUT，签名用 AWS SigV4 手写或最小依赖）；GDrive REST multipart；凭据键 `Prtsc_R2_*` / `Prtsc_GDrive_*`（**不迁移 WinShot 凭据**）。~~
   ✅ **M7a 完成（2026-11-01）**：`Cloud/SigV4Signer`（python 独立预计算向量锁定）+ `R2Uploader`（path-style PUT + SigV4 头 host;x-amz-content-sha256;x-amz-date，region=auto）+ `GDriveTokenClient`（refresh_token→access_token，缓存至过期前 60s）+ `GDriveUploader`（multipart/related metadata+bytes，可选 permissions anyone/reader → webViewLink）+ `ConfigUploadService`（按 Provider 每次分发，共享静态 HttpClient）+ `NullUploadService`。凭据改存 config.json `cloud` 节点（`r2.accessKeyId/secretAccessKey`、`gdrive.clientId/clientSecret/refreshToken`，camelCase；GDrive 节点钉为 `gdrive` 而非默认的 `gDrive`）——不迁移 WinShot 凭据。测试 402/402。
2. Library：~~`%AppData%\Prtsc\library.json` + 缩略图目录~~ ✅ **数据层完成**（`Library/LibraryStore` 原子写/损坏容错/Remove 按 Id + `ThumbnailWriter` maxEdge 320）；编辑器/托盘入口查看历史 → M7b。
3. Updater：参考 `UpdateConfig.CheckOnStartup / SkippedVersion`；若与 R-1 Velopack 打包协调，改用 vpk UpdateManager（见 R-1 卡，先沟通再动手）。
4. Pin 贴图：导出/工具环入口 → 无边框 Topmost 置顶小窗显示截图，可拖动/缩放/双击关闭。

## 验收标准

- 配置 R2 或 GDrive 后从编辑器一键上传，成功后状态栏/剪贴板拿到 URL；无凭据时给出可读错误，不崩溃。
- Library 能列出最近 N 张（缩略图 + 文件名 + 时间）并能再次打开/复制。
- Pin 后的贴图窗口置顶、可拖动、可关闭，关闭不影响主编辑器。
- Updater 检查启动开关生效（联网检查部分可 mock，允许后置到 R-1 打包落地时联调）。

## 测试用例（单测口径）

- R2 上传 URL/签名构造纯函数测试（固定凭据+时间戳 → 期望 Authorization 头）。
- GDrive multipart body 构造测试。
- Library 索引增删、损坏 json 容错。
- Pin 窗口逻辑以 App.Tests 覆盖可测部分（截图→窗口参数映射）。

## 备注 / 风险

- Updater 方案与 R-1 有耦合，开工前先定 Velopack 还是自研轮询，避免返工。
- mac 上无 winshot 参考代码（在 Windows 机），upload 字段名对齐其 config.go。
