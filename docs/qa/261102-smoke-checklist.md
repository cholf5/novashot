# QA-1 真机冒烟清单 — UI-1 改版后重写（2026-11-02）

> 适用版本：`9c9d605`（UI-1 视觉重设计 + i18n）。旧冒烟清单按 M4 时代 UI 写的（按钮名、面板位置全变），本文取代之。
> 条目按 `9c9d605` 代码枚举（`MainWindow.axaml` / `StylePanel` / `CompositionPanel` / `App.axaml.cs`），执行以实机为准。
> 记录方式：每项 ✅ / ❌ / ⚠️，❌ 顺手记一句现象（开卡用）。标注 ⚑ 的是 handoff §8 诚实清单里「只有静态快照、没在真机点过」的项，优先看。

## 环境注意（handoff §4.3）

- mac 上**不要**从后台 shell `./Prtsc &` 起进程（坑12：起得来但激活不了，点不到）。从前台终端或 Finder 双击启动。
- 屏幕别锁/别休眠；若遇到 `error code -6661`，`caffeinate -u -t 5` 唤醒后重启 app（坑13）。

## A. P0 快速版（约 15 分钟，先跑这段）

- [ ] 托盘单击 → 主窗口弹出：标题、命令列表（全屏/区域/窗口）、最近 4 张、底部状态栏 + 版本号都在
- [ ] 托盘菜单触发全屏捕获 → overlay → 编辑器打开 → `Ctrl+C` 复制到剪贴板成功
- [ ] dock 点 **矩形** → 拖一笔；按 `T` → 画布点一下输入文字；`Ctrl+S` 导出成功
- [ ] 右侧面板样式/画布单页直排可滚读（无 tab，见 docs/design/2026-11-22-merge-style-canvas-panel.md）；点一个色块出现选中环 ⚑(§8.5)
- [ ] 托盘语言切到 English → 主窗口与托盘菜单文案全变英文；切回 zh-CN 同理 ⚑(§8.3)（S-1 后入口在设置中心，托盘语言子菜单已删——顺手验证确实没了）
- [ ] 托盘/主窗口 ⋯ 菜单打开**设置中心**：六分区导航可切；语言改 English → 保存 → 全局文案刷新；热键改录制（如 Ctrl+Shift+F9）→ 保存 → 新热键生效旧热键失效；录制时按 Esc 能退出录制态
- [ ] 托盘打开 Library：缩略图是真图不是黑块 ⚑(§8.6)，计数文案正常

## B. 主窗口 · 空闲态

- [ ] 命令列表三行 hover 有反馈 ⚑(§8.1)
- [ ] 最近 4 张卡片 hover 反馈；点击任一张 → 回到编辑器加载该图
- [ ] 窗口缩到最小宽度不裁按钮（1200px 溢出 bug 的回归观察）

## C. 捕获三流程

- [ ] 全屏捕获：overlay 全屏、底部提示条文案（i18n）、悬浮确认环（FloatingToolRing）显示
- [ ] 区域捕获：拖选、确认环点击确认 / Esc 取消
- [ ] 窗口捕获：WindowPickerDialog 列表刷新、选中高亮、Ok/Cancel（注意 Refresh/Cancel/Ok 是 code-behind 挂的）
- [ ] 捕获流程里的 ToolTip ⚑(§8.2)

## D. dock 工具 ×15（点选 / hover / 热键 / 画一笔）

| 工具 | 热键 | 工具 | 热键 |
|---|---|---|---|
| Select | `V` | Spotlight | `S` |
| Rectangle | `R` | Pencil | `P` |
| Ellipse | `E` | Highlighter | `H` |
| Line | `L` | Mosaic | `M` |
| ArrowStraight | `A` | Blur | `B` |
| ArrowCurved | `O` | Invert | `I` |
| Text | `T` | Crop | `X` |
| Number | `N` | | |

