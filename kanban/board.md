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
- G-26 Capture Launcher 独立窗口（X/Y/W/H + 延时 + 模式单选；与 D-1 合并做）（P3）

## To Do
- C-1 M7 云上传/Library/Updater/Pin（R2=S3 兼容 PUT + GDrive multipart + Library(library.json+缩略图) + Updater + Pin 贴图；凭据键 Prtsc_R2_*/Prtsc_GDrive_*，不迁移 WinShot 凭据）（P1）
- S-1 M8 设置中心 GUI（常规/启动/热键录制/QuickSave/导出质量滑杆/编辑器默认值/WGC 光标开关/**语言切换（先只在托盘菜单，需搬进来）**；保存后热键重注册；吸收 backlog 导出滑杆与光标开关两项）（P1）
- D-1 M9 延时截图（托盘菜单 3s/5s + Capture.DelaySeconds；overlay 不得先于快照出现；winshot/flameshot 均有而 prtsc 缺）（P1）
- E-1 M9 屏幕取色器（放大镜 + HEX/RGB 浮签 + 点击复制，可设为描边色；flameshot 招牌交互）（P2）
- N-1 M9 桌面通知（托盘气球通知，导出/QuickSave/上传/复制触发，ShowNotification 字段已备）（P2）
- I-1 M9 Ctrl+O 导入图片文件（复用粘贴路径；注意 CapturedImage 所有权坑）（P2）
- L-1 M10 CLI（prtsc full|region|window --delay --path --clipboard --format；单实例命名管道转发）（P2）
- L-2 M10 i18n（resx 抽串 + zh-CN 首发，key 一致性单测）（P2）
- R-1 M10 Velopack 打包（安装器 + 便携版；与 C-1 Updater 子任务协调采用 vpk UpdateManager）（P1）

## In Progress
- C-1 M7 云上传/Library/Updater/Pin —— M7b UI 完成（2026-11-01）；剩 Updater（等 R-1 Velopack）

## In Review
- 

## Testing / QA
- QA-1 真机人工冒烟（**UI-1 改版后清单要重写**：dock 15 工具点选/hover/热键 + 面板 tab + 色块选中环 + 滑杆 + ⋯ 菜单 + ToolTip + 最近 4 张点击回编辑器 + zh/en 切换全窗口 + 托盘语言菜单；再叠加原 M4–M6d 项：选择/撤销、裁剪(X/比例/A 取色)、Copy/Ctrl+V/Ctrl+Q、Word/微信/Chrome 剪贴板互通、三种捕获、托盘、WindowPicker）（P0）
- QA-L1 Linux 真机冒烟（X11 三捕获/热键/xclip 两环境/自启/多屏负坐标/KDE+GNOME；清单见 handoff 261101-linux1 §6）（P1）

## Blocked
- 

## Done
- UI-1 视觉重设计 + i18n：Theme 层（Tokens/Icons/ControlTheme）替掉默认 Fluent，IBM Plex 随包；编辑器 dock（修工具栏溢出）+ Style/Canvas 分页；主窗口命令列表 + 最近 4 张；Library/云设置/窗口选择器/确认环重绘；Loc.cs 双语文案表 + 托盘切语言（config.language）；headless 视觉快照（PRTSC_SNAPSHOT=1，artifacts/ui/）；测试 666/666（Core 414 + Mac 80 + Linux 44 + App 64×2TFM）；明细见 handoff 261102 §3/§4（2026-11-02）
- LINUX-1 Linux 平台层（X11）：5 commit（interop/抓屏 → 窗口/热键 → 剪贴板/自启 → 装配/构建修复）；XGetImage 根窗抓屏 + XComposite 抓窗、XRandR 显示器（DPI 毫米推断吸附）、EWMH 窗口枚举、XGrabKey 热键（NumLock/CapsLock 四变体）、xclip 代持剪贴板、XDG Autostart；AppServices 运行时按 OS 分流（mac/Linux 共用 net10.0 TFM）；Wayland 原生留 LINUX-2；测试 596/596（Core 414 + Mac 76 + App 63×2 + Linux 44）；修 Directory.Build.props 分组事故（EnableWindowsTargeting 曾只对 Linux 生效）（2026-11-01）
- GAP-1 参考项目差距全量搬运第一批（两参考源码重扫 flameshot 76 项/winshot 66 项）：反色工具(I)、序号最小未用回收(winshot)、Ctrl+滚轮改序号(flameshot)、角度吸附(Ctrl=八方向/对角， Shift=正交)、Esc 取消拖拽、Backspace 删除选中、QuickSave 自动复制路径、CF_HDROP 文件粘贴(winshot)、CloseToTray/MinimizeToTray/窗口尺寸持久化接线、EnterEditor 应用编辑器默认值、IncludeBackground 接线(EditorStateStripping)、Library 删除确认+Del 键+复制路径、托盘 tooltip 版本号、云连接测试(R2 HeadBucket/GDrive token)；测试 553/553（Core 414 + Mac 76 + App 63×2TFM）；明细 .zcode/plans/261101-references-gap-sweep.md（2026-11-01）
- MAC-1 macOS 平台层：App 双 TFM（windows/net10.0）+ Prtsc.Platform.Mac（CoreGraphics 抓屏含跨屏 region、CGWindowList 窗口枚举/捕获、Carbon 全局热键无需辅助功能权限、NSPasteboard 剪贴板 TIFF/PNG 双向、launchd LaunchAgent 自启）+ 接口上提 Core（IHotkeyService/IAutostartService）+ mac 主线程 sync-over-async 死锁修复；测试 506/506（Core 381 + Mac 72 + App 53×2TFM）；mac 真机启动到托盘+热键注册验证通过；真机冒烟清单见 handoff 261101-mac1 §5（2026-11-01）
- M7b M7 UI 层：编辑器工具栏 Upload/Library/Pin/Cloud… 按钮 + Ctrl+U/L/P 热键 + PinnedWindow（置顶贴图：拖动/滚轮缩放/双击/Esc 关闭）+ LibraryWindow（缩略图列表/复制链接/打开/删除）+ CloudSettingsWindow（渠道/凭据/AutoUpload）+ 导出与上传后自动写 Library+缩略图 + IClipboardService.TrySetText（CF_UNICODETEXT）；测试 427/427（Core 374 + App 53）（2026-11-01）
- M7a M7 数据层：SigV4 签名器 + R2 上传 + GDrive（token/multipart）+ ConfigUploadService 分发 + Cloud 凭据配置 + Library 数据层（library.json/原子写/缩略图）；测试 402/402（Core 360 + App 42）（2026-11-01）
- M1–M6d 里程碑汇总（脚手架/捕获/选框 → 标注工具集 → 手绘与区域效果 → 合成管线/剪贴板/裁剪+边缘取色；测试基线 351/351，详见 issues/done-m1-m6d.md）（2026-11-01）
