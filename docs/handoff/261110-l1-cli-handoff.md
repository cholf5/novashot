# Prtsc 交接备忘 — 2026-11-10 L-1 M10 CLI

> 单卡交付；结构仿 `261110-d1-g26-delayed-capture-handoff.md`。测试基线 1038 → **1160**（Core 696 + App 168×2TFM + Mac 82 + Linux 46）。

## 0. TL;DR

- **L-1 完成**：flameshot 式 CLI——`prtsc gui|full|region|window --delay <s> --path <dir> --clipboard --format png|jpeg|webp --geometry X,Y,W,H`。
- **分流**（Program.Main，Velopack 钩子之后、`BuildAvaloniaApp` 之前）：CLI 捕获命令 → 单实例管道探测（有实例转发整条 argv 后按响应退出）→ 无实例按模式分流（full / region+geometry → headless 执行；region 缺 geometry → usage error；window → 报错退出不启动 GUI）；gui/无参数 → 现行 GUI 路径。**CLI 路径零 Avalonia 运行时**。
- **退出码约定**：0 成功 / 1 一般失败 / 2 用法错误（stderr 报错，stdout 只出结果行）。
- **单实例**：命名管道 `System.IO.Pipes`，服务端 GUI 全程 host（App 启动起、退出停，吞异常）；客户端 500ms 探测，连接成功即转发原始 argv（JSON 数组行式帧）并等执行结果；**plain `gui` 也转发**（接收方映射为 activate：主窗口 Show+Activate，对齐 winshot）。
- **headless 执行器**：Core `CliCaptureExecutor`（注入 ICaptureService/IConfigStore/IClipboardService，零 Avalonia）——抓帧 → `ImageExporter.Encode`（无标注 AnnotationDocument = 原图直出，不加编辑器 padding/背景，对齐 flameshot CLI）→ `--path` 落盘（文件名走 `FilenamePatterns` 既有模式 + config.QuickSave.Pattern，目录自动创建）/ `--clipboard`（`IClipboardService.TrySetImage` 快照）；两者可并存。CLI 进程与 GUI 接收方共用同一执行器。
- **--delay 与 D-1 的复用边界**（PM 裁决）：CLI 语境 = 简单延时（`Task.Delay` + CancellationToken，Ctrl+C 取消），**不复用** `DelayedCaptureCountdown`/`CountdownOrchestrator`——那是 UI 倒计时状态机（角标/取消热键/settle 间隙语义），headless 硬套没有意义；秒数钳制 0-60（与 D-1 的 {0,3,5} 合法值表无关）。转发到 GUI 的 region/window 交互模式则走 `TriggerCaptureAsync` 既有倒计时（CLI 显式 delay 透传，显式值覆盖 config 默认）。
- **CLI 输出 en，不经 Loc 表**（PM 裁决）：help/错误/结果文案都是英文字面量（`CliArgs.HelpText` + stderr），脚本契约不受语言设置影响。
- **日志形态分流**：CLI 进程（含 gui 转发客户端）一律 file-only sink（stdout/stderr 是脚本契约，Serilog 不许往 console 漏行）；真启动 GUI 时 re-configure 回 console+file（`LoggingSetup.Configure` 重复调用安全，替换前 Dispose 旧 logger）。
- 实弹（本机 macOS 26.5.2 / M2）全通，见 §5；测试 1160/1160 全绿；`dotnet build Prtsc.slnx` 0 Error。

## 1. 文件地图（本批增量）

