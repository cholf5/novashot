# V-1 v1.0 发布收尾（版本号 / tag / CI 首跑 / 文档 / 签名 / 分发）

- **优先级**: P1 ｜ **复杂度**: M ｜ **状态**: In Progress ｜ **来源**: M11 收尾门拆解——R-1 handoff「下一步」+ roadmap M11 的「用户文档 + 发布」两项此前无卡承载

## 目标

把「代码写完了但发不出去」这段补齐：仓库现在**没有 tag、版本号还是 0.1.0、CI 从未首跑、没有 README**，用户拿到仓库无法上手，Release 页面也不存在。

## 子任务

1. **版本号**：`Directory.Build.props` `<Version>` 从 `0.1.0` 改成首个发布版本；`ci.yml` 的 release job 会断言 `tag == Version`，两边必须同步改。
2. **CI 首跑**：推远端后看 `test-windows / test-linux / test-macos` 三 job 是否绿（本地只验过 YAML 语法）。风险点：runner 上 `dotnet-version: '10.0.x'` 可用性、Windows 宿主 bash（Git Bash）跑 `scripts/pack-windows.sh`。
3. **tag + release**：打 `v*` tag → release job 出 Setup.exe / Portable.zip / nupkg + sha256 清单 + feed 文件（`releases.win.json`）。打之前先把 Q-1 的栈溢出清掉，否则 release job 的测试门可能随机挂。
4. **README**：仓库根目录**没有 README**。至少要覆盖：这是什么（flameshot 式截图/标注工具，三平台）、平台支持矩阵（Windows 全功能 / macOS / Linux X11，Wayland 仅 XWayland）、安装（Setup / Portable / 源码构建）、首次使用（热键、托盘、QuickSave 目录）、CLI 用法（`prtsc --help` 摘录）、配置位置、已知限制（未签名 SmartScreen、Wayland、mac/Linux 无系统通知）。
5. **用户文档**：roadmap M11 明写「用户文档」。可先 README + `docs/user/`（截图/标注/导出/云上传/Library/设置/CLI 各一页），不必等站点。
6. **签名方案拍板**：产物未签名 → SmartScreen 拦。选项：EV 证书 / Azure Trusted Signing / 明确「不签」并写进 README 的已知限制。
7. **macOS 分发渠道**：vpk osx 腿 / .app bundle / Homebrew，三选一或明确「本版不发 mac 包」。

**进度（2026-11-10）**

- [x] 4. README：仓库根 `README.md` 已写（特性 / 平台矩阵与已知限制 / 安装 / 快速上手 / 快捷键 / CLI / 配置与数据目录 / 构建与打包 / 测试 / 项目结构 / 文档索引）。
- [x] 5. 用户文档：`docs/user/` 八页（索引、捕获、标注、合成导出、云与历史、设置、CLI、故障排查），README 已互链。
- 顺手修正：设置页 QuickSave 三种命名模式的示例文案与实现不一致（`prtsc-20261102-094213` → 实际 `prtsc_2026-11-02_09-42-13` / `prtsc_001`），双语同步。
- [x] 6. 签名方案：**明确不签**，理由与用户绕法写进 README + `docs/release/known-limitations.md`（2026-11-20）。
- [x] 7. mac 分发：`scripts/pack-macos.sh`（`.app` + ad-hoc 封签 + zip）+ CI `pack-macos` 腿（2026-11-20）。
- [ ] 1/2/3 待远端或真机：版本号、CI 首跑、tag+release、签名、mac 分发。另需拍板 **LICENSE**（仓库当前没有，README 已如实标注）。

**进度（2026-11-20）—— 工程侧收口，剩下的是发版动作与真机**

按 Tokenmetry 已发七个版本的成熟做法搬过来（不搬 Inno Setup / Native AOT / 测试工程拆分：prtsc 已投
Velopack，AOT 与 WGC+双 TFM 无关，headless session 已踩平 Q-1）：

- **LICENSE 拍板**：AGPL-3.0-or-later（仓库根 `LICENSE` 全文；`Directory.Build.props` 加
  `PackageLicenseExpression` 作为许可证元数据唯一出处；README「许可」改为链接，字体 OFL 例外保留）。
- **警告纪律**：`TreatWarningsAsErrors=true`（当前构建 0 警告），新增 `.editorconfig` 把 `AVLN5001`
  定为 error——XAML 编译器诊断不受 TWAE 管辖，这是唯一生效通道；新增 `AGENTS.md` 记录基线纪律。
- **CI 首跑两处红已修**：test-linux 补 `libfontconfig1`（`libSkiaSharp.so` 的动态依赖，ubuntu-24.04
  没有它，报错只提 libSkiaSharp）+ `fonts-noto-cjk`；test-windows 的
  `EditorDefaultsFromConfigTests.Dispose` 改退避重试 3×200ms（500ms 防抖写盘与删临时目录撞文件锁）。
  CI 另加 paths-ignore / concurrency / 每 job timeout / `gh release create --verify-tag`。