- [ ] 每个：点选有选中态；hover 有 chip 反馈 ⚑(§8.1)；无修饰热键能切换；选中后各画一笔生效
- [ ] dock 右端热键字母（8.5px）在真机字号下可读
- [ ] **反向**：`T` 进入文字编辑态后再按 `R`，应输入字母而不是切工具（OnKeyDown 的 EditingText 守卫）

## E. 编辑器顶栏 + 快捷键

- [ ] Back / Undo / Redo：功能 + ToolTip 样式 ⚑(§8.2)
- [ ] **Copy 主按钮**（白底深字）正常显示 + hover 态 ⚑(§8.1)（对应坑3：全局 TextBlock 前景色的回归点）
- [ ] Export（ghost）→ 导出流程；ToolTip `Editor.SaveTip`
- [ ] `⋯` MenuFlyout：右下对齐、菜单项渲染正常（Fluent 原样风险）⚑(§8.2)
- [ ] 快捷键：`Ctrl+S` 导出 / `Ctrl+C` 复制 / `Ctrl+Q` 快存 / `Ctrl+V` 粘贴 / `Ctrl+U` 上传 / `Ctrl+L` 历史 / `Ctrl+P` Pin
- [ ] Slider 拖动手感（填充段画不出来是已知项，收尾(a)再修，不算 ❌）

## F. 右侧面板

- [ ] 样式段：色块选中环（首次进编辑器那次重点看，`RefreshSwatchStates` 依赖 Loaded 遍历）⚑(§8.5)；描边宽度 1–16、字号 12–72
- [ ] 选中几何后 `SelectionKindLabel` 文案正确（ArrowStraight/ArrowCurved 分支顺序已修，真机扫一眼）
- [ ] 画布段（同页下方滚动可见）：实心/渐变预设可选；阴影/描边/内嵌三组 CheckBox + 滑杆联动；边框样式 ComboBox（外/中/内）；裁剪比例 ComboBox
- [ ] 背景三态 chip（None / Solid / Gradient）互斥切换：点哪个就是哪个、选中环只在当前档，切回 None 时棋盘格预览出现；连续快速点十几下不卡不乱（Q-1 修复后输入改走 Click + 单向回显，真机验证交互手感）
- [ ] 文字编辑态下 TextBox 拿焦点时热键不抢输入

## G. 导出 / Pin / 上传 / Library

- [ ] 导出对话框完整（路径、格式、质量——质量滑杆在 S-1 才有 UI，此处只验导出本身）
- [ ] QuickSave 存到配置目录
- [ ] `Ctrl+P` Pin → PinnedWindow 贴图 + 底部提示条（i18n）
- [ ] 上传（若已配渠道）→ 返回链接
- [ ] Library：列表、缩略图（320px 真图 ⚑(§8.6)）、计数文案、打开/删除

## H. 语言切换（i18n 首次真机，重点段；S-1 后入口在设置中心）

- [ ] 设置中心语言分段（跟随系统 / English / 简体中文）选中态显示正确；保存前全局文案不变（草稿语义）
- [ ] 保存后跟随刷新：主窗口标题（ApplyLocalizedTitle）、状态栏、双面板、Library、云设置、WindowPicker、overlay/贴图提示条
- [ ] **切换时已打开的其它窗口**标题/文案是否跟随 ⚑(§8.3 未逐一验证；DynamicResource 文案会热刷，普通 string 标题不热刷——已知项)
- [ ] `config.json.language` 持久化：重启后语言保持
- [ ] 中文观感：mac=PingFang 行高/字重 ⚑(§8.4)；UI 无中英混排残留

## I. Windows 真机段（§8.7：Windows 从没跑过）

- [ ] `dotnet build Prtsc.slnx` + windows-TFM 测试在 Windows 机跑一遍
- [ ] 前台启动跑一遍 **A 段 P0 快速版**
- [ ] 微软雅黑回退观感：字号/行高/字重是否要调 ⚑(§8.4)
- [ ] Fluent 原样控件（ScrollBar/TextBox 内边/ComboBox）在 Win 上的深色观感
- [ ] 设置 → 捕获 → 取消勾选「包含鼠标光标」→ 保存 → 截图画面无光标（`Capture.IncludeCursor` → WGC `IsCursorCaptureEnabled` 管道无 headless 覆盖，只能真机验）
- [ ] 设置中心自启开关真机验证（Win 注册表 Run 项 / mac launchd LaunchAgent 真的写入）
- [ ] 设置保存两次（**不改热键**）→ 热键仍工作；改热键保存 → 新键生效（Windows 泵侧重注册竞态 C1 修复的真机验证）

