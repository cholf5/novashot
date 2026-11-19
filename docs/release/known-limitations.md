# 已知限制（v1.0.0 首发）

> 发布时点**已知、明知不修或排在后面**的问题。每条写清：用户看到什么、什么条件下撞上、现在怎么绕。
> 原则：宁可写成文字，也不假装没有——首版的目标是「能装、能用、坑说清楚」，不是「没有坑」。
>
> 发版流程见 [`ci-release.md`](ci-release.md)；真机冒烟清单见 [`../qa/261102-smoke-checklist.md`](../qa/261102-smoke-checklist.md)。

## 一、分发与安装

### 1. 所有产物都没有代码签名

**症状**：Windows 装 Setup.exe 时 SmartScreen 提示「未知发布者」；macOS 首次打开 `.app` 提示
「无法验证开发者」。部分企业环境或杀软会直接拦下载/启动。

**原因**：没有 Windows EV 证书，也没有 Apple 开发者账号（都是付费 + 实名）。macOS 产物只做了 ad-hoc
签名——它保证的是「不会报『已损坏』」，不是「受信任」。

**现在怎么绕**：

- **Windows**：SmartScreen →「更多信息」→「仍要运行」。
- **macOS 26（Tahoe）已经没有「右键 → 打开」这条捷径了**——右键打开会直接告诉你用不了。最省事的是
  终端抹掉隔离标记，之后双击直接过，不分系统版本：

  ```bash
  xattr -cr /Applications/Prtsc.app
  ```

  图形界面路线：系统设置 → 隐私与安全性 → 滚到最下面 →「仍要打开」→ 输开机密码 → 再双击确认一次。
- 不想碰 Gatekeeper：直接从源码跑 `dotnet run --project src/Prtsc.App`。

**后续**：买证书后接进 `pack-windows.sh` / `pack-macos.sh`（macOS 还需公证 + staple）。付费前提，不是代码问题。

### 2. macOS 首次截图必须先授权「屏幕录制」，且授权后要重启应用

**症状**：第一次按热键没反应，或提示需要屏幕录制权限；在系统设置里勾上之后仍然抓不到。

**原因**：macOS 的屏幕录制是 TCC 权限，按「可执行文件的签名身份 + 路径」记账；未签名的副本换路径/换版本
会被当成新应用重新询问。授权状态的变更也要进程重启才生效。

**现在怎么绕**：系统设置 → 隐私与安全性 → 屏幕录制 → 勾选 Prtsc → **退出并重启 Prtsc**。
换了 `.app` 位置之后可能再问一次。

### 3. macOS / Linux 没有安装器，也没有应用内更新

**症状**：Windows 装完能在应用内检查更新并一键升级；mac/Linux 只有压缩包，换版本要手动替换。

**原因**：应用内更新走 Velopack 的 `UpdateManager`，它要求「由 vpk 安装器装出来的副本」
（`IsInstalled` 为真）。macOS 腿为了让下载件能过 Gatekeeper 只做 ad-hoc 封签，不产 Velopack 包；
Linux 本版不发预编译包。详见 [`ci-release.md`](ci-release.md) 的「macOS 为什么要自己组 .app」。

**现在怎么绕**：macOS 下新 zip → 退出 Prtsc → 替换 `/Applications/Prtsc.app` → 重新打开。
配置文件在 `~/Library/Application Support/Prtsc/`，不受替换影响。

### 4. Linux 只发源码构建，且依赖 `xclip`

**症状**：Releases 里没有 Linux 包；自己构建后复制到剪贴板失败。

**原因**：`.deb` / `.rpm` / AppImage / snap 各有自己的依赖与仓库体系，没有真实用户需求时不值得做。
剪贴板走 `xclip` 代持（X11 的选区所有权语义决定的，不是在包里塞个库就能解决）。

**现在怎么绕**：

```bash
sudo apt install xclip                # Debian/Ubuntu
sudo dnf install xclip                # Fedora
dotnet publish src/Prtsc.App -c Release -f net10.0 -r linux-x64 --self-contained true -o out
```

