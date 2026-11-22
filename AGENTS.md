# AGENTS.md — Novashot

flameshot 式的跨平台截图 / 标注 / 分享工具：**截图 → 就地标注 → 一步合成落盘、进剪贴板或上传**，常驻托盘、全局热键随叫随到。
产品定位是「一个键叫出来、画两笔就走」，不做截图工作台、不做图层系统、不做插件——判断新需求要不要做的第一条就是它有没有缩短这条链路。

平台：Windows（WGC）/ macOS（CoreGraphics）/ Linux（X11，XWayland 下可用）。UI 为 Avalonia 12 + Skia。

## 常用命令

```bash
dotnet build Novashot.slnx                        # 全量构建（警告即错误，见下）
dotnet test tests/Novashot.Core.Tests             # 纯逻辑：几何/合成/导出/配置/Library/CLI 协议
dotnet test tests/Novashot.App.Tests -f net10.0   # headless UI 接线（windows TFM 腿用 -f net10.0-windows10.0.19041.0）
dotnet test tests/Novashot.Mac.Tests              # CoreGraphics / NSPasteboard（部分 Manual 用例只在 mac 上跑）
dotnet test tests/Novashot.Linux.Tests            # Fake X11
NOVASHOT_SNAPSHOT=1 dotnet test tests/Novashot.App.Tests -f net10.0 --filter UiSnapshot   # 视觉快照 → artifacts/ui/
dotnet run --project src/Novashot.App             # 默认 TFM 随宿主平台；-f 显式指定
scripts/pack-windows.sh                        # Velopack Windows 包 → artifacts/pack/
scripts/pack-macos.sh                          # .app bundle → artifacts/pack/
scripts/release.sh 1.0.0 --watch                # 发版（bump → tag → push → 盯 CI 与资产）
```

.NET SDK 10（`net10.0`；App 另有 `net10.0-windows10.0.19041.0` 腿）。开发机可以是 Windows / macOS / Linux 任一。

## 架构边界（禁止反向依赖）

- **Novashot.Core**：纯逻辑 + Skia。标注模型与渲染指令、合成管线（背景/Padding/圆角/阴影/内衬/边框）、导出编解码（PNG/JPEG/WebP）、裁剪与边缘取色、配置模型与原子写、Library、云上传（SigV4 / GDrive）、通知策略、CLI 参数与管道协议/执行器、更新检查策略。**不引用 Avalonia，也不引用任何平台层**；凡是「决定怎么画/怎么存/怎么算」的都下沉到这里，方便纯单测。
- **Novashot.App**：Avalonia UI 与进程编排——主窗口、overlay、编辑器、设置中心、托盘、单实例与 CLI 转发。平台能力只经 `Core` 的接口消费，DI 装配集中在 `AppServices.cs`。
- **Novashot.Platform.Windows / .Mac / .Linux**：各自的抓帧、窗口枚举、全局热键、剪贴板、自启、通知。Windows 层只在 windows TFM 引用；Mac 与 Linux 两层都在 `net10.0` TFM 内，运行时按 `OperatingSystem` 分流（`AppServices.AddNovashotPlatform`），**只实例化命中的那一套**。新增平台能力 = Core 提接口 → 平台层实现 → `AppServices` 注册 → 加测试。

Capture 结果的唯一形态是 `CapturedImage`（SKBitmap + 物理像素矩形 + scale + 来源）；跨屏选框的口径是「DIP 选框 + 按屏切片渲染 + 物理像素拼接」，改这块先读 `SelectionSession`。

## 警告纪律

`Directory.Build.props` 里 `TreatWarningsAsErrors=true`。新增警告按顺序处理：修掉 → 修不掉就在**同一行上方写清理由**再 `NoWarn`（`Novashot.Platform.Linux` 的 `CA1416;CA2014;CS0067` 就是这种带理由的例外：全是「交叉编译时不可达的 Linux P/Invoke 语义告警」），不要整类压掉。

XAML 编译器诊断（`AVLNxxxx`）**不受** `TreatWarningsAsErrors` 管辖——Avalonia.Build.Tasks 走 `IBuildEngine.LogWarningEvent` 原始 API，绕过了升级逻辑。唯一生效通道是仓库根 `.editorconfig` 的 `avalonia_xaml_diagnostic.<code>.severity`（目前 `AVLN5001` = 用了过时/实验性 XAML API，定级 error）。

## i18n 纪律

全部界面文案唯一出处 `src/Novashot.App/Localization/Loc.cs`（`Strings.Zh` / `Strings.En` 两张表，key 相同）。
- XAML 用 `{DynamicResource L.<Key>}`；切语言 = 按 `L.` 前缀重写 `Application.Resources`，DynamicResource 自己刷新，窗口不重建。代码里用 `Loc.T("<Key>")`。
- 回退链：zh 缺项 → en → key 本身（漏翻在界面上一眼可见）。
- `LocKeyConsistencyTests`（L-2）兜底：En/Zh 集合双向相等、XAML 与代码里的引用 ⊆ 定义、占位符 `{N}` 多重集相等。**加 key 必须两表同步加**，否则那条测试会红。
- 动态拼出来的 key 必须登记进该测试的在册清单。

