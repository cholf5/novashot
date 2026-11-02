# Prtsc 交接备忘 — 2026-11-02（视觉重设计 UI-1 + i18n 落地）

> 目标读者：下一位接手的 AI Agent。本文件取代 261101-m6cd-m7a-handoff；历史坑请以本文 §4 为准（旧版仍可查，但视觉层的坑只有本文有）。

## 0. 本会话结论（TL;DR）

- **UI-1 完成**：默认 Fluent 外观整体替换为自建主题层（Tokens / Icons / ControlTheme），编辑器工具栏改底部悬浮 dock（顺带修掉 1200px 下按钮被裁的溢出 bug），右侧面板拆 `Style` / `Canvas`，主窗口改「命令列表 + 最近 4 张」，Library / 云设置 / 窗口选择器 / 悬浮确认环 / 贴图提示条统一重绘。
- **i18n 落地（zh-CN / en）**：`Loc.cs` 两张表 + `DynamicResource L.*`，托盘菜单可切、持久化到 `config.json.language`。
- **新增视觉快照机制**：Avalonia headless + Skia 把真窗口渲成 PNG（`artifacts/ui/`），受 `PRTSC_SNAPSHOT=1` 开关控制，不进常规测试。
- 测试 **666/666**（Core 414 + Mac 80 + Linux 44 + App 64×2TFM），`dotnet build Prtsc.slnx` 0 error。mac 真机启动 + 主窗口外观确认通过。
- **风险**：界面只做了「渲染层」验证，**交互态（悬停、点选、Flyout、ToolTip、托盘菜单）没有逐项真机点过**——见 §8。

## 1. 进度表

| M | 状态 |
|---|---|
| M1–M7b | ✅ 完成 |
| **UI-1（视觉重设计 + i18n）** | ✅ **完成**（本会话） |
| M7 Updater（C-1 卡） | ⏸ 仅剩此项，等 R-1 定 Velopack |
| M8 设置中心（S-1） | 待开工（**新增**：语言切换入口要搬进来） |
| M9 捕获增强（D-1/E-1/N-1/I-1） | 待开工 |
| M10 分发（L-1/L-2/R-1） | 待开工（L-2 的 i18n 主体已被 UI-1 吸收） |
| QA-1 真机冒烟 | ⚠️ P0，自 M4 起持续积压；**本次改版后清单需要重写** |

## 2. 视觉层结构（新会话先读这段）

```
src/Prtsc.App/
  Themes/
    Tokens.axaml    颜色/字体/圆角资源（唯一色板来源；改配色只动这里）
    Icons.axaml     24×24 stroke 图标 StreamGeometry（Lucide 改编，ISC）
    Controls.axaml  所有 ControlTheme + 全局元素样式（样式层唯一入口）
  Localization/
    Loc.cs          Strings.En / Strings.Zh 两张表 + Loc.T / SetPreference
  Controls/
    Keycap.cs       键帽控件（Theme 必须在用点内联，见 §4.1）
  Assets/Fonts/     IBM Plex Sans(400/500/600) + Plex Mono(400) + OFL.txt
```

**加一个图标**：在 `Icons.axaml` 加 `<StreamGeometry x:Key="Icon.Xxx">M…</StreamGeometry>`（24×24 视框，stroke 由使用方给），用时
`<Path Classes="glyph" Data="{DynamicResource Icon.Xxx}"/>`。实心图标加 `Classes="glyph solid"`，虚线加 `glyph dash`。

**加一条文案**：`Loc.cs` 里 `Strings.En` 和 `Strings.Zh` 各加一条（key 不带 `L.` 前缀）。XAML 用
`{DynamicResource L.Capture.Title}`，代码用 `Loc.T("Capture.Title")`（**不带前缀**）。漏写 key 时回落英文，再回落 key 本身。

**加一个工具按钮**（编辑器 dock）：复制 MainWindow.axaml 里任一 `<RadioButton Classes="tool" GroupName="Tool" Tag="…">`，`Tag` 是 `ToolKind` 的枚举名，内容模板照抄（图标 + 8.5px 热键字母），`IsChecked` 用 `EnumEq` 转换器绑定。热键在 `MainWindow.axaml.cs::OnKeyDown` 里加。

