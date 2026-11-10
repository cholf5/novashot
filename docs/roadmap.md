# Prtsc 路线图（2026-11-01 修订）

> 依据：M6d 交接 `docs/handoff/261101-m6cd-handoff.md` + 对 `references/winshot`、`references/flameshot` 的功能差距盘点。
> 本文件是里程碑总表与缺口登记处；执行细节与踩坑记录仍以 handoff 文档为准。看板在 `kanban/board.md`。
> **2026-11-01 二次全量扫描**：两参考项目源码逐项重扫（flameshot 76 项 / winshot 66 项），新发现差距已搬入（G-1～G-25，详见 `.zcode/plans/261101-references-gap-sweep.md` 与 `docs/handoff/261101-gap-sweep-handoff.md`）；其余差距仍按下表分卡推进。

## 已完成

| 里程碑 | 内容 |
|---|---|
| M1–M3 | 脚手架、选框 8-handles、工具环、WGC+GDI 抓帧、Window picker |
| M4a–M4d | SkiaCanvas 分层渲染、Rect/Ellipse/Line/Arrow(直+曲)/Text/Number/Spotlight、Select+移动缩放+Undo/Redo、样式面板、工具热键 |
| M5 | Pencil / Highlighter / Mosaic / Blur |
| M6a–M6d | 合成管线（渐变背景/Padding/圆角/阴影/内衬/边框）+ 导出 PNG/JPEG/WebP + 剪贴板双向(CF_DIBV5/CF_DIB)+QuickSave + 非破坏裁剪/比例预设/边缘取色 |
| M7a–M7b | R2/GDrive 上传、Cloud 凭据配置、Library（索引+缩略图）、工具栏 Upload/Library/Pin/Cloud… 按钮、PinnedWindow 贴图、LibraryWindow、CloudSettingsWindow；测试 427/427 |
| MAC-1 | **macOS 平台层**：App 双 TFM + Prtsc.Platform.Mac（CoreGraphics 抓屏、CGWindowList 窗口、Carbon 热键、NSPasteboard 剪贴板、launchd 自启）；测试基线 506/506（Core 381 + Mac 72 + App 53×2TFM），mac 真机启动到托盘+热键注册验证通过（2026-11-01） |
| GAP-1 | **参考项目差距全量搬运（第一批）**：反色工具(I)、序号最小未用回收、Ctrl+滚轮改序号、角度吸附(Ctrl/Shift)、Esc 取消拖拽、Backspace 删除、QuickSave 复制路径、CF_HDROP 文件粘贴、CloseToTray/MinimizeToTray、窗口尺寸持久化、编辑器默认值接线、IncludeBackground 接线、Library 删除确认+Del 键+复制路径、托盘版本号、云连接测试；测试 553/553（Core 414 + Mac 76 + App 63×2TFM）（2026-11-01） |
| LINUX-1 | **Linux 平台层（X11）**：Prtsc.Platform.Linux（XGetImage/XRandR 抓屏含负坐标多屏拼接、EWMH 窗口枚举、XGrabKey 热键含 NumLock 变体、xclip 剪贴板、XDG Autostart）+ AppServices 运行时按 OS 分流（mac/Linux 共用裸 net10.0 TFM）；Wayland 原生会话留 LINUX-2；测试 596/596（Core 414 + Mac 76 + App 63×2TFM + Linux 44），真机冒烟 QA-L1 待领（2026-11-01） |
| UI-1 | **视觉重设计**：主题层（Tokens/Icons/Controls ControlTheme）替掉默认 Fluent；编辑器工具栏改底部悬浮 dock（修掉溢出）、右侧面板拆 Style/Canvas；主窗口改命令列表+最近 4 张；Library/云设置/窗口选择器统一重绘；IBM Plex 随包分发；**i18n（zh-CN/en）+ 托盘切语言**；headless 视觉快照（`PRTSC_SNAPSHOT=1`）；测试 666/666（Core 414 + Mac 80 + Linux 44 + App 64×2TFM）（2026-11-02） |
| M8 | **设置中心 GUI（S-1）**：六分区设置窗口（常规/热键/QuickSave/导出/编辑器默认值/捕获），整窗草稿 + 保存/取消；热键录制控件；语言入口从托盘搬进设置；新 Core 字段 `Capture.IncludeCursor`；1 Critical + 4 Important 审查项全修（2026-11-02） |
| M9 | **捕获增强（D-1 / E-1 / I-1 / N-1）**：延时截图 + Capture Launcher（G-26 合并）、屏幕取色器、Ctrl+O 导入、桌面通知；顺带修两个 headless 并发坑（位图延一跳 Dispose / 静态画刷 Immutable）与 B-1 PNG 剪贴板格式、B-8 Mac 剪贴板解码守卫（2026-11-10） |
| M10 | **分发就绪（L-1 / L-2 / R-1 + C-1 Updater）**：CLI（gui/full/region/window，单实例管道转发，headless 1.23s）、i18n 收尾（key 一致性单测 + 硬编码审计）、Velopack 打包脚本 + CI 三 OS 矩阵 + 应用内更新检查；测试 1194/1194（2026-11-10） |