## Headless 测试纪律（`tests/Novashot.App.Tests`）

- 实例化真实 Avalonia 控件的测试一律标 `[Collection("HeadlessUi")]`（`DisableParallelization = true`）：xunit 默认按类并行，多个 `HeadlessUnitTestSession` 并存会争抢渲染循环，表现为偶发 abort。
- 视觉快照走 `SnapshotBootstrap` + `UseHeadless(UseHeadlessDrawing = false)`，只在 `NOVASHOT_SNAPSHOT=1` 时运行——它会 `SetupWithoutStarting` 一个全局 Avalonia 实例，与常规 session 共用进程会互踩，所以默认不参与常规运行。图写到 `artifacts/ui/`（已 gitignore），仅作人工目检，不做像素断言。
- 断言要语言无关：断文案就引用 `Loc` 的属性，不要写死中英文字符串。
- **Q-1 教训（RadioButton 互踢环）**：`GroupName` 相同的多个 `RadioButton` + `IsChecked` **TwoWay** 绑定会构成逻辑环——组管理器取消兄弟 → 回写 VM → 整表通知 → 绑定重新发布 → 再进 `IsCheckedChanged`，测试宿主上表现为栈溢出（15 跑 3 abort）。规矩：**组内 chip 用 OneWay 回显 + `Click` 携带状态**，别让目标侧回写源。
- 另一个高频坑：`Dispose` 里删临时目录可能与 VM 的 500ms 防抖写盘撞文件锁（Windows 上抛 `IOException`）。资产型清理要带退避重试；要给「写完才断言」就用 `FlushConfigSaveSync()`。

## 已知坑（勿"顺手改"）

- **vpk 的 `[win]` directive 必须保留**（`scripts/pack-windows.sh`）。不带它时 vpk 按**宿主** OS 选打包器：在 macOS/Linux 上会误用 osx/linux 打包器，产物名与 `releases.{channel}.json` 全错。channel 同样要显式钉死。
- **Velopack feed 的托管点就是 GitHub Release 页**：`releases.win.json` / `RELEASES` / `*-full.nupkg` 与安装包同批上传，应用内 updater 读 `.../releases/latest/download/`。改动产物清单要同步 `scripts/release.sh --watch` 的核对表与 `docs/release/ci-release.md`。
- **CLI 与 GUI 是同一份实现**：`novashot full` 在无实例时 headless 执行，有实例时整条命令行经命名管道（`CliPipeNames.ForCurrentUser()`）转发给 GUI 进程内的 `CliCaptureExecutor`；`Program.Main` 的 Velopack 钩子必须在最前。别为 CLI 另写一套捕获路径。
- **剪贴板写 PNG 有 4MP 阈值**（`B-1`）：UI 线程同步编码实测 1080p ~72ms / 4K ~278ms / 4K 噪声 ~992ms，超阈值只放 DIBV5+DIB，不写 PNG。要动这个数先重测。
- **`Novashot.Platform.Linux` 的 NoWarn 是有理由的**（见「警告纪律」），并掉之前先确认交叉编译语义。
- 字体 IBM Plex 随包分发（SIL OFL，见 `src/Novashot.App/Assets/Fonts/OFL.txt`），与仓库的 AGPL 是两条许可。

## 看板与提交约定

任务管理用 `kanban/board.md`（Backlog / To Do / In Progress / In Review / Testing / QA / Blocked / Done）+
`kanban/issues/<ID>-<slug>.md`。移动卡片 = 同一次操作里改看板行 + 卡片内 status。里程碑总表在 `docs/roadmap.md`。

提交信息为中文，格式 `type: 一句话`（`fix(app):` / `docs(kanban):` / `feat(core):` …），正文说明「改了什么、为什么、明确不做什么」。

**开卡必须由用户明确指示**。用户没明说「开卡 / 建卡 / 立卡」时直接干活，不要自作主张往看板加行；已有卡片的移动与收口仍按约定执行。

## 发版

流程与资产契约见 [`docs/release/ci-release.md`](docs/release/ci-release.md)，人读的坑见
[`docs/release/known-limitations.md`](docs/release/known-limitations.md)。一句话版本：

```bash
scripts/release.sh 1.0.0 --watch    # 本地测试 → bump Directory.Build.props <Version> → tag v1.0.0 → push → 盯 CI
```

版本号唯一出处是 `Directory.Build.props` 的 `<Version>`：CI 在 tag push 时强制 `tag == <Version>`，
Velopack 包名、Release 标题、应用内更新比较都以它为准。**首版必须有手写 `docs/release/notes/<tag>.md`**——没有上一个 tag 时 `--generate-notes` 会把全部提交历史抖成发版说明。
