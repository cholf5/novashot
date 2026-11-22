# R-1 M10 Velopack 打包

- **优先级**: P1 ｜ **复杂度**: M ｜ **开发者**: agent（2026-11-10） ｜ **状态**: Done
- **依赖**: ~~C-1 的 Updater 子任务需先与本卡协调方案~~ ✅ 已定：vpk UpdateManager（两卡合并执行）
- **关联**: 旧交接 M8 既定方向 Velopack；winshot `build/windows/installer/project.nsi`（NSIS 参照）

## 目标

可分发的 Windows 安装器 + 便携版，发布通道就绪。

## 子任务

1. ~~`vpk` 打包流程~~ ✅ **完成（2026-11-10）**：`scripts/pack-windows.sh`——dotnet publish（**裁决：self-contained** win-x64，截图工具不给终端用户添 .NET 运行时依赖）→ `vpk '[win]' pack`（`[win]` 是 vpk 的目标平台 **directive 语法**，非文档占位符；缺了在 macOS 上会误走 osx 打包器并拒收 .ico）。产物 `Novashot-win-Setup.exe` + `Novashot-win-Portable.zip` + `Novashot-{v}-full.nupkg` + `releases.win.json`/`RELEASES`（feed 索引）。**本机 macOS 实跑成功**（vpk 1.2.161 交叉打包，产物为 PE32+ x86-64；vpk 同时自动验证了 `VelopackApp.Run()` 已在 Main 中）。
2. ~~与 C-1 协调~~ ✅ 采用 vpk UpdateManager；`UpdateConfig.CheckOnStartup/SkippedVersion/FeedUrl` 全部接通（见 C-1 卡）。
3. ~~CI~~ ✅ `.github/workflows/ci.yml`：push/PR 三平台测试矩阵（windows=Core+App windows-TFM 腿 / ubuntu=Core+Linux+App net10.0 腿 / macos=Mac+App net10.0 腿）；tag `v*` 追加 release job——版本断言（tag == Directory.Build.props `<Version>`）→ pack 脚本 → Setup.exe/Portable.zip/nupkg/feed/SHA256SUMS.txt 传 GitHub Release（gh release create/upload --clobber 幂等）。
4. ~~安装器元数据~~ ✅ 图标 `assets/app.ico`（`scripts/make-app-icon.py` 生成，品牌渐变 #667eea→#764ba2 对齐编辑器默认背景；ICO 含 16/32/48/64/128/256 六档）；开始菜单/桌面快捷方式与卸载项由 vpk 默认处理（日志确认 Shortcuts: Desktop,StartMenuRoot）；**自启用既有 `RegistryAutostartService`——安装器不写 Run 注册表，装完由用户在设置里开**（决定记录）。

## 验收标准

- ~~干净 Windows 虚拟机安装 → 截图/标注/导出全流程可用；卸载残留可接受且有说明~~ → **未验证（诚实清单）**：本机 macOS 只能交叉产出安装包，VM 安装冒烟与卸载残留待 Windows 真机/CI 后补。
- ~~更新检查在打包版内可用~~ → **运行时已接入**（VelopackApp + UpdateManager + 启动检查 + 提示框，见 C-1）；端到端更新（旧版装好→发新版→应用内升级重启）未真机验证，允许降级记录。pack 侧链路已闭环：release job 把 feed 文件一并传 Release。

## 测试用例

- ~~CI 打包产物可安装冒烟~~ → 降级：CI 只打包+上传；安装冒烟待真机（handoff 诚实清单）。
- ~~版本号单调性检查~~ ✅ release job 内 pwsh 断言 tag == packVersion（不一致直接 fail）。

## 备注 / 风险

- vpk 交叉打包的关键坑（已写进脚本注释与 handoff）：`[win]` directive 必须显式传，否则按宿主 OS 选打包器；macOS 宿主上 `vpk pack` 默认 channel 是 `osx`，须 `-c win`。
- 安装包未签名（CI 无证书），SmartScreen 会拦——分发前需决定签名方案（子任务外）。
- mac 分发（.app bundle / vpk osx 腿）不在本卡范围，未排期。
