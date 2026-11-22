# CI 打包发版（novashot v1.0.0 起）

> 一句话：`scripts/release.sh 1.0.0 --watch` 一条命令发版；推 tag 之前想先验打包链路，就跑
> `gh workflow run ci.yml`——它会把打包与冒烟全跑一遍，但不建 Release（dry-run）。

## 一图流

```
scripts/release.sh 1.0.0 --watch   # 本地测试 → bump 版本 → commit → tag v1.0.0 → push → 盯 CI
  └─ CI (.github/workflows/ci.yml)
       ├─ test-windows / test-linux / test-macos   三个 runner 各跑自己平台能跑的测试腿
       │    └─ test-linux 末尾：Xvfb 启动冒烟（novashot full --path，真抓一帧落盘）
       ├─ pack-windows (windows-latest)  tag 必须等于 <Version>，否则拒绝打包
       │    ├─ scripts/pack-windows.sh   → Velopack：Setup.exe / Portable.zip / full.nupkg / feed
       │    ├─ 安装器静默装 + 卸冒烟      → 装上、确认 exe 落盘、卸干净
       │    └─ SHA256SUMS.txt（本腿三件）
       ├─ pack-macos (macos-latest)
       │    └─ scripts/pack-macos.sh     → .app bundle + ad-hoc 封签 → Novashot-<ver>-osx-<arch>.zip
       │         └─ 解压后复核封签（codesign --verify --strict）
       └─ release (ubuntu-latest)        仅 tag push
            ├─ 下载 pack-win / pack-macos 的全部产物
            ├─ 重算 SHA256SUMS.txt（覆盖全部资产）
            └─ gh release create --verify-tag（正文取 docs/release/notes/<tag>.md）
```

`workflow_dispatch`（手动触发）= 同一张图去掉最后的 `release` job：测试、打包、两处冒烟、
校验和全跑，产物留在 run 的 artifacts 里。**这是「CI 首跑会不会红」的处置办法**——不用先打 tag。

## 资产契约

| 资产 | 谁消费 | 说明 |
|---|---|---|
| `Novashot-win-Setup.exe` | Windows 用户 + 应用内更新 | Velopack 安装器；装到 `%LOCALAPPDATA%\Novashot`，带卸载器（`Update.exe`） |
| `Novashot-win-Portable.zip` | Windows 便携用户 | 解压即用；便携版不检查更新（没有 `Update.exe`） |
| `Novashot-<版本>-full.nupkg` | Velopack 更新器 | 完整包，`UpdateManager` 下载它做增量/全覆盖升级 |
| `releases.win.json` / `RELEASES` | Velopack feed | **feed 的托管点就是 Release 页面**：应用内「更新 feed 地址」填 `https://github.com/<owner>/<repo>/releases/latest/download/` |
| `Novashot-<版本>-osx-arm64.zip` / `-osx-x64.zip` | macOS 用户 | `.app` bundle，ad-hoc 封签；不参与应用内更新 |
| `SHA256SUMS.txt` | 用户 | `sha256sum -c SHA256SUMS.txt` |

改动这份清单要同步三处：`scripts/pack-windows.sh` / `scripts/pack-macos.sh`（产出）、
`.github/workflows/ci.yml`（上传）、`scripts/release.sh`（`--watch` 的核对表）。

## 版本号唯一事实

`Directory.Build.props` 的 `<Version>`（三段数字）：

- 发版 tag `v{Version}`——CI 在 tag push 时**强制**两者相等，不等就不打包；
- Velopack 包名与 `releases.win.json` 里的版本；
- macOS `Info.plist` 的 `CFBundleShortVersionString`（`pack-macos.sh` 注入）；
- 应用内更新检查的比较基线（`UpdateManager` 读 Velopack 包里的版本，与这里的 `<Version>` 同源）。

不要在各 csproj 里再写 `<Version>`：`release.sh` 只改这一处，多写一处分叉就是发布事故。

## 本地打包（发版前自查）

