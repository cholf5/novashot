# 07 故障排查

先看日志：Windows `%AppData%\Novashot\logs\`、macOS `~/Library/Application Support/Novashot/logs/`、Linux `~/.config/Novashot/logs/`，按天滚动、保留 14 天（`novashot-YYYYMMDD.log`）。报问题时带上最后一段日志与系统版本最省时间。

## 截图是黑的 / 抓不到内容

- **macOS**：没给「屏幕录制」权限。系统设置 → 隐私与安全性 → 屏幕录制 → 勾上 Novashot 后**重启 Novashot**（授权后必须重开进程）。未授权时日志与界面都会给出这条指引。
- **Linux**：没有 `DISPLAY`（纯 Wayland 会话）或 `DISPLAY` 指向不可用的 X server。见下方「Wayland」。
- **Wayland（GNOME/KDE 原生会话）**：当前只支持 XWayland。检查 `echo $XDG_SESSION_TYPE`，若为 `wayland`，登录界面改用「Xorg / X11 会话」，或暂时用系统自带截图工具。原生 Wayland 支持见看板 LINUX-2。
- **Windows**：受 DRM 保护的内容（部分播放器、Netflix）系统层面禁止抓取，任何截图工具都拿不到。

## 全局热键没反应

1. 有些应用会独占 `PrintScreen`（截图工具、输入法、录屏软件），换一个组合：设置 → 热键 → 点该行 → 按新组合 → 保存
2. **Linux**：另一个进程已经 `XGrabKey` 了同一个组合时会注册失败，日志有 warning；换组合即可
3. **macOS**：热键走 Carbon，不需要辅助功能权限；但若组合被系统快捷键占用（系统设置 → 键盘 → 键盘快捷键），先在那里让位
4. 保存后仍无效：完全退出程序（托盘 → 退出）再启动，确认不是残留进程注册着旧组合

## Linux 剪贴板不通

Novashot 的 X11 剪贴板依赖 `xclip`。没装时所有剪贴板操作会安全降级（静默失败、日志 warning）：

```bash
which xclip || sudo apt install xclip     # 或 dnf install xclip
```

另外 X11 剪贴板由 Novashot 进程持有：**退出程序会清掉剪贴板里的图**（X11 固有语义）。需要长期保存请落盘或上传。

## Windows 首次运行被 SmartScreen 拦

产物未签名（签名方案未定），属预期：点「更多信息 → 仍要运行」。企业环境可用哈希白名单或自行重签。

## 系统通知不弹

- 只看 Windows：macOS/Linux 目前装配的是空实现（静默），这是已知限制
- 检查 设置 → 常规 → 截图完成通知 是否被关
- 系统侧：专注助手 / 勿扰 / 通知权限关闭时 toast 会被系统吞掉
- 安装版才用 AUMID `Novashot`；源码 `dotnet run` 场景下 AUMID 未注册 → 只记日志不弹窗（预期降级）。排查可跑 `Get-StartApps | findstr Novashot` 看 AUMID 是否存在

## 应用内更新一直没提示

1. 未打包运行（源码跑）时 `UpdateManager.IsInstalled` 恒 false → 永不检查，属预期
2. 便携版（Portable.zip）没有 `Update.exe`，同样不检查
3. **feed 地址必须指到 `releases.win.json` 所在目录**：正确形态是 `https://github.com/<owner>/<repo>/releases/latest/download/`；填仓库主页或 `/releases` 页面会 404 且静默失效
4. feed 为空时启动不检查（也不写日志噪音）
5. 检查「跳过此版本」（设置里记录的 skippedVersion）——跳过后该版本不再提示

## 粘贴不进来 / 粘出去别人看不到

- 粘贴进来失败：先用 `Ctrl+O` 导入本地图片绕过（某些应用只放私有剪贴板格式）
- 粘出去别人看不到：确认对方支持 DIB/PNG；浏览器/Electron 应用一般没问题，个别老软件只认 CF_BITMAP
- 超大图（>4MP）时写入路径会跳过 PNG 格式以保 UI 流畅，此时按 DIB 走——只认 PNG 的读者可能拿不到，属设计取舍

## 延时截图拍到了倒计时角标

不应该发生：抓帧在角标关闭后 250ms 才执行。若真遇到，记下时间点与日志（`DelayedCapture` 相关行）反馈。

## 托盘图标消失 / 双击没反应

- Windows：图标被折叠进溢出区，拖回收纳栏或改系统设置
- 关窗隐藏到托盘的语义下，「退出」只在托盘菜单里；找不到图标时任务管理器结束 `Novashot.exe` 后重启

## 配置被写坏 / 想重置

退出程序，把 `config.json` 改名（如 `config.json.bak`），重启会生成默认配置。Library 历史同理删 `library.json` + `Thumbs/`。

## 反馈问题时请提供

1. 系统与版本（Windows 11 23H2 / macOS 14.5 / Ubuntu 24.04 + X11 或 Wayland）
2. Novashot 版本（托盘悬浮提示或设置页）
3. 复现步骤（越短越好）
4. 对应时间段的日志片段