## 3. 本会话改动清单（按层）

**主题层（新）**：`Themes/Tokens.axaml`、`Themes/Icons.axaml`、`Themes/Controls.axaml`；
`App.axaml` 改成 `RequestedThemeVariant="Dark"` + 合并 token/icon 资源 + `StyleInclude` 样式层；
`Prtsc.App.csproj` 加 `<AvaloniaResource Include="Assets/Fonts/*.ttf" />`。

**界面**：
- `Views/MainWindow.axaml` 整页重写（48px header / 主体 / 30px 状态栏；空闲态 = 命令列表 + 最近 4 张；编辑态 = 画布 + 底部 dock + 264 右侧面板）。`.axaml.cs` 加 `OnPanelTabClick`（纯视图态切 tab）、`OnRecentShotTapped`（点最近图 → `ImportImageFile` 回编辑器）、`ApplyLocalizedTitle`。
- `Controls/StylePanel.axaml`、`Controls/CompositionPanel.axaml` 重写（**保留** `x:Name="ColorSwatches"` / `GradientPresets` 和全部 `OnXxxClick` 签名——`CompositionPanelXamlTests` 按名字查 ItemsControl 并断言 24 项）。
- `Views/LibraryWindow.axaml`、`Views/CloudSettingsWindow.axaml`、`Views/WindowPickerDialog.axaml`、`Controls/FloatingToolRing.axaml`、`Views/OverlayWindow.axaml`（仅底部提示条）、`Views/PinnedWindow.axaml`（仅提示条）重绘/改色。
- `Views/WindowPickerDialog.axaml` 注意：**Refresh/Cancel/Ok 的 Click 是 code-behind 里挂的**，XAML 里不能再写 `Click="OnXxx"`（会编译失败）。

**视图模型**：
- `MainWindowViewModel`：`VersionText`、`RecentEntries` / `HasRecent` / `RefreshRecentAsync()`（读 Library 前 4 条 + 缩略图），`StatusText` 全量走 `Loc.T`。
- `LibraryViewModel`：`CountText` 走 `FormatCount`；缩略图加载抽到 `ViewModels/LibraryThumbnails.cs`（窗口和主窗口共用）。
- `StylePanelViewModel`：新增 `StrokeHex`；`SelectionKindLabel` 走 `Kind.*` 文案（注意分支顺序：`ArrowCurved/ArrowStraight` 必须排在 `LineAnnotation` 之前）。
- `CloudSettingsViewModel`：新增 `IsNoneVisible`（渠道=none 时的空状态卡片）。

**配置/入口**：`PrtscConfig.Language`（"system"/"en"/"zh-CN"）；`App.axaml.cs` 启动时 `Loc.Initialize`，托盘菜单整表重建（`RebuildTrayMenu`）+ 语言子菜单（Radio），切语言后调 `MainWindow.ApplyLocalizedTitle()`。

**测试/文档**：`tests/Prtsc.App.Tests/UiSnapshotTests.cs`（新增）、`docs/design/2026-11-02-ui-redesign.{md,html}`、`docs/roadmap.md`（UI-1 行）、`.gitignore`（`.tmp/`、`artifacts/`）。

## 4. 关键坑（新会话必读）

### 4.1 Avalonia 12 视觉层