## J. Linux 真机段

- [ ] 设置里改热键保存 → 旧组合不再触发、新组合生效（XUngrabKey 修复，I3）

## K. N-1 桌面通知真机段（Windows toast 只能实弹验；2026-11-10 追加）

> 接线与开关判定已有单测（Core 578 / App 154×2TFM）；本段全部是 Windows toast 实弹项，
> mac/Linux 本卡为 Null 静默（无通知 UI，验「完全无感不炸」即可）。

- [ ] **安装版 toast 实弹**（vpk Setup.exe 装好 → 开始菜单快捷方式在册）：编辑器导出成功 → Windows 右下角弹「Screenshot saved / 截图已保存」toast，正文带保存路径；QuickSave（`Ctrl+Q`）→「快速保存完成」；`Ctrl+C` →「已复制到剪贴板」；配渠道上传成功 →「上传完成」正文含 URL（handoff 诚实清单：本机 macOS 只能交叉编译，未实弹）
- [ ] **点击 toast 开目录**：QuickSave/导出的 toast 点击 → explorer 打开并选中该文件（`explorer /select,<path>`）；上传/复制类 toast 点击无动作（无 path，尽力而为语义）
- [ ] **未打包降级**：`dotnet run`（或便携 zip，无开始菜单快捷方式 → AUMID `Prtsc` 未注册）→ toast 可能不弹，但**绝不崩溃**，日志只有一条 Warning（`Toast notification failed`）；导出/复制/上传主流程不受影响
- [ ] **开关全关**：设置 → 常规 → 取消「截图完成通知」→ 保存 → 导出/QuickSave/复制/上传全部静默（无任何 toast）；重新勾选 → 恢复
- [ ] **失败不扰**：通知被系统「专注助手/勿扰」拦截或通知权限关掉时，截图流程零感知
- [ ] mac/Linux 冒烟：通知开关开/关两种状态下正常截图导出（Null 服务静默，无窗口、无报错、无日志异常）
- [ ] 默认配置（`Export.AutoCopyToClipboard=true`）导出一次 → 观察 toast 数量：**预期 1 条**（导出那条带路径；导出内自动复制不再弹复制 toast——N-1 review I-1 已抑制双弹，管线走 `CopySilently`）。若看到 2 条记 ❌（回归）；配 AutoUpload 时总共 2 条（导出 + 上传）


## L. I-1 导入图片真机段（文件对话框只能实弹验；2026-11-10 追加）

> 解码/所有权/失败提示复用 `ImportImageFile`（G-11 单测已覆盖）；选择器 UI 与取消分支 headless
> 只能驱动 Noop 空结果。本段全部是文件对话框实弹项（Windows 真机 + macOS 各一轮）。

- [ ] **入口**：编辑器 `⋯` 菜单出现「打开图片文件… / Open image file…」；空闲态（无编辑器）按 `Ctrl+O` 也能弹文件对话框
- [ ] **图片组过滤**：对话框默认「图片文件 (PNG、JPEG、WebP、BMP、GIF)」，另有「全部文件」一档
- [ ] **PNG/JPEG/WebP 全流程**：各选一张 → 进编辑器，标注、导出（Ctrl+S）正常；状态栏为「已截取 W × H」而非报错
- [ ] **BMP/GIF**：同样能解码进编辑器（Skia 常见面）
- [ ] **非图拒绝**：切「全部文件」选一个 `.txt` → 状态栏「不支持的图片格式：.txt」，不进编辑器
- [ ] **取消无副作用**：空结果（Esc/取消按钮）→ 空闲态不进编辑器、状态栏不变；编辑器态不动当前图、不打断编辑
- [ ] **替换语义**：编辑器态 Ctrl+O 选另一张图 → 当前编辑器被替换（旧标注丢弃），无崩溃/无残留
- [ ] **文本编辑保护**：Text 工具编辑文字时 Ctrl+O 不触发对话框（不抢输入）
- [ ] **HDROP 回归（G-11）**：资源管理器复制图片文件 → Ctrl+V 仍能粘进编辑器（判定真值改共用 `ImageFileTypes` 后的行为回归）
- [ ] 双语：语言切 English → 菜单项/对话框标题/类型过滤名全英文

