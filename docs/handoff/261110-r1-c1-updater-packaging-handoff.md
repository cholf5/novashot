# R-1 + C-1 Handoff — Velopack 打包 + Updater 收尾（2026-11-10）

## TL;DR

两卡合并完成：**C-1 整卡 Done**（Updater 最后一块），**R-1 Done**（打包 + CI）。
方案按既定裁决走 **Velopack（vpk 1.2.161 / Velopack 1.2.161）**：

- 运行时：`Program.Main` 第一行 `VelopackApp.Build().Run()`；启动后 fire-and-forget 更新检查（Core 纯函数决策 → 未打包/无 feed/无网络全部静默）→ 有新版弹非阻塞提示框（下载并安装 / 跳过此版本 / 以后再说）。
- 配置：`UpdateConfig` 新增 `FeedUrl`；设置页常规分区新增输入框 + hint。
- 打包：`scripts/pack-windows.sh`（self-contained win-x64 publish → `vpk '[win]' pack`），**本机 macOS 交叉打包实跑成功**，产物 Setup.exe + Portable.zip + full.nupkg + feed 索引。
- CI：`.github/workflows/ci.yml` 三平台测试矩阵 + tag `v*` release job（版本断言 → pack → Release 传产物 + SHA256SUMS）。
- 图标：`assets/app.ico`（`scripts/make-app-icon.py` 可复现，品牌渐变对齐编辑器默认背景）。

测试基线 837 → **880 全绿**（Core 527 / App 114×2TFM / Mac 80 / Linux 45），零删除。（review pass 后口径：874 + StartupUpdateCheckerTests 3 例×2TFM）

## 文件地图

| 文件 | 内容 |
|---|---|
| `src/Prtsc.Core/Update/UpdateCheckPolicy.cs` | 决策纯函数：`ShouldCheckOnStartup`（isPackaged×开关×feedUrl 三条件与）、`ShouldPrompt`（System.Version 语义：更高且 != skipped 才提示；prerelease 串解析失败不提示） |
| `src/Prtsc.Core/Config/PrtscConfig.cs` | `UpdateConfig.FeedUrl`（默认空） |
| `src/Prtsc.App/Program.cs` | `VelopackApp.Build().Run()` 于 Main 最早处（vpk 打包时自动验证此行；未打包安全空转） |
| `src/Prtsc.App/Update/StartupUpdateChecker.cs` | 启动检查 fire-and-forget：构造 `UpdateManager(feedUrl)`（try-catch）→ `IsInstalled` → policy → `CheckForUpdatesAsync` → ShouldPrompt → UI 线程弹窗；所有异常只记日志/状态栏 |
| `src/Prtsc.App/Update/UpdatePromptViewModel.cs` | 对话框 VM（版本文案 / 下载进度 / 用户三选一；App.Tests 6 例） |
| `src/Prtsc.App/Views/UpdatePromptWindow.axaml(.cs)` | 非阻塞提示框：Install（DownloadUpdatesAsync 进度→ApplyUpdatesAndRestart）/ Skip（写 SkippedVersion + SaveAsync）/ Later |
| `src/Prtsc.App/ViewModels/SettingsViewModel.cs` | FeedUrl 草稿 + Save（Trim 落盘） |
| `src/Prtsc.App/Views/SettingsWindow.axaml` | 常规分区 FeedUrl 输入框 + hint |
| `src/Prtsc.App/Localization/Loc.cs` | 新增 12 个 key（En/Zh：`Settings.General.FeedUrl/FeedUrlHint`、`Update.Prompt.*`×9、`Status.UpdateCheckFailed`） |
| `src/Prtsc.App/Prtsc.App.csproj` | `Velopack 1.2.161`（双 TFM 都引用，mac restore/build 已验） |
| `scripts/make-app-icon.py` | 生成 `assets/app.ico`（PIL，16–256 六档；依赖 Pillow） |
| `scripts/pack-windows.sh` | publish + vpk pack；`--version` 参数或读 Directory.Build.props；channel 钉 `win` |
| `assets/app.ico` | 品牌 .ico（渐变 #667eea→#764ba2 + 白相机剪影） |
| `.github/workflows/ci.yml` | 测试矩阵（windows/ubuntu/macos）+ tag release job |
| `tests/Prtsc.Core.Tests/Update/UpdateCheckPolicyTests.cs` | 21 例（相等/更低/更高/四段修订/跳过版/跳过版非法/prerelease 解析失败/v 前缀解析失败等） |
| `tests/Prtsc.Core.Tests/Config/UpdateConfigTests.cs` | 4 例（默认值 / roundtrip / 旧 config 缺节点 / update 节点未知字段容忍） |
| `tests/Prtsc.App.Tests/UpdatePromptViewModelTests.cs` | 6 例（Lede 含双版本 / 默认 Undecided / 三选一 / busy 态隐藏动作 / busy 中选择忽略 / 进度钳制） |
| `tests/Prtsc.App.Tests/StartupUpdateCheckerTests.cs` | 3 例（review pass 新增：空/空白 FeedUrl 静默返回不碰 Velopack 与 owner；测试进程 locator 未设 → 构造 catch 兜住不崩） |