## 未完成

| 里程碑 | 内容 | 看板卡 | 状态 |
|---|---|---|---|
| M11 | **v1.0 收尾门**：① 真机人工冒烟（QA-1 清单 A–P 共 88 项，勾选数 0；QA-L1 Linux 段未领）；② 发布收尾（版本号 0.1.0 → 发布版、tag、CI 首跑、README + 用户文档、签名方案、mac 分发渠道） | QA-1 / QA-L1 / V-1 | 收尾门（代码侧已无半成品，`dotnet build` 0 warning / 0 error） |

### 与旧交接文档编号的映射

旧 M8（CLI/通知/快捷键自定义/Velopack）拆分：通知 → M9；快捷键自定义 → M8 设置中心；CLI + Velopack → M10。旧 M9（v1.0）→ M11。

## 缺口来源（winshot / flameshot 对比结论）

> 2026-11-01 二次扫描后更新：下述“两边都有而 prtsc 缺”里，QuickSave 复制路径、CF_HDROP 文件粘贴、CloseToTray/MinimizeToTray/窗口尺寸持久化、编辑器默认值接线、IncludeBackground、Library 删除确认/复制路径、云连接测试已搬完（GAP-1）；M9/M10 各项已于 2026-11-10 交付。剩余看 Backlog：V-1（发布收尾）、LINUX-2（Wayland）（Q-1 栈溢出 flake 已于 2026-11-10 修复，见 Done）。

- **两边都有而 prtsc 缺**：延时截图（D-1 ✅）、设置 GUI（S-1 ✅）、桌面通知（N-1 ✅ Windows；mac/Linux 原生通知明确不做）、CLI（L-1 ✅）、i18n（L-2 ✅）。
- **flameshot 独有值得抄**：屏幕取色器（E-1 ✅）、Pin 贴图（已并入 C-1 ✅）、工具栏按钮自定义（B-6 ✅）、反色（X-1 ✅ GAP-1 实装）、 Capture Launcher（G-26 ✅ 与 D-1 合并做）、侧面板色轮/Hex 编辑（并入 S-1 ✅）、放大镜（并入 E-1 ✅）。
- **winshot 独有值得抄**：渐变背景合成（M6 ✅）、R2/GDrive 上传（C-1 ✅）、导入图片（I-1 ✅）、WindowPicker 缩略图（B-2 ✅）、连接测试（GAP-1 ✅）。
- **prtsc 自有积压**：PNG 剪贴板格式（B-1 ✅）、跨屏选框（B-3 ✅）、crop reshape（B-4 ✅）、真机冒烟（QA-1，P0，未清零）。
- **决策不抄**：flameshot DBus/守护进程（Linux 专有）、上传历史窗口（Library 已覆盖）、UI 颜色编辑器/取色预设编辑器（S-1 范围外美化）、网格吸附（低频）、winshot 鼠标滚轮画布缩放（上游自己禁用了）、winshot showNotification 假配置（不搬 bug）、全屏=光标屏（prtsc 整虚拟桌面语义保留）。

## 顺序建议

QA-1 冒烟随时可做且是 P0；Q-1 已修（App 测试 25 连跑无 abort），发布门的测试稳定性不再是阻塞项；V-1 的版本号/tag 与 README 可并行起草，打 tag 前把真机冒烟 P0 段清掉即可。
