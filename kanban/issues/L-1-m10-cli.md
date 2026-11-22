# L-1 M10 CLI

- **优先级**: P2 ｜ **复杂度**: M ｜ **开发者**: cholf5 ｜ **状态**: Done（2026-11-10）
- **依赖**: D-1（--delay 参数语义对齐）
- **关联**: flameshot CLI（`flameshot gui|full|screen|region -d -p -c`）；handoff 既定方向「headless 导出直接复用 `ImageExporter`」

## 目标

flameshot 式命令行：脚本化/第三方启动器调用截图。

## 子任务

- [x] 1. 参数解析：`novashot gui|full|region|window --delay <s> --path <dir> --clipboard --format png|jpeg|webp`（+`--geometry X,Y,W,H`、`-h/--help`、短旗标 `-d/-p/-c/-f`；Core 纯函数 `CliArgs.Parse`，非法输入带原因失败不回退）。
- [x] 2. 单实例模型：命名管道（`System.IO.Pipes`，名含每用户隔离段）转发命令后退出（无实例则自带 headless 模式）；plain `gui` 也单实例（有实例 → 转发 activate 后退出，对齐 winshot）。
- [x] 3. headless 路径：抓帧（`ICaptureService`，平台层复用）→ `ImageExporter` 编码落盘/剪贴板，不开 GUI（Core `CliCaptureExecutor`，CLI 进程与 GUI 接收方共用）。
- [x] 4. D-1 预留的 `--delay` 实装（CLI 语境 = 简单延时 `Task.Delay` + Ctrl+C 取消，0-60s 钳制；**不复用** D-1 倒计时状态机——它带 UI 事件语义，边界写进 handoff）。

## 验收标准

- [x] `novashot full --clipboard` 在无 GUI 交互下 2s 内完成（本机实测 1.25s，剪贴板有图）；`--path` 落盘文件名走 `FilenamePatterns`（timestamp/increment 实测）。

## 测试用例

- [x] 参数解析纯函数单测（合法/非法/钳制/geometry/默认导出目标/help）——`CliArgsTests` 35 用例。
- [x] 管道转发协议单测（行式 JSON 帧 round-trip + 异常输入容错）——`CliPipeProtocolTests` 26 用例；真管道端到端（客户端×服务端×解析失败回 usage error×坏帧）——`CliPipeTests` 11 用例。
- [x] 执行器全分支（full/region/--path 命名跟 config 模式/--clipboard 快照/format 映射/目录自建/延时/取消/捕获失败/落盘失败/交互命令兜底拒绝）——`CliCaptureExecutorTests` 19 用例。
- [x] GUI 接收分发 + 宿主装配（headless 不炸）——`CliRequestRouterTests` + `CliPipeHostAssemblyTests`（App.Tests 9 用例）。
- [x] 真机 macOS 实弹（2026-11-10，见 handoff §5）：headless 全链路 + 转发路径 + gui/window 转发全通；Linux/Windows 留 QA。

## Verification（2026-11-10）

- 模式语义（PM 裁决）：`gui`/无参数 = 现行 GUI（有实例转发 activate）；`full` headless 抓全虚拟桌面导出；`region` headless 需 `--geometry`（缺失 → stderr + 退出码 2；有实例转发触发 overlay 选区）；`window` 需交互选窗（有实例转发触发选窗流程，无实例报错退出不启动 GUI）。转发来的 headless 命令在 GUI 进程内执行同一执行器（无 GUI 闪现）；`activate` → 主窗口弹前台。退出码：0 成功 / 1 失败 / 2 用法错误。
- 测试 **1170/1170 全绿**（Core 705 + App 168×2TFM + Mac 83 + Linux 46，基线 1038 → +132）；`dotnet build Novashot.slnx` 0 Error（windows TFM mac 交叉编译通过）。
- **Review pass（同日独立审查，`fix(app): L-1 review pass`）**：I-1 无实例探测拖慢 GUI 冷启动 ≈0.5s → `CheapProbe` 廉价闸门（无实例 0 等待）+ gui 模式 100ms 短超时，实弹 gui 冷启动到首条日志 0.13s；I-2 `MacCaptureService._rawIds` 共享可变状态 → 快照语义（共享字段删除，hammer 测试 + 结构性审查兜底）；顺手 M-1（`-p -c` 吞旗标）/M-2（协议 v:1 校验）/M-6（二次 Ctrl+C 放行终止）/M-7（GUI 启动日志 Configure 去重）。M-3/M-4/M-5/M-8 由 PM 记录。详见 handoff §7。
- 实弹（本机 macOS 26.5.2 M2）：headless `full --clipboard` **1.25s**（review pass 后复验 1.23s）exit 0、剪贴板有图（TIFF+PNGf）；`--path`+`--format webp/jpeg` 文件名/扩展名/尺寸全对；`region --geometry 100,50,800,600` 出图恰 800x600；GUI 实例存活时 `full --clipboard`/`full --path`/`gui`/`window` 转发全通（GUI 日志「CLI forwarded command executed」佐证执行发生在 GUI 进程）；`--help` exit 0；错误路径 2/1/2/1。**坑：显示器休眠时 CGGetActiveDisplayList 返回 0**（首跑误判为权限问题，caffeinate -u 唤醒后全通）。
- 已知边界：CLI 输出全 en（不经 Loc 表）；无 --path 无 --clipboard 默认 --clipboard（flameshot 对齐）；--geometry 只配 region；双实例并发（开发调试场景）管道归属有竞态，记 handoff 已知影响。