产物输出（gitignore 内，不入库）：`artifacts/pack/`（publish 目录 + vpk 产物）。

## 关键坑（都是实跑踩出来的）

1. **`vpk '[win]' pack` 的 `[win]` 是字面 directive 语法**（System.CommandLine directive），不是文档占位符。
   不带它时 vpk 按**宿主 OS** 选打包器：macOS 上会走 osx 打包器并拒收 `.ico`（要求 `.icns`）。
   带上后明确走 Windows 打包器（Setup.exe / .ico / releases.win.json）。Windows 宿主上默认即 win，带 directive 亦合法。
2. **macOS 上 vpk 默认 channel 是 `osx`**——必须显式 `-c win`，否则 feed 文件名变成 releases.osx.json，运行时 `UpdateManager` 默认 channel 对不上。
3. **Velopack 1.x API 与简报措辞的差异**：
   - 「是否已打包」= `UpdateManager.IsInstalled`（`IsPackaged` 是 0.0.x 旧名，1.x 已无）；
   - feedUrl 传 **UpdateManager 构造器**，`CheckForUpdatesAsync()` 无参；
   - `VelopackLocator.Current` 在 `VelopackApp.Build().Run()` 未跑时会 throw——所以 Main 第一行必须先 Run()（也是官方要求）；
   - `new UpdateManager(url)` 抛不抛**只取决于 URL 形态**（空/空白抛 ArgumentException，反编译确认；与环境是否打包无关）。未打包 + 合法 URL → `IsInstalled` 恒 false——运行时判定靠它，Core 侧 policy 只接收 bool，不依赖 Velopack。
4. **vpk 会自动验证 `VelopackApp.Run()` 在 Main 里**（本机实跑日志：`Verified VelopackApp.Run() in Prtsc.App.Program::Main`）——位置不对打包会 fail 或告警。
5. `dotnet publish` win TFM 在 mac/linux 交叉编译无障碍（仓库 `EnableWindowsTargeting=true` 已开）。
6. Velopack 安装器默认装快捷方式（Desktop + StartMenuRoot）与卸载项，无需额外参数；**自启不写安装器**——`RegistryAutostartService` 由用户在设置里开（决定：安装器不碰 Run 注册表，卸载不残留自启键）。
7. 发布产物未签名：SmartScreen 会拦。签名方案（EV 证书 / Azure Trusted Signing）未定，子任务外。

## 诚实清单（未真机/未验证项）

