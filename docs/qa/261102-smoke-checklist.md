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
- [ ] 右侧面板 Style / Canvas 两个 tab 能切；点一个色块出现选中环 ⚑(§8.5)
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

- [ ] Style tab：色块选中环（首次进编辑器那次重点看，`RefreshSwatchStates` 依赖 Loaded 遍历）⚑(§8.5)；描边宽度 1–16、字号 12–72
- [ ] 选中几何后 `SelectionKindLabel` 文案正确（ArrowStraight/ArrowCurved 分支顺序已修，真机扫一眼）
- [ ] Canvas tab：实心/渐变预设可选；阴影/描边/内嵌三组 CheckBox + 滑杆联动；边框样式 ComboBox（外/中/内）；裁剪比例 ComboBox
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