1. **`Styles` 根级只能用 `<Style>`，`<Styles.Resources>` 里只能放 `ControlTheme`/资源**。把 `<Style Selector="Button">` 放进 `Styles.Resources` 会编译成 AVLN3000「无法为 Resources 找到 adder」。
2. **`Style Selector="控件前缀:Type"` 在 Styles 文件里解析不了 `using:` 前缀**（AVLN2000「Unable to resolve type controls」）。所以 `Keycap` 没有类型级样式，用点必须写 `Theme="{StaticResource KeycapTheme}"`（见 MainWindow 里的 8 处）。
3. **不要写全局 `TextBlock { Foreground }` 样式**。它会盖掉控件继承的 Foreground，主按钮（白底深字）的文字直接消失。靠 `Window.FontSize/Foreground` + 局部 `Classes` 继承。
4. `ControlTheme` 里可以嵌套变体样式（`^.primary`、`^.swatch:checked`…），**优先级低于直接设在控件上的值**——色块那种「Background 用绑定设在元素上」的写法因此不会被 `:pointerover` 洗掉。
5. **Slider 在 Avalonia 12 给 Decrease/Increase 按钮分配 0 宽度**（实测 `Bounds=(0,8,0,3)`，Fluent 自带模板同样），所以自己做模板时填充段画不出来。当前实现：全宽 `Border` 当槽 + `Thumb` 当指针，`Track` 必须绑 `Minimum/Maximum/Value/Orientation`（不绑连指针位置都是错的）。要填充段得自己算布局。
6. `Track.DecreaseButton/IncreaseButton` 类型是 `Button`（用 `RepeatButton` 实例化没问题），`RepeatButton` 命名空间是 `Avalonia.Controls`（不是 `.Primitives`）。
7. Fluent 的 `TextBox` 模板内边是 `Border#PART_BorderElement`，`ComboBox` 只做了属性级覆盖——想更深改就得整块换模板。
8. 字体 `FontFamily` 走 `avares://Prtsc/Assets/Fonts#IBM Plex Sans`；**中文字形靠系统回退**（mac=PingFang，Win=微软雅黑），headless 快照里中文已验证可读。

### 4.2 视觉快照

9. `HeadlessUnitTestSession` 的 `CaptureRenderedFrame()` 返回 null（headless drawing 模式）。快照必须自己做 `AppBuilder.Configure<SnapshotBootstrap>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting()`。
10. 这会初始化一个**全局** Avalonia 实例，和 `HeadlessUnitTestSession` 共用进程会互相踩（`OverlayWindowInputTests` 会挂）。所以快照测试用 `PRTSC_SNAPSHOT=1` 环境变量门控，默认直接 return。
11. 快照里加载真主题必须在 bootstrap App 里手动 `Styles.Add(FluentTheme)` + `StyleInclude("avares://Prtsc/Themes/Controls.axaml")` + 合并 Tokens/Icons 两个 ResourceInclude。

### 4.3 环境（mac 上跑 GUI）

12. 从后台 shell 直接 `./Prtsc &` 起的进程**无法被激活**（`NSRunningApplication.activate` 返回 false，AX 树里看不到 window，`osascript click at` 只命中 "Avalonia Application"）。所以本轮交互验证改用 headless 快照。
13. 屏幕休眠时 `Avalonia.Native` 起不来：`RenderTimer ... error code -6661`。`caffeinate -u -t 5` 唤醒后即可正常启动。
14. 视觉核对用的窗口截图：`screencapture -x -o -l <windowId>` + 小型 swift 脚本列 `CGWindowListCopyWindowInfo`（可复用 `/tmp/winlist.swift` 的思路，未入库）。

### 4.4 其他（沿用）

15. `SKRect` 是 Left/Top/Right/Bottom；`SKBitmap.FromImage` 产物在 macOS 逐像素访问可能 SIGSEGV；`CapturedImage` 位图所有权不外泄（不要 `using var`）。
16. `LibraryStore.AddAsync` 在 UI 线程上 `GetAwaiter().GetResult()` 会死锁（SemaphoreSlim + SynchronizationContext），测试里用 `Task.Run(...)` 包一层。
17. windows-TFM 测试项目不传递 macOS 原生 Skia/HarfBuzz，App.Tests 里已有条件包。

## 5. 验证方式

```bash
# 编译（两个 TFM）
dotnet build Prtsc.slnx

# 测试（mac：666 全绿）
dotnet test Prtsc.slnx -f net10.0
dotnet test tests/Prtsc.App.Tests -f net10.0-windows10.0.19041.0

# 重新生成视觉快照（输出 artifacts/ui/*.png）
PRTSC_SNAPSHOT=1 dotnet test tests/Prtsc.App.Tests --filter UiSnapshot
```

当前基线：`dotnet build` 0 error / 15 warning（历史性 AVLN5001 等）；测试 **666/666**。
快照共 8 张：`01-capture-en` `02-editor-en` `03-editor-canvas-tab-en` `04-capture-zh` `10-panel-style` `11-panel-canvas` `20-library` `21-cloud`。