- **发布自动化（子任务 1/3 的机制部分）**：`scripts/release.sh`（preflight → 本地测试 → 原子 bump
  `<Version>` → 中文 commit → annotated tag → push → `--watch` 盯 CI 与资产核对）、
  `docs/release/notes/v1.0.0.md`（首版必须手写，否则 `--generate-notes` 抖全部历史）、
  release 正文优先 `--notes-file`。**版本号仍未改**：`0.1.0` 的 bump 由 `release.sh 1.0.0` 在打 tag 时完成。
- **CI 打包 dry-run + 两处冒烟**：`workflow_dispatch` 触发时会实弹跑打包与冒烟、但不建 Release；
  Windows 侧静默装/卸冒烟（不用 `Start-Process -Wait`，卸载是延迟清理要轮询），Linux 侧 Xvfb +
  `prtsc full --path` 真抓一帧（断言退出码 0 且 PNG >1KB）。
- **签名（子任务 6）拍板 = 明确不签**：无 EV 证书与 Apple 开发者账号。结论写进 README 与
  `docs/release/known-limitations.md`（含 macOS 26 上「右键打开」已失效、首推 `xattr -cr`）。
- **mac 分发（子任务 7）落地**：`scripts/pack-macos.sh` —— self-contained publish → `.app` bundle
  → Apple 网格版 `.icns`（`make-app-icon.py` 顺手加产 `app-icon-macos.png`）→ ad-hoc 封签
  （嵌套 Mach-O 逐个封 + 整包 `--deep`，理由写在脚本注释里）→ `ditto` 打 zip；CI 加 `pack-macos` 腿并
  复核「解压之后封签仍然有效」。**不用 `vpk [osx] pack`**：没有 Apple 证书时 `.pkg` 会被 Gatekeeper 判
  damaged，且 vpk 塞 `UpdateMac` 后旧封签失效——macOS 因此无安装器、无应用内更新（已写进限制清单）。
  本机实跑：`.app` 启动到托盘、热键注册成功、CLI `full --path` 抓 2560×1440 成功、`codesign --verify
  --strict` 通过；arm64 与 x64 两个 zip 都出。
- **文档**：`docs/release/ci-release.md`（一图流 + 资产契约 + 版本唯一事实 + dry-run/发版步骤 + 冒烟说明）、
  `docs/release/known-limitations.md`（未签名、TCC 屏幕录制、mac/Linux 无通知与无更新、Linux 仅源码、
  Wayland、剪贴板 4MP 阈值、真机覆盖缺口），README 平台支持/安装/打包/CI/许可同步。

**剩余（本卡未关的原因）**：

- [ ] 干净 Windows VM：装 Setup.exe → 三捕获/标注/导出全流程 → 卸载无残留；
- [ ] 更新端到端：装旧版 → 发新版 → 应用内提示 → 下载 → 重启换新（需两个真实版本）；
- [ ] QA-1 / QA-L1 真机清单勾选；
- [ ] `scripts/release.sh 1.0.0`（打 tag 属发版动作，等上面几项清完由用户执行；本地预检已演练）。

## 验收标准

- [ ] GitHub Actions 三个 test job 首次全绿（附 run 链接）。
- [ ] 打 tag 后 release 页面出现安装包 + `releases.win.json` + sha256，且 `tag == Version` 断言通过。
- [ ] 干净 Windows VM：Setup.exe 安装 → 截图/标注/导出全流程 → 卸载无残留（R-1 诚实清单遗留项）。
- [ ] 更新端到端：装旧版 → 发新版 → 应用内提示 → 下载 → 重启换新（R-1 诚实清单遗留项）。
- [x] 仓库根 README 存在，新人按 README 能在 10 分钟内跑起来（Setup / `.app` / 源码构建三选一）。
- [x] 签名与 mac 分发各有明确结论（做 / 不做 + 理由），写进 README 与 `docs/release/known-limitations.md`。
- [ ] CI 打包 dry-run 首跑确认（机制已就位：`workflow_dispatch` 会跑打包 + 两处冒烟但不建 Release）。
- [ ] `scripts/release.sh` 预检实跑（脚本已就位、语法与各段逻辑单独验过；完整预检会一路走到 bump/tag，属发版动作）。

## 测试用例

- [ ] `git tag vX.Y.Z && git push --tags` 后 CI release job 全绿（含 `pack-windows.sh` 的 bash 3.2 / Git Bash 两态实测）。
- [ ] VM 安装冒烟清单（R-1 handoff 已列）。
- [ ] 更新端到端用本地目录 feed 模拟两个版本（`UpdateManager` 支持本地路径）。

## 关联文档

- `docs/handoff/261110-r1-c1-updater-packaging-handoff.md`（诚实清单 + 下一步 1–4）
- `docs/release/ci-release.md`（一图流 / 资产契约 / dry-run 与发版步骤）、`docs/release/known-limitations.md`
- `docs/release/notes/v1.0.0.md`（首版手写发版说明）、`AGENTS.md`（工程纪律基线）
- `docs/roadmap.md` M11 行；`.github/workflows/ci.yml`；`scripts/pack-windows.sh`、`scripts/pack-macos.sh`、`scripts/release.sh`
- `kanban/issues/R-1-m10-packaging.md`、`kanban/issues/C-1-m7-cloud-library-updater-pin.md`

## Verification

- How to run/verify: 见上「测试用例」；CI run 链接与 VM 冒烟记录回填本卡。