- **干净 Windows VM 安装冒烟**：Setup.exe 双击安装 → 截图/标注/导出全流程 → 卸载残留盘点。本机 macOS 无法执行；CI release job 已具备产出能力，待首次 tag 推送或 Windows 机手跑 `scripts/pack-windows.sh`。
- **更新端到端**：装旧版 → 发新版（Release 传 feed 文件）→ 应用内提示 → 下载 → `ApplyUpdatesAndRestart` 重启换新。逻辑各环节均有单测/日志，但整链未真机验证。
- **CI 全绿**：workflow YAML 已写 + 本地语法校验，但 GitHub Actions 首跑未发生（仓库尚无远端 Actions 历史）。风险点：runner 上 `dotnet-version: '10.0.x'` 的可用性、Windows 宿主 bash（Git Bash）跑 pack 脚本。
- **应用内 FeedUrl 输入框 / 更新提示框的真人 UX**：仅 headless 结构测试覆盖，未真人点过。
- **Portable.zip 行为**：便携包内 `UpdateManager.IsInstalled` 应为 false（无 Update.exe）→ 不检查更新——语义推定正确，未实测。
- 验收标准降级条款：R-1「VM 安装冒烟」「更新在打包版内可用（端到端）」允许降级并已在 issue 卡标注。

## Review pass（2026-11-10 独立审查后跟进）

独立审查通过（无 Critical），1 Important + 9 Minor 已随本批清掉（commit `fix(app): R-1+C-1 review pass`）：

- **I-1**：FeedUrl hint 文案误导——反编译确认 Velopack 1.2.161 **无任何 GitHub 仓库 URL 识别**（只认 SimpleWebSource：`{base}/releases.win.json`），填裸 releases 页会 404 且更新静默失效。en/zh hint 改为给出正确形态示例 `https://github.com/cholf5/prtsc/releases/latest/download/`（CI 每版上传同名 `releases.win.json`，该 URL 恒指向最新版，与实现匹配）。
- **M2**：空 FeedUrl 每次启动走异常记 Warning → `RunOnStartupAsync` 开头 `IsNullOrWhiteSpace` 静默 return（默认配置不再产生 `UpdateManager init failed` 噪音日志），新增 3 例守卫测试钉住。
- **M3**：`manager.IsInstalled` 读取挪进 try-catch，与 `App.axaml.cs` 启动注释口径对齐。
- **M4**：pack 脚本空数组 + `set -u` 在 macOS bash 3.2 炸 → `${ICON_ARGS[@]+...}`（本机 bash 3.2 实测空/非空两态）。
- **M5**：vpk fallback 识别 `vpk.exe`（Windows 宿主）。
- **M6**：publish 前 `rm -rf "$PACK_DIR"`，陈旧文件不混包。
- **M7**：删 `UpdatePromptWindow` 公共无参构造（连带消灭一组 `null!` 字段的可达 NRE 面）。
- **M8**：`UpdatePromptViewModel.Choice` 注释改实（副作用由窗口事件处理，属性当前仅测试读取）。
- **M9**：ci.yml 顶层 `permissions: contents: read`（release job 仍 job 级 `contents: write`）。
- **M10**：文档计数更正（policy 21 例 / config 4 例 / Loc key 12 个）+ 坑 3 措辞（`new UpdateManager` 抛不抛取决于 URL 形态而非是否打包）。

## 下一步

1. 推远端后让 CI 首跑（矩阵 + 语法落地）；打 `v0.1.0` tag 验 release job（注意：Directory.Build.props `<Version>0.1.0</Version>` 必须先改成发布版本号，断言 tag == Version）。
2. Windows 真机/VM：安装冒烟 + 卸载残留 + 端到端更新（可用本地目录 feed：`UpdateManager` 支持本地路径，两个不同版本的 pack 产物即可模拟）。
3. 签名方案拍板（SmartScreen 用户体验）。
4. mac 分发渠道（vpk osx 腿 / .app bundle / Homebrew）——此前 handoff 已注明未排期。
5. `docs/roadmap.md` M7/M10 行可顺手勾（PM 维护 board.md，本批未动）。
