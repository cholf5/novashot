# Kanban Board — Prtsc

> 建板 2026-11-01（M6d 完成后）。M1–M6d 历史见 Done 列汇总卡与 `docs/handoff/`。
> 卡片约定沿用 cholf5 看板工作流：目标 / 子任务 / 开发者 / 复杂度(S/M/L) / 验收标准 / 测试用例 / 关联文档 / 依赖，明细在 `issues/<卡号>.md`。
> 里程碑总表与缺口来源见 `docs/roadmap.md`。

## Backlog
- B-1 粘贴支持 PNG 剪贴板格式（RegisterClipboardFormat("PNG")，浏览器/Electron 互操作）（P2）
- B-2 WindowPicker 96×54 缩略图（P3）
- B-3 跨屏选框（多显示器 overlay 覆盖虚拟桌面）（P2）
- B-4 crop 后 reshape 手柄（复用 BoxHandles）（P3）
- B-6 工具栏按钮自定义（flameshot 式可见工具配置）（P3）
- B-7 Library 删除记录时可选连磁盘文件一起删（现仅删 library.json 记录、文件保留——L-2 审计确认的行为裁决， safer 默认；需求来源：删除确认对话框文案语义修正）（P3）

## To Do
- E-1 M9 屏幕取色器（放大镜 + HEX/RGB 浮签 + 点击复制，可设为描边色；flameshot 招牌交互）（P2）
- N-1 M9 桌面通知（托盘气球通知，导出/QuickSave/上传/复制触发，ShowNotification 字段已备）（P2）
- I-1 M9 Ctrl+O 导入图片文件（复用粘贴路径；注意 CapturedImage 所有权坑）（P2）
- L-1 M10 CLI（prtsc full|region|window --delay --path --clipboard --format；单实例命名管道转发）（P2）

## In Progress


## In Review
- 

## Testing / QA
- QA-1 真机人工冒烟（**UI-1 改版后清单要重写**：dock 15 工具点选/hover/热键 + 面板 tab + 色块选中环 + 滑杆 + ⋯ 菜单 + ToolTip + 最近 4 张点击回编辑器 + zh/en 切换全窗口 + 托盘语言菜单；再叠加原 M4–M6d 项：选择/撤销、裁剪(X/比例/A 取色)、Copy/Ctrl+V/Ctrl+Q、Word/微信/Chrome 剪贴板互通、三种捕获、托盘、WindowPicker）（P0）
- QA-L1 Linux 真机冒烟（X11 三捕获/热键/xclip 两环境/自启/多屏负坐标/KDE+GNOME；清单见 handoff 261101-linux1 §6）（P1）

## Blocked
- 