### 5. Wayland 只支持 XWayland

**症状**：纯 Wayland 会话（未启用 XWayland）下热键不响应、抓不到屏。

**原因**：Linux 平台层是 X11 实现（XGetImage / XComposite / XGrabKey）。Wayland 的截图协议
（`ext-image-copy-capture` 等）是另一套模型，且各合成器支持不一。

**现在怎么绕**：登录时选「Xorg / X11 会话」，或确保 XWayland 可用（多数发行版默认开启）。
原生 Wayland 支持登记在看板 `LINUX-2`。

## 二、平台能力差异

### 6. 只有 Windows 有系统级桌面通知

**症状**：macOS / Linux 上导出、复制、上传成功都**没有**提示；Windows 上有 toast。

**原因**：`INotificationService` 在 mac/Linux 上是 `NullNotificationService`——macOS 要接
`UNUserNotificationCenter` 且需签名身份，Linux 要接 D-Bus 通知服务（`org.freedesktop.Notifications`），
两者都没做到「失败也不打扰」的稳妥程度，宁可不做也不弹半吊子提示。

**现在怎么绕**：mac/Linux 的反馈在界面上——状态栏文字、托盘 tooltip、Library 里新出现的条目。

### 7. 剪贴板大图不写 PNG 格式

**症状**：复制超过 400 万像素的图之后，粘到只认 PNG 的应用里没有内容（粘到 Word / 微信 / Chrome 正常）。

**原因**：Windows 剪贴板会同时放 DIBV5 + DIB + PNG 三种格式。PNG 编码是 UI 线程同步做的，
实测 1080p ~72ms、4K ~278ms、4K 噪声 ~992ms，所以超过 4MP 就跳过 PNG，只留 DIBV5/DIB。

**现在怎么绕**：需要 PNG 时用 `Ctrl+S` 导出文件，或用 `Ctrl+Q` 快存到 `~/Pictures/Prtsc`。

## 三、验证覆盖（诚实清单）

### 8. 真机人工冒烟还没走完

**症状**：不体现在功能上，是「我们验到哪一步」的问题。

**现状**：`docs/qa/261102-smoke-checklist.md`（QA-1，含 UI 改版后的 dock/面板/剪贴板互通/三种捕获/托盘）
与 Linux 真机项（QA-L1）里的勾选大多是空的；自动化这边是四套测试（Core 785 / App 209 双 TFM / Mac 91 /
Linux 46）加三平台 CI。

**macOS 这条腿本版验到哪**：打包脚本本机实跑通过（`.app` 启动到托盘、热键注册成功、CLI 抓帧成功、
`codesign --verify --strict` 通过），但**没有**逐项走完标注/导出/上传的人工清单。

### 9. Windows 安装与更新没在干净 VM 上端到端验过

**症状**：同样是「验到哪一步」的问题。

**现状**：CI 的安装器冒烟证明「静默装上 / 卸载干净 / exe 落盘」；**没有**证明过
「装旧版 → 发新版 → 应用内提示 → 下载 → 重启换新」这条完整链路（需要两个真实版本 + 一个干净 VM）。
这一条是 R-1 交接里明确留下的缺口。

## 四、内部小瑕疵（不影响日常使用）

- `artifacts/pack/` 不会被 `pack-windows.sh` 清理：本机反复试打同一个版本号时 vpk 会拒绝
  （`release in channel win which is equal or greater`），先 `rm -rf artifacts/pack` 再打。CI 干净 checkout 不受影响。
- 发版说明靠手写 `docs/release/notes/<tag>.md`；忘了写就会回落 `--generate-notes`，首版没有上一个 tag 时
  那是一整份提交历史。`release.sh` 在缺文件时会提前提示。
- 应用内更新检查要求用户手动填 feed 地址（`https://github.com/<owner>/<repo>/releases/latest/download/`）——
  Velopack 没有「从 GitHub 仓库名推断 feed」的能力，所以不能只填仓库地址。