```bash
scripts/pack-windows.sh                       # Windows 产物 → artifacts/pack/（mac/linux 上交叉打包亦可）
scripts/pack-macos.sh                         # 本机架构的 .app → artifacts/pack/
scripts/pack-macos.sh --rids "osx-arm64 osx-x64"   # 两个架构（CI 就是这条）
```

两个脚本都会自己读 `<Version>`，也可以用 `--version 1.0.0` 显式指定。

`pack-windows.sh` 不会清理 `artifacts/pack/`：vpk 在同 channel 里发现「版本 >= 当前版本」的旧 release
会直接拒绝打包（报 `There is a release in channel win which is equal or greater...`）。本机反复试打
同一个版本号时先 `rm -rf artifacts/pack`。CI 是干净 checkout，不受影响。

## macOS 为什么要自己组 .app（不用 `vpk [osx] pack`）

vpk 的 osx 通道产出 `.pkg` 安装器（或带 `UpdateMac` 助手的 bundle）。没有 Apple 开发者证书时：

- `.pkg` 未公证 → Gatekeeper 直接判「已损坏」，**没有**「仍要尝试打开」这条逃生口；
- vpk 往 bundle 里塞 `UpdateMac` 之后，之前打的封签全部失效，而它只在你给了签名身份时才重新签。

所以 macOS 腿走 `scripts/pack-macos.sh`：整目录 self-contained publish → 组 `.app` → `.icns` 现场生成
→ ad-hoc 封签（嵌套 Mach-O 逐个封 + 整包 `--deep`） → `codesign --verify --strict` → `ditto` 打 zip。
下载件的提示从「已损坏」降级为「未验证开发者」，用户有路可走（见 [`known-limitations.md`](known-limitations.md)）。

代价：没有安装器、没有应用内更新。macOS 换版本 = 下新 zip 替换 `.app`。

## 冒烟测什么

- **Windows 安装器**：`Setup.exe --silent` → 确认 `Novashot.exe` 落盘 → 杀掉可能被 postinstall 拉起的进程
  → `Update.exe --uninstall` → 轮询等文件消失。两个硬教训：**不要用 `Start-Process -Wait`**（它等整棵
  进程树，postinstall 拉起应用后会挂到 job 超时）；卸载是延迟清理，**退出码 0 只代表已受理**，文件
  消失要轮询。
- **Linux 启动**：`dotnet publish linux-x64` → Xvfb → `xdpyinfo` 就绪探测（Xvfb 的 socket 是异步创建的，
  首连会撞空）→ `novashot full --path` 真抓一帧 → 断言退出码 0 且 PNG >1KB。抓的是 XGetImage 全链路，
  比「进程存活 20 秒」有内容。
- **macOS 封签**：打包后把 zip 真解一遍再 `codesign --verify --strict`——zip 里丢了扩展属性或符号链接的话，
  用户拿到的就是 damaged，本机验过的 stage 目录说明不了这件事。

## 发版步骤

```bash
scripts/release.sh 1.0.0            # 本地测试 → bump → tag → push
scripts/release.sh 1.0.0 --watch    # 追加：盯 CI 到完成并核对资产齐全（自带 45 分钟上限）
```

前提：在 `main`、工作树干净、与远端同步、tag 不存在。失败恢复（tag 已推、CI 挂）：

```bash
git push origin :refs/tags/v1.0.0 && git tag -d v1.0.0
# 修好之后重跑 release.sh（<Version> 已在目标版本时会跳过 bump 提交，直接重打 tag）
```

**首版必须手写 `docs/release/notes/<tag>.md`**：没有上一个 tag 时 `--generate-notes` 会把全部提交历史
抖成发版说明，读者看不到「这一版有什么」。有文件就 `--notes-file`，没有才回落。

## 发版说明与文档

- 发版说明：[`notes/`](notes/)（每个 tag 一份，手写）
- 人读的坑：[`known-limitations.md`](known-limitations.md)
- 开发约定：[`../../AGENTS.md`](../../AGENTS.md)