## Done
- R-1 M10 Velopack 打包 + C-1 Updater（合并执行，C-1 整卡完成）：`VelopackApp.Build().Run()` 进 Main 首行；`scripts/pack-windows.sh`（publish self-contained win-x64 → `vpk [win] pack`，**[win] directive 钉死**防误走宿主打包器；bash 3.2 空数组两态实测）；macOS 交叉打包实跑成功（Setup.exe + Portable.zip + full.nupkg + releases.win.json 落 artifacts/pack/，不入 git）；`assets/app.ico` 脚本化生成；CI `.github/workflows/ci.yml`（三 OS 测试矩阵 + tag release 附 sha256）；Updater：`UpdateConfig.FeedUrl` + 设置页输入框（hint 给 `releases/latest/download/` 正确形态——Velopack 无 GitHub URL 识别，审查者反编译证实）+ `UpdateCheckPolicy` 纯函数（21 例）+ 启动 fire-and-forget 检查（未打包静默、全程吞异常不崩启动）+ 非阻塞更新提示框（安装/跳过此版本/以后再说）；独立 review 1 Important（hint 陷阱）+ 9 Minor 全处置（`bdc7a3b`）；测试 880/880（Core 527 + App 114×2TFM + Mac 80 + Linux 45）；未真机项（VM 安装/更新端到端/CI 首跑）入 handoff 261110（2026-11-10）
- L-2 M10 i18n 收尾：范围裁决——resx/机制子任务被 UI-1 的 Loc.cs 双语表吸收（卡允许二选一），语言切换进设置页由 S-1 交付，本卡落地剩余两件：① key 一致性单测（En/Zh 集合双向相等 + XAML/代码 274 处引用 ⊆ 定义 + 占位符 `{N}` 多重集相等 + 动态拼 key 显式在册断言，mutation 三向实证有效）；② 硬编码文案审计（17 新 key 抽串、8 处落位、不抽类别口径成文）；回退链（zh 缺→en→key 本身）钉死；顺手修正 Library 删除确认文案与实际行为（只删记录不删文件，文件增强登记 backlog B-7）；独立 review 1 Important（占位符断言缺失）+ 3 Minor 全处置（`f0d2ab5`）；测试 837/837（Core 502 + App 105×2TFM + Mac 80 + Linux 45，PM 实测口径）（2026-11-10）
- D-1+G-26 延时截图 + Capture Launcher：Core `DelayedCaptureCountdown`（单 Monitor 锁全覆盖状态迁移）+ `CountdownOrchestrator`（pending 绑定轮次号，settle 裁决下沉 Core）+ `CaptureDelayOptions.Resolve`（延时解析纯函数）；托盘「延时截图」3s/5s 子项 + 设置页默认延时（0/3/5 钳制）+ 倒计时角标窗（WorkArea 定位，关窗后 250ms 才抓帧）；取消语义=热键取消本轮/再触发取消旧轮按新意图；G-26 启动器窗口（全屏/区域单选 + X/Y/W/H 越界钳制 + 延时 + 开始截图，走同一咽喉点）；独立 review 报 1 Critical（250ms 间隙竞态→角标入快照）+1 Important（线程池回调违约）全修复（`42100d7`，C-1/I-1 均有「去实现即挂」回归用例）；测试 727/727（Core 502 + App 100×2TFM + Mac 80 + Linux 45）；快照 `artifacts/ui/40-launcher.png`；真机待验项入 handoff 261110 §3 与 QA-1（2026-11-10）
- S-1 M8 设置中心 GUI：六分区设置窗口（常规/热键/QuickSave/导出/编辑器默认值/捕获），整窗草稿 + 保存/取消语义；语言入口从托盘搬进设置（托盘语言子菜单删除）；热键录制控件（KeyCombo→HotkeyParser spec 串，Esc/纯修饰键/NumPad 拒绝）+ 保存一刻原子发生（自启切换→config Update→热键重注册→语言全局刷新）；新 Core 字段 Capture.IncludeCursor（WGC 光标开关）；独立 reviewer 报 1 Critical + 4 Important 全修复（`19bfde1`：Windows 泵线程权威 `_registeredIds` 防重注册竞态、Linux 补 XUngrabKey、重复热键提示、Loc.ApplyStoredPreference、IncludeCursor 复制）；测试 635+91 全绿；未真机项入 `docs/qa/261102-smoke-checklist.md`（⚑ 段 I/J）；handoff `261102-s1-settings-handoff.md`（2026-11-02）
- U-1 透明背景预览可见性：`TransparencyChecker`（Core，瓦片 shader 棋盘格）+ `SkiaCanvas` 在 `Background=None` 时于内容区铺暗色占位格（编辑器 chrome，不进导出）+ 面板 None 项 ToolTip；先 RED 后 GREEN，Core 新增 6 例单测，快照 `artifacts/ui/05-editor-transparent-bg.png` 人工核对；全量 Core 438 + App 97×2TFM + Mac 80 + Linux 45；存量 flake 与本卡无关：SettingsWindowXamlTests（并行）/ MacClipboardServiceTests.ManualRoundtrip（并行）（2026-11-03）
- E-2 画布参数持久化：Core `EditorConfigMapper` 双向映射（FromConfig 出初始态 / ApplyTo 回写，外观字段 round-trip 有测试钉死）+ 画布面板 `Apply` 咽喉点 `CanvasStateChanged` 回调 + 主 VM UI 线程同步回写内存、500ms 防抖写盘、ReturnToCapture flush；「编辑器默认值」语义升级为「上次使用的画布参数」。顺带修三件：showBackground 死字段激活（None↔false 双向）、GradientParser.ParseColor 8 位 CSS #rrggbbaa 通道纠正（SKColor.Parse 原生按 #aarrggbb 读）与 ArgumentException 捕获（"#gg0000" 不再炸）、ToCssColor 小写对齐 config 风格；测试 651/651（Core 432 + App 94×2TFM + Mac 80 + Linux 45）；已知存量：SettingsWindowXamlTests 全解决方案并行下偶发 flake（stash 基线复现，与本卡无关）（2026-11-03）
- UI-1 视觉重设计 + i18n：Theme 层（Tokens/Icons/ControlTheme）替掉默认 Fluent，IBM Plex 随包；编辑器 dock（修工具栏溢出）+ Style/Canvas 分页；主窗口命令列表 + 最近 4 张；Library/云设置/窗口选择器/确认环重绘；Loc.cs 双语文案表 + 托盘切语言（config.language）；headless 视觉快照（PRTSC_SNAPSHOT=1，artifacts/ui/）；测试 666/666（Core 414 + Mac 80 + Linux 44 + App 64×2TFM）；明细见 handoff 261102 §3/§4（2026-11-02）
- LINUX-1 Linux 平台层（X11）：5 commit（interop/抓屏 → 窗口/热键 → 剪贴板/自启 → 装配/构建修复）；XGetImage 根窗抓屏 + XComposite 抓窗、XRandR 显示器（DPI 毫米推断吸附）、EWMH 窗口枚举、XGrabKey 热键（NumLock/CapsLock 四变体）、xclip 代持剪贴板、XDG Autostart；AppServices 运行时按 OS 分流（mac/Linux 共用 net10.0 TFM）；Wayland 原生留 LINUX-2；测试 596/596（Core 414 + Mac 76 + App 63×2 + Linux 44）；修 Directory.Build.props 分组事故（EnableWindowsTargeting 曾只对 Linux 生效）（2026-11-01）
- GAP-1 参考项目差距全量搬运第一批（两参考源码重扫 flameshot 76 项/winshot 66 项）：反色工具(I)、序号最小未用回收(winshot)、Ctrl+滚轮改序号(flameshot)、角度吸附(Ctrl=八方向/对角， Shift=正交)、Esc 取消拖拽、Backspace 删除选中、QuickSave 自动复制路径、CF_HDROP 文件粘贴(winshot)、CloseToTray/MinimizeToTray/窗口尺寸持久化接线、EnterEditor 应用编辑器默认值、IncludeBackground 接线(EditorStateStripping)、Library 删除确认+Del 键+复制路径、托盘 tooltip 版本号、云连接测试(R2 HeadBucket/GDrive token)；测试 553/553（Core 414 + Mac 76 + App 63×2TFM）；明细 .zcode/plans/261101-references-gap-sweep.md（2026-11-01）
- MAC-1 macOS 平台层：App 双 TFM（windows/net10.0）+ Prtsc.Platform.Mac（CoreGraphics 抓屏含跨屏 region、CGWindowList 窗口枚举/捕获、Carbon 全局热键无需辅助功能权限、NSPasteboard 剪贴板 TIFF/PNG 双向、launchd LaunchAgent 自启）+ 接口上提 Core（IHotkeyService/IAutostartService）+ mac 主线程 sync-over-async 死锁修复；测试 506/506（Core 381 + Mac 72 + App 53×2TFM）；mac 真机启动到托盘+热键注册验证通过；真机冒烟清单见 handoff 261101-mac1 §5（2026-11-01）
- M7b M7 UI 层：编辑器工具栏 Upload/Library/Pin/Cloud… 按钮 + Ctrl+U/L/P 热键 + PinnedWindow（置顶贴图：拖动/滚轮缩放/双击/Esc 关闭）+ LibraryWindow（缩略图列表/复制链接/打开/删除）+ CloudSettingsWindow（渠道/凭据/AutoUpload）+ 导出与上传后自动写 Library+缩略图 + IClipboardService.TrySetText（CF_UNICODETEXT）；测试 427/427（Core 374 + App 53）（2026-11-01）
- M7a M7 数据层：SigV4 签名器 + R2 上传 + GDrive（token/multipart）+ ConfigUploadService 分发 + Cloud 凭据配置 + Library 数据层（library.json/原子写/缩略图）；测试 402/402（Core 360 + App 42）（2026-11-01）
- M1–M6d 里程碑汇总（脚手架/捕获/选框 → 标注工具集 → 手绘与区域效果 → 合成管线/剪贴板/裁剪+边缘取色；测试基线 351/351，详见 issues/done-m1-m6d.md）（2026-11-01）