```
src/Prtsc.Core/Cli/CliCommand.cs             解析产物 record（Mode/Delay/Geometry/Path/Clipboard/Format/Help）+ IsHeadlessExport/IsInteractive 语义分流
src/Prtsc.Core/Cli/CliArgs.cs                解析纯函数：模式大小写不敏感、短/长旗标、--delay 0-60 钳制、--geometry 复用 RegionRectSpec.TryParse、
                                             非法组合/未知项带原因失败；--geometry 只配 region；值以 "--" 开头视为缺值；无导出目标默认 clipboard（仅捕获模式）
src/Prtsc.Core/Cli/CliPipeResponse.cs        响应 record + CliExitCodes（0/1/2）
src/Prtsc.Core/Cli/CliPipeProtocol.cs        行式 JSON 帧编解码纯函数（Utf8JsonWriter/JsonDocument 手写，trim 安全；64KiB 上限；嵌裸换行/非法 JSON/缺字段容错）
src/Prtsc.Core/Cli/CliPipeFrame.cs           帧读取（读到 '\n'，超限 IOException，EOF null）
src/Prtsc.Core/Cli/CliPipeNames.cs           管道名：prtsc-cli-<SHA256(用户名)前8hex>（每用户隔离，见 §3-3）
src/Prtsc.Core/Cli/CliPipeClient.cs          500ms ConnectAsync 探测 → 发请求 → 等响应（120s 超时）；连接失败=NoInstance 回退 headless；
                                             连接成功后传输失败不回退（防双份截图）
src/Prtsc.Core/Cli/CliPipeServerHost.cs      accept 循环（每连接一 Task、一次一命令、处理完关闭、maxInstances 4）；解码→CliArgs.Parse（失败回 usage 2）→handler→回写；全程吞异常
src/Prtsc.Core/Cli/CliCaptureExecutor.cs     headless 执行器：延时（可注入调度器委托）→抓帧→导出→CliExportResult（路径/字节数/描述行）；局部失败保留 SavedPath 事实
src/Prtsc.App/Cli/CliRequestRouter.cs        GUI 接收分发：gui→activate；region 缺 geometry/window→TriggerCaptureAsync(Tray)；full/region+geometry→headless（后台线程）；
                                             分发自身不抛（handler 异常转 failure 响应）；三回调由 App 注入（内部各自 marshal，路由器零 Avalonia）
src/Prtsc.App/Cli/CliApplication.cs          CLI 进程编排：help→usage→转发→无实例分流→RunHeadless（AppServices.Initialize + DI 执行器 + Ctrl+C 令牌 + AppServices.Shutdown）
src/Prtsc.App/Program.cs                     分流：parse（纯函数）→ Configure(file-only) → CliApplication.Run → LaunchGui 则 re-Configure(console) 走现行路径
src/Prtsc.App/App.axaml.cs                   管道服务端宿主（StartCliPipeHost 吞异常；desktop.Exit Dispose）+ BuildCliPipeRouter（executeHeadless 走 DI + CliCaptureExecutor）
src/Prtsc.App/Logging/LoggingSetup.cs        Configure(configDir, consoleSink) 参数化 + 旧 logger Dispose
tests/Prtsc.Core.Tests/Cli/CliArgsTests.cs             35：合法/非法/钳制/geometry/默认目标/help/组合/重复项
tests/Prtsc.Core.Tests/Cli/CliPipeProtocolTests.cs     26：round-trip（空参/空格引号中文/值内换行转义）+ 容错（坏 JSON/非数组/非字符串项/嵌换行/超限/缺字段）
tests/Prtsc.Core.Tests/Cli/CliCaptureExecutorTests.cs  19：全分支 Fake（含 Cancelled、局部失败保留 SavedPath、increment 模式跟 config）
tests/Prtsc.Core.Tests/Cli/CliPipeTests.cs             11：真管道端到端 + 无实例回退 + 坏帧回 usage + Start/Dispose 幂等
tests/Prtsc.App.Tests/CliHostTests.cs                  9：CliRequestRouter 分发映射（无 Avalonia）+ 宿主×路由装配端到端（headless 不炸）
kanban/issues/L-1-m10-cli.md                           Done + Verification
```

## 2. 裁决落实（PM 简报逐条）

- 模式语义四条照落（见 issue Verification）。region 缺 geometry 在**解析层合法**（记录 Geometry=null），headless/交互的分流在宿主层做——同一份解析产物同时服务 CLI 进程与 GUI 接收方，不掺模式判断。
- 转发「整条原始命令行」：argv 以 JSON 数组进帧，接收方用 `CliArgs.Parse` 重解析——含空格/引号的 `--path` 无损。
- 转发后的 headless 命令在 GUI 进程后台线程执行同一 `CliCaptureExecutor`（实弹验证无 GUI 闪现，GUI 日志有「CLI forwarded command executed」）。
- 无 --path 无 --clipboard 默认 --clipboard（仅捕获模式；gui/无参数不适用，避免 gui 记录带无意义 flag）。
- 客户端等转发结果再退出（退出码反映 GUI 内执行结果）；交互模式（region 选区/window 选窗）立即回「已启动」exit 0，不阻塞用户交互。

## 3. 关键坑（新会话必读）

1. **Unix 管道名长度上限**（本批首次实弹踩出）：dotnet 在 Unix 把命名管道落成 `GetTempPath() + "CoreFxPipe_" + name` 的 domain socket，**路径上限 104 字符**。macOS 临时目录 ~49 + 前缀 11 → 名字最长 ~44；测试曾用 47 字符的 GUID 名直接 `ArgumentOutOfRangeException`。生产名 `prtsc-cli-+8hex=18` 总长 78，安全；测试用 17 字符短随机名。Linux 用户自定义超长 TMPDIR 时会退化（服务端建流失败被吞 → CLI 永远走 headless，功能不炸但转发失效）——已接受，QA 注意。
2. **客户端「连接成功后」绝不回退 headless**：连接建立后写/读失败时命令可能已被服务端受理，本地重跑会出双份截图——按传输失败 exit 1 上报。回退只发生在 ConnectAsync 失败（= 无实例）。
3. **每用户隔离**：Windows 默认命名空间机器级（快速用户切换撞名）、Linux /tmp 共享 → 名字带 `SHA256(用户名)` 前 8 hex；macOS $TMPDIR 本就按用户隔离，tag 冗余无害。两平台同一派生函数，客户端/服务端天然一致。
4. **`prtsc gui` 的 stdout 污染**：console sink 的开合不能按「parse 出的 mode」决定——`gui` 既可能是 LaunchGui 也可能是转发客户端。最终方案：CLI 进程一律 file-only，`LaunchGui` 返回后再 re-configure console（Configure 重复调用安全）。首次实弹抓到（stdout 混入 Serilog 行），已修并有回归验证。
5. **显示器休眠 ≠ 权限问题**：实弹首跑 `CGGetActiveDisplayList` 返回 0、CVDisplayLink 报 -6661（kCVReturnInvalidDisplay），差点误判成「屏幕录制权限未授予」。caffeinate -u 唤醒后全通。以后 mac 上跑 GUI/抓帧类验证，先确认显示器 awake。GUI 从 agent 后台上下文直接 `nohup` 启动在休眠态也会炸 AvaloniaNative RenderTimer（同源）。
6. **`--delay` 值含负号**：`TryReadValue` 只拒绝以 `--` 开头的值（下一个选项），`--delay -3` 是合法值（钳到 0）——负数值不能一刀切拒绝。
7. **GUI 接收交互模式的 delay 语义**：CLI 显式 delay 直接透传 `TriggerCaptureAsync(delaySeconds:)`——`CaptureDelayOptions.Resolve` 对显式值不钳 {0,3,5}（`Math.Max(0, explicit ?? Clamp(configured))`），所以 CLI `--delay 7` 在 GUI 倒计时里就是 7s。这是有意的（CLI 是显式意图）；如未来要统一合法值表，改 Resolve 即可。
8. **`AnnotationDocument` 不实现 IDisposable**（Source 非拥有）：执行器里不能 `using`——位图生命周期归 `CapturedImage`（finally Dispose），文档本身即用即弃。

