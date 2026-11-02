# Prtsc 视觉重设计（2026-11-02）

> 稿：`docs/design/2026-11-02-ui-redesign.html`（浏览器直接打开，含 4 张对比稿 + token 表）
> 快照：`artifacts/ui/*.png`（headless 渲染，`PRTSC_SNAPSHOT=1 dotnet test --filter UiSnapshot` 生成）

## 判断

截图工具是「照片外面的画框」。UI 抢戏，内容就毁了。

所以整界面压成中性灰的仪器面板：

1. **画布衬底绝对中性**（`#141416`，不带蓝调）——旁边一片蓝黑会让用户误判截图本身的颜色。
2. **界面没有品牌色**。选中态用白色填充，其余全是中性灰；界面上唯一鲜艳的东西，是用户此刻要画上去的那个标注颜色。
3. **数字一律等宽**（尺寸、px、倍率、时间），文字用 Plex Sans。读数是这台仪器最常被看的东西。
4. **不跟 WinShot 的紫青渐变玻璃**：它和用户的红色标注、和截图本身抢注意力；而且 blur 在 Win/mac/Linux 上表现不一致。

## Token

| 用途 | 值 |
|---|---|
| Canvas mat | `#141416` |
| Window | `#1B1B1E` |
| Panel | `#212125` |
| Dock / raised | `#232328` / `#27272C` |
| 发丝线 | `#12FFFFFF` / 强调线 `#20FFFFFF` |
| 文字 | `#ECECEF` / `#9B9BA3` / `#6A6A73` |
| 主操作 | `#EDEDEF` 填充 + `#151517` 文字 |
| 危险 | `#E8837E` · 成功 `#4ADE9B` |
| 圆角 | 5 / 7 / 11 |

字体：IBM Plex Sans（400/500/600）+ Plex Mono（400），随包分发（`Assets/Fonts`，OFL）。中文走系统字体回退（PingFang SC / 微软雅黑 / Noto）。

## 结构改动（不只是换皮）

| 位置 | 之前 | 现在 | 原因 |
|---|---|---|---|
| 编辑器工具栏 | 顶部 44px 长条，15 个 Fluent 单选按钮 | 画布底部悬浮 dock，图标 + 热键字母 | 原布局在 1200px 下溢出，Save/Copy/Upload 被裁掉 |
| 右侧面板 | 一列 ~1400px 高的控件 | `Style` / `Canvas` 两个 tab | 标注时不用滚过背景/圆角/阴影 |
| 顶部 | 占位黑条 + 英文 Minimize to tray | 删掉占位条，原生标题栏下直接是真 header；主操作收敛成 Copy（主）+ Save + ⋯ | 占位条是调试残留 |
| 主窗口空闲态 | 「Capture」+ 三个灰按钮 | 一列命令（图标/说明/键帽）+ 最近 4 张 | 键帽直接教热键；最近截图是最常用的入口 |
| Library | 默认 ListBox | 行卡片 + 上传状态 chip + 底部操作条 | 主操作（打开图片）要显式 |

## 多语言（zh-CN / en）

- 文案表：`src/Prtsc.App/Localization/Loc.cs`（`Strings.En` / `Strings.Zh`）。
- XAML 用 `{DynamicResource L.Capture.Title}`；代码用 `Loc.T("Capture.Title")`。
- 切语言 = 把整张表按 `L.` 前缀重写进 `Application.Resources`，DynamicResource 自己刷新，窗口不重建。
- 入口：托盘菜单 → 语言（跟随系统 / English / 简体中文），持久化到 `config.json` 的 `language`。
- 缺 key 时回落英文，再回落 key 本身（开发期一眼看出漏翻）。

## 验证方式

`tests/Prtsc.App.Tests/UiSnapshotTests.cs`：用 Avalonia headless + Skia 把真实窗口（含真主题、真 DataContext）渲染成 PNG 到 `artifacts/ui/`。

```bash
PRTSC_SNAPSHOT=1 dotnet test tests/Prtsc.App.Tests --filter UiSnapshot
```

它默认不参与常规测试（会 `SetupWithoutStarting` 一个全局 Avalonia 实例，和 `HeadlessUnitTestSession` 共存会互相踩）。

## 遗留

- 设置中心（M8）还没做：语言目前只在托盘菜单里切。
- 只做深色（`RequestedThemeVariant="Dark"`），浅色主题留待需要时再评估。
- Slider 只画「槽 + 圆点」，没有已填充段：Avalonia 12 的 `Track` 不给 Decrease/Increase 按钮分配宽度（实测 0），要填充段得自己算布局。