## 6. 用户要求（贯穿，未变）

1. **不主动运行 Prtsc.exe**（本轮为例外：用户要求看实际效果；平时 build+test 绿即可）。
2. 「继续」即推进；一里程碑一 commit；handoff 入库 `docs/handoff/`；计划入 `.zcode/plans/`（gitignore）。
3. Core 无 Avalonia/Win32；平台相关进 `Prtsc.Platform.*`。
4. 界面文案要么英文要么中文，别中英混排（UI-1 已统一走 i18n 表）。
5. **只有深色**：`RequestedThemeVariant="Dark"` 是有意为之，不要顺手加浅色主题。

## 7. 下一步建议（按优先级）

| 顺序 | 事项 | 范围 | 验收 |
|---|---|---|---|
| 1 | **QA-1 真机冒烟（重写清单）** | 老清单按旧 UI 写的：工具栏按钮名、面板位置全变了。新清单要覆盖：dock 15 个工具点选/hover/热键、面板 tab 切换、色块选中环、滑杆拖动、Flyout（⋯ 菜单）、ToolTip、最近 4 张点击回编辑器、中文/英文切换后各窗口（含托盘菜单） | 用户真机逐项过；发现问题开卡 |
| 2 | **M8 设置中心（S-1）** | 常规（自启/托盘/通知）/ 热键录制 / QuickSave / 导出质量 / 编辑器默认值 / WGC 光标 / **语言**。新主题层可直接复用（`Themes/` 已是通用层），建议做成独立 Window + 左栏分段导航 | 所有 config 字段有 UI；保存后热键重注册；语言切换从托盘搬进设置 |
| 3 | **视觉收尾（小）** | (a) Slider 填充段（自己算布局）；(b) Windows 真机看图（本轮只在 mac 渲染过，字体度量/描边可能需微调）；(c) 深色 `ScrollBar` 还是 Fluent 原样；(d) `MenuFlyout`/`ToolTip` 只做了基础覆盖，未截图验证 | 快照里补 Windows 形态；菜单/tooltip 有图 |
| 4 | **M9 D-1 延时截图** | 托盘 3s/5s + `Capture.DelaySeconds`；overlay 不得先于快照出现 | 与 flameshot/winshot 行为对齐；写单测 |
| 5 | **M9 E-1 取色器** | 放大镜 + HEX/RGB 浮签 + 点击复制，可设为描边色。视觉上是本次主题最容易吃红利的一块（用 Tokens 直接画） | 真机取色正确；浮签样式与主题一致 |
| — | M7 Updater（C-1） | **仍被 R-1 卡住**：需要用户先拍板 Velopack | 拍板后可开工 |
| — | L-1 CLI / R-1 打包 | 可与上面并行，但要先有 Velopack 决策 | — |

## 8. 未验证 / 需要真机确认（诚实清单）

1. **悬停态没看过图**：`Button:pointerover`、工具 chip hover、色块 hover、列表行 hover、dock 选中切换，都只有静态快照。
2. **ToolTip 与 MenuFlyout 样式**只写了 Style，没渲染验证；`MenuFlyoutPresenter` 在 mac 上可能被原生菜单接管，样式未必生效。
3. **托盘语言菜单**：`NativeMenu` 的 `ToggleType=Radio` 勾选状态在 mac/Win 表现可能不同，未验证；切语言后主窗口标题刷新已接，但其它已打开窗口的标题未逐一验证。
4. **中文回退字形**：headless 渲染用的是系统字体，Windows 上是微软雅黑，行高/字重观感需真机扫一眼。
5. **`RefreshSwatchStates`**（色块选中环）依赖 `Loaded` + code-behind 视觉树遍历，headless 下正常，真机首次进编辑器时值得点一下确认。
6. **最近 4 张的缩略图**：快照里是黑块（测试图生成的问题），真机上是 `ThumbnailWriter` 产出的 320px PNG，未实拍确认。
7. Windows 真机完全没有跑过（只过了编译 + windows-TFM 单测）。