## M. D-1/G-26 延时截图 + Capture Launcher 真机段（2026-11-10 终审补录；素材源 handoff 261110-d1 §3/§4）

> 倒计时状态机/轮次裁决/settle 间隙均有 Core 单测（含「去实现必挂」回归用例）；本段全是真机项。

- [ ] **macOS 延时 3s 抓下拉菜单**：设置默认延时 3s（或托盘「延时截图 3s」）→ 倒计时期间展开一个下拉菜单 → 到点抓帧，菜单完整入框
- [ ] **热键取消即时性**：倒计时期间按任意截图热键 → 本轮取消（角标提示 + 主窗状态栏「已取消」），不启动新一轮
- [ ] **角标不入快照**：倒计时角标在抓帧前已关（250ms 间隙），快照/overlay 画面里无角标残留；Region 模式「倒计时结束才开 overlay」时序确认
- [ ] **托盘/启动器再触发 = 取消旧轮按新意图**：倒计时中再点托盘延时子项 → 重启倒计时；Launcher 选 0 秒 → 直接抓
- [ ] **混合 DPI 角标居中**：多屏不同缩放下角标顶部居中精度（EstimatedWidthDip=150 估算，可能略偏，功能无碍）
- [ ] **Windows**：托盘「延时截图」子菜单形态（NativeMenuItem 嵌套）；WGC 抓帧时角标真的不在画面里（250ms 对 DWM 合成是否足够）；倒计时期间全局热键可达性
- [ ] **G-26 启动器**：X/Y/W/H 越界钳制（负坐标/超桌面）、多屏负坐标布局真机确认

## N. E-1 屏幕取色器真机段（2026-11-10 终审补录；素材源 handoff 261110-e1）

> 取色/放大镜/浮签布局有 headless 单测；真机只能验像素取值与观感。

- [ ] **macOS 128/192 dpi 取色精度**：对比 Digital Colour Meter（注意其默认 sRGB 显示配置）逐像素核对 HEX/RGB
- [ ] **2x Retina 屏放大镜观感**：11×11 网格 + 中心高亮在 2x 下的清晰度/网格对齐
- [ ] **多屏边缘浮签避让**：光标贴近屏幕边缘时 HEX/RGB 浮签翻转不裁切
- [ ] **复制 HEX → 粘贴**：取色点击复制 → 输入框粘贴出 `#RRGGBB`；Stroke 路径（先选中形状再 C 取色）生效
- [ ] **历史行再取色**：StylePanel 会话历史行点击 → 色值回填
- [ ] **Windows**：CF_UNICODETEXT 粘贴到任意输入框
- [ ] **Linux**：xclip 缺失时取色模式仍可用（复制静默失败、色值入历史，不崩）

## O. L-1 CLI 真机段（2026-11-10 终审补录；素材源 handoff 261110-l1 §4/§5 + 终审威胁边界）

> macOS 实弹已全通（headless/转发/退出码电池，见 handoff §5/§7）；本段是 Windows/Linux 腿 + 打包共存。

