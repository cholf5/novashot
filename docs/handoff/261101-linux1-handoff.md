# LINUX-1 交接：Linux 平台层（2026-11-01）

## 1. 交付了什么

prtsc 第三平台层 `Prtsc.Platform.Linux`（X11 原生实现），5 个 commit（L1-a 互操作与抓屏 → L1-b 窗口/热键 → L1-c 剪贴板/自启 → L1-d 装配 + 构建修复）。

**测试基线：596/596**（Core 414 + Mac 76 + App 63×2TFM + Linux 44），build 0 警告 0 错误。

## 2. 实现要点（按 Core 接口）

| 接口 | Linux 实现 | 关键决策 |
|---|---|---|
| `ICaptureService` | `LinuxCaptureService` | XGetImage 从**根窗**抓（合成器已把最终画面画在根窗上）；fullscreen=虚拟桌面包围盒一次抓；window=XComposite NameWindowPixmap 优先（可抓被遮挡窗），无合成器回退根窗矩形拷贝 |
| 显示器 | XRandR `XRRGetScreenResourcesCurrent` + CRTC/Output | 几何即物理像素（X11 与 Windows 虚拟桌面同构）；DPI 从毫米尺寸推、吸附 96×{1,1.25,1.5,1.75,2} ±8dpi，非标 DPI 退 1.0；primary 在前排序（对齐 Mac） |
| `IWindowService` | `LinuxWindowService` | EWMH `_NET_CLIENT_LIST`（Z 序前台在前）+ `XQueryTree` 回退（无 EWMH 极简 WM）；过滤 unmapped/hidden/超小(60×40)/自身(WM_CLASS=prtsc)；标题回退 WM_CLASS；激活走 `_NET_ACTIVE_WINDOW` client message |
| `IHotkeyService` | `LinuxHotkeyService` | XGrabKey + 隐藏 1×1 事件窗 + 后台泵线程（X 连接 fd poll + self-pipe 取消）；**NumLock/CapsLock 修饰变体四连抓**（经典坑）；keycode 反查 keysym 带 per-keysym 缓存；分发前掩掉 Mod2/Lock 再匹配 |
| `IClipboardService` | `LinuxClipboardService` | **进程外 xclip 代持**（`-l 1` 读一次即释放，避免自维护 selection 所有权线程）；PNG 走 image/png target；xclip/xsel 都不在 PATH → 全部安全降级 false/null + warning |
| `IAutostartService` | `XdgAutostartService` | freedesktop Autostart `~/.config/autostart/prtsc.desktop`（GNOME/KDE/XFCE 通用）；Exec 引号规则（空格/引号/反斜杠才包+转义）；SyncPath 与 launchd 同语义 |

## 3. TFM 与装配架构（关键变化）

- App 保持双 TFM（`net10.0-windows10.0.19041.0` + `net10.0`），**Linux 与 mac 共用裸 net10.0**：
  - `Prtsc.App.csproj`：非 windows TFM 同时引用 Platform.Mac **和** Platform.Linux
  - `AppServices.AddPrtscPlatform()`：**运行时**按 `OperatingSystem.IsMacOS()/IsLinux()` 分流装配；两个平台程序集都在包内，但只实例化命中的那套（X11 interop 不碰就不会 dlopen）
- `IXLibApi` 门面：服务层零 P/Invoke，测试全走 Fake（`FakeXLibApi`，像素源按**全局虚拟桌面坐标**回调）

## 4. 坑

1. **Directory.Build.props 分组事故（bd20e87 修复）**：GAP-1 批1 给 Linux 项目加 NoWarn 时误把整个公共 PropertyGroup 套进了 `Condition='Prtsc.Platform.Linux'` —— `EnableWindowsTargeting`/`Version` 等全部基础属性只对 Linux 生效，其它项目突然 NETSDK1100 编不过。教训：往 Directory.Build.props 加条件组时**只加新属性，不动现有属性的位置**。
2. **XGetImage 返回的 XImage 是 0 基坐标**：不要拿请求矩形的全局 X/Y 再当 origin 逐像素读（会双重偏移，测试 `CaptureRegion_CropsRequestedRect` 抓住过）。
3. **XImage 字段无访问函数**（Xlib 全是宏）：按结构体偏移直读（width@40 / bytes_per_line@72 / bits_per_pixel@76 / masks@80-88 / data@96）， decades 稳定但跨位宽要注意。
4. **Xlib Status 返回 int 不是 bool**：`XGetWindowAttributes`/`XCompositeNameWindowPixmap`/`XQueryTree` 都要 `== 0 / != 0` 比较。
5. **xclip 语义**：写入必须 `-l 1`（否则 xclip 常驻代持、每写一次泄漏一个进程）；读 target 用 `-t image/png`。
6. **XGrabKey 必须抓 NumLock/CapsLock 变体**：只抓裸组合，开 NumLock 后热键静默失效。

## 5. 已知限制（LINUX-2 候选）

- **Wayland 原生会话**：XWayland 下只能抓 X11 客户端、看不到 Wayland 原生窗；`wlr-screencopy`（Sway/Hyprland）与 org.freedesktop.portal.Screenshot（GNOME）留待后续
- **全局热键在 Wayland 无解**：XGrabKey 只作用于 X server；Wayland 需 compositor 协议支持
- **托盘**：Avalonia 11+ StatusNotifierItem/AppIndicator 内置，未实测
- **本机未真机验证**（开发机是 macOS）：冒烟清单见 §6，等 Linux 机器过一遍

## 6. Linux 真机冒烟清单（QA-L1，待领）

- [ ] `dotnet publish src/Prtsc.App -c Release -r linux-x64 --self-contained` 出产物
- [ ] X11 会话（Xorg）启动到托盘，三热键注册成功（日志确认）
- [ ] 全屏/区域/窗口三捕获 + 选框标注 + 导出 PNG
- [ ] xclip 装与不装两种环境的剪贴板行为（后者应有 warning 且不崩）
- [ ] 自启 Enable → 重登验证 → Disable
- [ ] 多显示器（含负坐标左屏）拼接正确
- [ ] KDE 与 GNOME 各过一遍（托盘/自启差异）

## 7. 测试统计

Linux 44：Capture 8 + Geometry 8 + Window 4 + Hotkey 6+3 + Autostart 7 + DI 2 + （Fake 6）