## 4. 已知影响 / 边界

- **双实例**（开发调试场景）：两份 GUI 都起管道服务端同名——Windows 多实例命名管道连接归属不定；Unix 后 bind 者覆盖 socket。单实例激活语义下用户不该跑到双实例；后果限于转发到「其中之一」，记 QA 已知项。
- **Windows/Linux 腿**：编译过（slnx 0 Error）、单测全绿，真机未验证（管道名长上限在自定义 TMPDIR 的退化、Win32 命名管道 ACL、Linux 剪贴板服务在 CLI 进程内是否可用——留 QA-1）。
- CLI headless 导出**不加编辑器合成**（padding/背景/圆角）——flameshot 语义；要带样式的导出走 GUI。
- 转发等待上限 120s（delay 最大 60 + 抓帧导出余量）；GUI 内执行超时客户端 exit 1（执行可能仍在后台完成，结果不回传）。
- CLI 进程与 GUI 共写同一日志文件（rolling daily）——多进程写有交错，dev 日志仅观测用。

## 5. 实弹记录（本机 macOS 26.5.2 / Apple M2，2026-11-10）

前置：`dotnet build` Debug；显示器需 awake（见 §3-5）。

| 命令 | 场景 | 结果 |
|---|---|---|
| `prtsc --help` | 无实例 | exit 0，usage 入 stdout，stderr 空 |
| `prtsc full --clipboard` | **无实例 headless** | **exit 0，1.25s**（含 ~0.5s 管道探测），stdout `Copied to clipboard (1920x1080)`；`clipboard info` 含 TIFF 8.3MB + «class PNGf» 3.0MB |
| `prtsc full --path /tmp/prtsc-shots --format webp` | 无实例 | exit 0，`prtsc_2026-11-10_05-32-03.webp`（FilenamePatterns timestamp），485KB，实际 WebP VP8 1920x1080 |
| `prtsc region --geometry 100,50,800,600 --path …` | 无实例 | exit 0，PNG 恰 800x600 |
| `prtsc full --path … --clipboard --format jpeg` | 无实例 | exit 0，`.jpg` 落盘 + 剪贴板双目标 |
| `prtsc full --clipboard` / `full --path …` | **GUI 实例存活 → 转发** | exit 0，结果行回 stdout；GUI 日志两处 `CLI forwarded command executed`（执行在 GUI 进程、无 GUI 闪现） |
| `prtsc gui` | GUI 实例存活 | exit 0，stdout 仅 `Prtsc window activated.`（console sink 修复后） |
| `prtsc window` | GUI 实例存活 | exit 0，`Window capture started…`，GUI 弹选窗 dialog |
| `prtsc region`（无 geometry） | 无实例 | exit 2，stderr 报 geometry 要求 |
| `prtsc window` | 无实例 | exit 1，stderr 提示先起 GUI |
| `prtsc full --format gif` | 无实例 | exit 2，`Expected png \| jpeg \| webp` |
| `prtsc full --clipboard`（显示器休眠时） | 无实例 | exit 1，stderr 带权限指引文案（设计内失败 UX，非 bug） |

观测：`--delay 60` 上限钳制、`--delay 0-60` 之外值钳制由单测覆盖（真实 60s 等待未实弹耗等）。

## 6. 下一步

- QA-1 真机冒烟补 L-1 段：Linux（管道名长上限/剪贴板）、Windows（Win32 管道 ACL、CLI 进程内 Win32 剪贴板、WGC headless 抓帧）、macOS 权限拒绝文案。
- 可选后续（未排期）：`--screen <n>` 单屏导出、`--clipboard-only` 输出静音模式、CLI 输出 JSON `--out json`。
- kanban/board.md 移动由 PM 负责（本批未动）。