- [ ] **Windows CLI 实弹**：`prtsc full --clipboard` 无实例 headless（<2s、剪贴板实有图）、CLI 进程内 Win32 剪贴板可用性、WGC headless 抓帧（`ProbeSupported` 构造期探测在 CLI 进程的行为）
- [ ] **Windows 管道威胁边界**：同机另一用户会话 `\\.\pipe\prtsc-cli-<tag>` 可连接/可抢注（无 ACL）——后果记录：可向 GUI 发命令触发截图/activate、可让 GUI 建流失败（吞异常 → 转发永久失效）、CLI argv（含 --path）会发给抢注者；后续考虑 PipeSecurity 限当前用户 SID
- [ ] **Linux CLI**：无实例 headless 全链；自定义超长 TMPDIR 下管道名超 104 字符退化（预期「永远 headless、不炸」）；Linux 剪贴板在 CLI 进程可用性
- [ ] **macOS 权限拒绝文案**：屏幕录制未授权时 `full` 的 stderr 指引文案（设计内失败 UX，exit 1 不崩）
- [ ] **`--delay 60` 满额等待**：端到端耗等一次（含转发路径服务端串行执行的上界）
- [ ] **vpk 共存实弹**：安装版（开始菜单）CLI 与 Velopack 更新重启共存——重启目标落入 gui 分支、argv 干净；Portable/未打包下的单实例转发
- [ ] **转发交互延时语义（终审 I-1 修复回归）**：config 默认延时 3s 时——本地热键 region 与 `prtsc region`（转发、省略 --delay）都出现 3s 倒计时；`prtsc region --delay 0` 立即开 overlay（显式压过默认）

## P. B-6 工具栏自定义 + B-2 WindowPicker 缩略图真机段（2026-11-11 追加；素材源 kanban issues B-6/B-2）

> 可见性决策/分隔线坍缩/降级路径有 headless 单测 + 快照（06-editor-dock-filtered.png）；真机验观感与交互。

- [ ] **B-6 设置页多选**：设置 → 编辑器默认值 → 工具栏按钮 15 勾选框（Select 灰色禁改）；关掉一半保存 → 主窗口工具坞立即只剩启用项、分隔线不悬空；全选保存重开 config.json `visibleTools` 为空表
- [ ] **B-6 隐藏工具热键**：隐藏 Rectangle 后进编辑器按 R 仍切到 Rectangle（状态栏/行为变化，dock 无按钮）；当前工具被隐藏后进入编辑器回落 Select
- [ ] **B-6 语言切换**：zh/en 切换后设置页勾选框标签跟随（Loc 双语）
- [ ] **B-2 缩略图实弹**：窗口捕获 picker 列表每项 96×54 实时缩略图清晰；最小化/全屏视频窗口降级为进程首字母占位不崩；Refresh 后缩略图重载不残留旧图
- [ ] **B-2 多屏 DPI**：Retina 主屏 + 外接 1x 屏窗口缩略图均清晰（无二次模糊/尺寸错）

## Q. B-9 Windows 深色标题栏真机段（2026-11-11 追加；素材源 kanban issues B-9）

> DWM 视觉只能真机目检（Avalonia headless 句柄是零值 STUB）；自动测试只锁「每个窗口都被钩到 / 无句柄跳过 / 明暗回退决策」。

- [ ] **最小验收**：夜间模式（Windows 深色主题）完全退出进程 → 重开：主窗标题栏直接深色，不需要拖动/最大化/任何交互
- [ ] **对话框逐个**：设置中心 / 云设置 / Library / WindowPicker / 更新提示：每个窗口标题栏都是深色（各自 HWND）
- [ ] **启动隐藏路径**：`MinimizeToTray=true`（或 `--autostart`）启动 → 从托盘点开主窗：标题栏仍是深色（Opened 补写路径）
- [ ] **浅色回退**：临时把 `App.axaml` `RequestedThemeVariant` 改 Light（或运行期切浅色）→ 标题栏回白，无深色残留（显式清零生效）
- [ ] **Win10 与 Win11 各一台**：Win10（19041+ 属性 20 / 17763–18362 回落 19）与 Win11 22000+（Avalonia 也会写同一属性）标题栏一致
- [ ] **无 DWM 环境**：远程桌面/精简环境启动不崩（属性写入失败静默降级为系统默认标题栏）
