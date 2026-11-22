<div align="center">

<img src="assets/app-icon.png" alt="Novashot" width="120"/>

# Novashot

**flameshot 式的跨平台截图 / 标注 / 分享工具**

截图 → 就地标注 → 一步合成落盘、进剪贴板或上传
常驻托盘 · 全局热键随叫随到

*A flameshot-style screenshot and annotation tool for Windows, macOS and Linux (X11).*

[![CI](https://github.com/cholf5/novashot/actions/workflows/ci.yml/badge.svg)](https://github.com/cholf5/novashot/actions/workflows/ci.yml)
[![License: AGPL v3](https://img.shields.io/badge/License-AGPL_v3-blue)](LICENSE)
![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20macOS%20%7C%20Linux-6b5eed)
![.NET](https://img.shields.io/badge/.NET-10-512BD4)
![Avalonia](https://img.shields.io/badge/Avalonia-12-9b4ee8)

[下载安装包](https://github.com/cholf5/novashot/releases/latest) · [用户手册](docs/user/README.md) · [路线图](docs/roadmap.md)

</div>

## 特性

**捕获**
- 全屏 / 区域 / 窗口三种捕获，全局热键默认 `PrintScreen` / `Ctrl+PrintScreen` / `Ctrl+Shift+PrintScreen`
- 窗口选择器带实时缩略图（96×54），不用猜窗口标题
- 延时截图（托盘 3s / 5s 或设置页默认值）+ 倒计时角标，倒计时期间按热键即可取消
- 截图启动器：指定全屏/区域、几何、延时后一键开拍
- 跨屏选框：混合 DPI 双屏跨屏拖选，选框在两屏各自切片渲染，确认后拼接

**标注（15 个工具）**

| 工具 | 热键 | 工具 | 热键 |
|---|---|---|---|
| 选择/移动 | `V` | 序号 | `N` |
| 矩形 | `R` | 聚光灯 | `S` |
| 椭圆 | `E` | 铅笔 | `P` |
| 箭头（曲线） | `O` | 荧光笔 | `H` |
| 箭头（直线） | `A` | 马赛克 | `M` |
| 直线 | `L` | 模糊 | `B` |
| 文字 | `T` | 反色 | `I` |
| 裁剪 | `X` | | |

- 8 手柄缩放、`Ctrl` 八方向 / `Shift` 正交约束、`Ctrl+滚轮` 调描边宽度与效果强度
- 非破坏裁剪 + 比例预设 + 裁剪后继续拖手柄 reshape
- 撤销 / 重做 50 步，`Delete` 删除选中对象

**合成与输出**
- 24 套渐变背景 + 纯色 / 无背景（透明预览显示棋盘格）+ 自动取边缘色
- Padding、圆角、阴影、内衬、边框（外/中/内）、输出比例、开箱即用的编辑器默认值记忆
- 导出 PNG / JPEG / WebP；`Ctrl+S` 导出、`Ctrl+Q` 快存（默认 `~/Pictures/Novashot`）、`Ctrl+C` 复制
- 剪贴板双向：读 DIBV5 / DIB / PNG，写 DIBV5 + DIB + PNG（>4MP 跳过大图 PNG 编码），透明背景不拍平
- `Ctrl+P` 贴图窗（置顶、可拖动/滚轮缩放）

**云与历史**
- 上传到 Cloudflare R2（S3 兼容）或 Google Drive，一键得到分享链接
- Library 截图历史：缩略图、打开、复制链接、删除（可选连磁盘文件一起删）

**其他**
- CLI：`novashot full|region|window|gui --delay --path --clipboard --format`，单实例自动转发给已运行实例
- 设置中心 GUI：热键录制、自启、托盘行为、QuickSave、导出默认值、编辑器默认值、捕获开关、语言、更新检查
- 简体中文 / English 双语，跟随系统或手动切换
- 桌面通知（Windows toast）、Velopack 应用内更新

## 平台支持

| 能力 | Windows 10/11 | macOS | Linux (X11) |
|---|---|---|---|
| 抓帧 | Windows Graphics Capture | CoreGraphics | XGetImage（XRandR 多屏含负坐标） |
| 窗口捕获 | ✔ | ✔ | ✔（XComposite） |
| 全局热键 | ✔ | ✔（无需辅助功能权限） | ✔（XGrabKey） |
| 剪贴板 | ✔ | ✔（NSPasteboard） | ✔（依赖 `xclip`） |
| 开机自启 | ✔（注册表 Run） | ✔（launchd） | ✔（XDG Autostart） |
| 桌面通知 | ✔ | —（静默，接口预留） | —（静默，接口预留） |
| 安装包 | Setup.exe / Portable.zip | `.app`（zip） | 源码构建 |

**已知限制**（完整清单与绕法见 [`docs/release/known-limitations.md`](docs/release/known-limitations.md)）
- **产物没有代码签名**：Windows SmartScreen 提示「未知发布者」→「更多信息 → 仍要运行」；macOS 提示
  「无法验证开发者」，最省事是 `xattr -cr /Applications/Novashot.app`（macOS 26 上右键「打开」已失效）
- **Wayland**：仅在 XWayland 会话下可用，原生 Wayland 截图协议未支持（看板 LINUX-2）
- **macOS 需要屏幕录制权限**：系统设置 → 隐私与安全性 → 屏幕录制，勾选后重启应用
- **只有 Windows 有系统级通知与应用内更新**；macOS / Linux 换版本要手动替换，Linux 本版只发源码构建

## 安装

> [!TIP]
> 到 [Releases](https://github.com/cholf5/novashot/releases/latest) 页下载对应平台的安装包。

**Windows**

1. 从 Releases 下载 `Novashot-win-Setup.exe`（安装版）或 `Novashot-win-Portable.zip`（便携版）
2. 安装后从开始菜单启动，程序常驻托盘
3. 需要更新时在「设置 → 常规 → 更新 feed 地址」填入 feed 根地址（形如 `https://github.com/<owner>/<repo>/releases/latest/download/`），之后启动时自动检查

便携版不检查更新（没有 `Update.exe`，属预期行为）。

**macOS**

从 Releases 下载 `Novashot-<版本>-osx-arm64.zip`（Apple Silicon）或 `-osx-x64.zip`（Intel），解压得到
`Novashot.app`，拖进「应用程序」。产物是 ad-hoc 签名、未公证，首次打开会被 Gatekeeper 拦一次：

```bash
xattr -cr /Applications/Novashot.app     # 抹掉隔离标记，之后双击直接开
```

然后授权「屏幕录制」（系统设置 → 隐私与安全性 → 屏幕录制 → 勾选 Novashot），重启应用即可按热键截图。
macOS 版不参与应用内更新：换版本 = 退出 → 替换 `.app` → 重开。

**Linux**

本版只发源码构建，按[从源码构建](#从源码构建)自行构建；先装 `xclip`：

```bash
sudo apt install xclip        # Debian/Ubuntu
sudo dnf install xclip        # Fedora
```

## 快速上手

1. **启动**：程序常驻托盘，主窗口是命令列表（全屏 / 区域 / 窗口 / 启动器）+ 最近 4 张截图
2. **截图**：按 `PrintScreen` 全屏，或 `Ctrl+PrintScreen` 拉选框 → `Enter` 确认进入编辑器
3. **标注**：工具坞在底部，单字母热键切换；右侧面板切 Style / Canvas 调样式与合成参数
4. **输出**：`Ctrl+C` 复制 / `Ctrl+S` 导出 / `Ctrl+Q` 快存 / `Ctrl+U` 上传

**编辑器快捷键**

| 快捷键 | 作用 |
|---|---|
| `Ctrl+Z` / `Ctrl+Y` / `Ctrl+Shift+Z` | 撤销 / 重做 |
| `Ctrl+C` / `Ctrl+V` | 复制到剪贴板 / 从剪贴板粘贴 |
| `Ctrl+S` / `Ctrl+Q` | 导出（对话框）/ 快存到默认目录 |
| `Ctrl+U` / `Ctrl+L` / `Ctrl+P` | 上传 / 打开历史 / 贴图 |
| `Ctrl+O` | 导入本地图片（空闲态也可用） |
| `Delete` / `Backspace` | 删除选中标注 |
| `Esc` | 取消当前拖拽 → 取消选中 → 退出编辑器 |

**选区叠加层（overlay）**

| 按键 | 作用 |
|---|---|
| 拖动 / 8 手柄 | 拉框 / 缩放，`Ctrl` 八方向吸附、`Shift` 正交 |
| `Enter` | 确认选框（无选框 = 整屏） |
| `Esc` | 清除选框；再按一次退出捕获 |
| `C` | 切换屏幕取色模式（放大镜 + HEX/RGB，点击即复制） |

## CLI

CLI 与 GUI 共用同一份配置和平台层；已有实例在跑时，命令会整条转发给该实例（不启新进程）。

```bash
novashot                                  # 无参数 = 启动 GUI（已有实例则前置窗口）
novashot gui                              # 同上
novashot full                             # 全屏截图（无 GUI 也可 headless 执行）
novashot full --path ~/Pictures           # 存到指定目录（文件名按配置的命名模式）
novashot full --clipboard                # 复制到剪贴板
novashot region --geometry 0,0,1280,720  # 区域截图（物理像素，虚拟桌面坐标）
novashot window                           # 窗口截图（需要已运行的 GUI 实例弹选择器）
novashot full --delay 3 -f jpeg -p /tmp   # 延时 3 秒 + JPEG
novashot --help                           # 完整用法
```

| 选项 | 说明 |
|---|---|
| `-d, --delay <秒>` | 抓帧前等待，0–60 钳制；省略 = 交互模式取设置里的默认延时 |
| `-p, --path <目录>` | 落盘目录，不存在会自动创建 |
| `-c, --clipboard` | 复制到剪贴板（`--path` 与 `--clipboard` 都不给时默认走剪贴板） |
| `-f, --format <png\|jpeg\|webp>` | 导出格式，默认 png |
| `--geometry X,Y,W,H` | 仅 `region` 模式；物理像素，X/Y 可为负（多屏） |
| `-h, --help` | 打印帮助 |

退出码：`0` 成功 / `1` 失败 / `2` 用法错误。`window` 与不带 `--geometry` 的 `region` 属交互模式，没有运行中的 GUI 时会明确报错而不是偷偷拉起窗口。

## 配置文件与数据目录

Windows 在 `%AppData%\Novashot\`，macOS 在 `~/Library/Application Support/Novashot/`，Linux 在 `~/.config/Novashot/`：

| 文件/目录 | 内容 |
|---|---|
| `config.json` | 全部设置（热键、启动、QuickSave、导出、捕获、编辑器默认值、云凭据、更新、语言） |
| `library.json` | Library 历史索引 |
| `Thumbs/` | Library 缩略图 |
| `logs/novashot-YYYYMMDD.log` | 运行日志（按天滚动，保留 14 天） |

设置中心覆盖了几乎全部字段，改 JSON 只在前置配置或排查时用；手改后重启生效。云凭据明文存在 `config.json` 中，请自行注意文件权限。

## 从源码构建

需要 [.NET SDK 10](https://dotnet.microsoft.com/)；UI 为 Avalonia 12。

```bash
git clone <repo> && cd novashot
dotnet build Novashot.slnx                 # 全解决方案
dotnet run --project src/Novashot.App      # 启动（默认 TFM 随宿主平台）
```

按平台显式指定目标：

```bash
dotnet run --project src/Novashot.App -f net10.0-windows10.0.19041.0   # Windows
dotnet run --project src/Novashot.App -f net10.0                        # macOS / Linux
```

**打包**

```bash
dotnet tool install --global vpk        # Windows 打包需要（vpk 也可在 macOS/Linux 上交叉打包）
scripts/pack-windows.sh                 # 版本号取 Directory.Build.props
scripts/pack-windows.sh --version 1.0.0 # 或显式指定
scripts/pack-macos.sh                   # macOS .app（本机架构）；--rids "osx-arm64 osx-x64" 两个架构
```

Windows 产物在 `artifacts/pack/`：`Novashot-win-Setup.exe`、`Novashot-win-Portable.zip`、`*.nupkg`、
`releases.win.json` / `RELEASES`（更新 feed）。macOS 产物是同目录的 `Novashot-<版本>-osx-<arch>.zip`
（`.app` bundle + ad-hoc 封签）。发版流程与资产契约见 [`docs/release/ci-release.md`](docs/release/ci-release.md)。

## 测试

```bash
dotnet test tests/Novashot.Core.Tests       # 785 例（纯逻辑：几何/合成/导出/配置/CLI 协议…）
dotnet test tests/Novashot.App.Tests -f net10.0   # 212 例（headless UI 接线；windows TFM 腿 218 例）
dotnet test tests/Novashot.Mac.Tests        # 91 例（含真粘贴板 Manual 用例）
dotnet test tests/Novashot.Linux.Tests      # 46 例（Fake X11）
```

视觉快照（把真实窗口渲染成 PNG 到 `artifacts/ui/`，供人工核对主题）：

```bash
NOVASHOT_SNAPSHOT=1 dotnet test tests/Novashot.App.Tests -f net10.0 --filter UiSnapshot
```

CI（`.github/workflows/ci.yml`）在 Windows / Linux / macOS 三平台跑对应测试腿，test-linux 末尾还会在 Xvfb
里真抓一帧做启动冒烟。推 `v*` tag 会校验 `tag == Directory.Build.props <Version>`，然后打 Windows / macOS 包、
跑安装器与封签冒烟，再建 Release 并上传。发版走一条命令：

```bash
scripts/release.sh 1.0.0 --watch        # bump 版本 → tag → push → 盯 CI 与资产
gh workflow run ci.yml                  # 或者先做一次打包 dry-run（不建 Release）
```

## 项目结构

```
src/Novashot.Core             纯逻辑：标注模型、合成、导出、配置、Library、云上传、CLI 协议与执行器
src/Novashot.App              Avalonia UI：主窗口、overlay、编辑器、设置中心、托盘、CLI 编排
src/Novashot.Platform.Windows  WGC/GDI 抓帧、窗口枚举、热键、剪贴板、自启、toast
src/Novashot.Platform.Mac      CoreGraphics/CGWindowList、Carbon 热键、NSPasteboard、launchd
src/Novashot.Platform.Linux    X11 抓帧、EWMH、XGrabKey、xclip、XDG Autostart
tests/                     Core / App（headless）/ Mac / Linux 四套测试
docs/                      设计、handoff、QA 清单、用户手册
kanban/                    看板与卡（board.md + issues/）
```

## 文档

- 用户手册：[`docs/user/`](docs/user/README.md)（捕获、标注、合成导出、云与历史、设置、CLI、故障排查）
- 路线图与里程碑：[`docs/roadmap.md`](docs/roadmap.md)
- 开发看板：[`kanban/board.md`](kanban/board.md)
- 真机冒烟清单：[`docs/qa/261102-smoke-checklist.md`](docs/qa/261102-smoke-checklist.md)
- 发版流程与资产契约：[`docs/release/ci-release.md`](docs/release/ci-release.md)；已知限制：[`docs/release/known-limitations.md`](docs/release/known-limitations.md)
- 历次交接与踩坑记录：[`docs/handoff/`](docs/handoff/)

## 许可

[AGPL-3.0-or-later](LICENSE)。改名再分发请更换品牌标识。

随包分发的 IBM Plex 字体按 SIL OFL 授权，见 `src/Novashot.App/Assets/Fonts/OFL.txt`。
