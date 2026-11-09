# C-1 M7 云上传 / Library / Updater / Pin

- **优先级**: P1 ｜ **复杂度**: L ｜ **开发者**: M7a+M7b 已完（2026-11-01），Updater 已完（2026-11-10） ｜ **状态**: Done
- **依赖**: 无（与 S-1 可并行）
- **关联**: `docs/handoff/261101-m6cd-handoff.md` §6（M7 起步建议）、`docs/roadmap.md`、参考代码 `references/winshot/internal/{upload,library,updater}`（Windows 机）

## 目标

补齐 winshot 的云端/效率三件套 + flameshot 招牌的 Pin 贴图。

## 子任务

1. ~~`Prtsc.Core/Cloud/`：`IUploadService` + R2 实现（S3 兼容 PUT，签名用 AWS SigV4 手写或最小依赖）；GDrive REST multipart；凭据键 `Prtsc_R2_*` / `Prtsc_GDrive_*`（**不迁移 WinShot 凭据**）。~~
   ✅ **M7a 完成（2026-11-01）**：`Cloud/SigV4Signer`（python 独立预计算向量锁定）+ `R2Uploader`（path-style PUT + SigV4 头 host;x-amz-content-sha256;x-amz-date，region=auto）+ `GDriveTokenClient`（refresh_token→access_token，缓存至过期前 60s）+ `GDriveUploader`（multipart/related metadata+bytes，可选 permissions anyone/reader → webViewLink）+ `ConfigUploadService`（按 Provider 每次分发，共享静态 HttpClient）+ `NullUploadService`。凭据改存 config.json `cloud` 节点（`r2.accessKeyId/secretAccessKey`、`gdrive.clientId/clientSecret/refreshToken`，camelCase；GDrive 节点钉为 `gdrive` 而非默认的 `gDrive`）——不迁移 WinShot 凭据。测试 402/402。
2. Library：~~`%AppData%\Prtsc\library.json` + 缩略图目录~~ ✅ **完成**（数据层 M7a + `LibraryWindow` UI M7b：缩略图列表/复制链接/打开/删除）；导出与上传均自动入历史。
3. Updater：~~参考 `UpdateConfig.CheckOnStartup / SkippedVersion`；若与 R-1 Velopack 打包协调，改用 vpk UpdateManager（见 R-1 卡，先沟通再动手）。~~
   ✅ **完成（2026-11-10，与 R-1 合并执行）**：方案按裁决接入 **Velopack 1.2.161 vpk UpdateManager**。
   - config：`UpdateConfig` 加 `FeedUrl`（string 默认空；JSON roundtrip + 未知字段容忍有测试）。
   - 决策下沉 Core 纯函数 `Update/UpdateCheckPolicy`：`ShouldCheckOnStartup(isPackaged, checkOnStartup, feedUrl)` 三条件与；`ShouldPrompt(update, current, skipped)`——System.Version 解析语义钉死（更高才提示；相等/更低不提示；同版本跳过生效；prerelease 串解析失败不提示），测试 20 例 + config 5 例。
   - 运行时接入：`Program.Main` 第一行 `VelopackApp.Build().Run()`（vpk 打包时自动验证此行存在）；`Startup/StartupUpdateChecker` fire-and-forget——未打包（`UpdateManager.IsInstalled` false，mac/linux 腿恒 false）/policy 拒绝/无网络/Feed 404 全部静默或状态栏提示，绝不阻塞启动。
   - UI：`UpdatePromptWindow` 非阻塞提示框（新版本号 + 下载并安装/跳过此版本/以后再说）；跳过写 `SkippedVersion` 存盘；下载带进度，`ApplyUpdatesAndRestart` 由 Velopack 接管重启。VM 决策抽出为 `UpdatePromptViewModel`（App.Tests 6 例）。
   - 设置页：常规分区 CheckOnStartup 勾选框下新增 FeedUrl 输入框 + hint（草稿+Save 模式）。
   - 全部新文案进 Loc 双语表；Velopack 调用全 try-catch 包裹。
   - API 注记：Velopack 1.x 里「是否已打包」判据是 `UpdateManager.IsInstalled`（简报里的 `IsPackaged` 是 0.0.x 旧名）；feedUrl 走 UpdateManager 构造器而非 CheckForUpdatesAsync 参数。
4. ~~Pin 贴图：导出/工具环入口 → 无边框 Topmost 置顶小窗显示截图，可拖动/缩放/双击关闭。~~ ✅ **M7b 完成**（`PinnedWindow`：Ctrl+P/工具栏 Pin 入口，拖动/滚轮缩放/双击/Esc 关闭）。

## 验收标准

- ~~配置 R2 或 GDrive 后从编辑器一键上传，成功后状态栏/剪贴板拿到 URL；无凭据时给出可读错误，不崩溃。~~ ✅ M7a/M7b 已验。
- ~~Library 能列出最近 N 张（缩略图 + 文件名 + 时间）并能再次打开/复制。~~ ✅ M7b 已验。
- ~~Pin 后的贴图窗口置顶、可拖动、可关闭，关闭不影响主编辑器。~~ ✅ M7b 已验。
- ~~Updater 检查启动开关生效~~ ✅ 单测钉死（policy 三条件与 + ShouldPrompt 语义）；联网检查部分在打包版内的端到端升级未真机验证（R-1 handoff 诚实清单，允许后置）。

## 测试用例（单测口径）

- R2 上传 URL/签名构造纯函数测试（固定凭据+时间戳 → 期望 Authorization 头）。✅
- GDrive multipart body 构造测试。✅
- Library 索引增删、损坏 json 容错。✅
- Pin 窗口逻辑以 App.Tests 覆盖可测部分（截图→窗口参数映射）。✅
- （新增 2026-11-10）UpdateCheckPolicy 决策用例 20 例 + UpdateConfig FeedUrl roundtrip/未知字段 5 例 + UpdatePromptViewModel 对话态 6 例。

## 备注 / 风险

- ~~Updater 方案与 R-1 有耦合，开工前先定 Velopack 还是自研轮询，避免返工。~~ ✅ 已定 Velopack 并落地，整卡完成。
- mac 上无 winshot 参考代码（在 Windows 机），upload 字段名对齐其 config.go。
