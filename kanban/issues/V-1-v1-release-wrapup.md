# V-1 v1.0 发布收尾（版本号 / tag / CI 首跑 / 文档 / 签名 / 分发）

- **优先级**: P1 ｜ **复杂度**: M ｜ **状态**: Backlog ｜ **来源**: M11 收尾门拆解——R-1 handoff「下一步」+ roadmap M11 的「用户文档 + 发布」两项此前无卡承载

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

## 验收标准

- [ ] GitHub Actions 三个 test job 首次全绿（附 run 链接）。
- [ ] 打 tag 后 release 页面出现安装包 + `releases.win.json` + sha256，且 `tag == Version` 断言通过。
- [ ] 干净 Windows VM：Setup.exe 安装 → 截图/标注/导出全流程 → 卸载无残留（R-1 诚实清单遗留项）。
- [ ] 更新端到端：装旧版 → 发新版 → 应用内提示 → 下载 → 重启换新（R-1 诚实清单遗留项）。
- [ ] 仓库根 README 存在，新人按 README 能在 10 分钟内跑起来（Setup 或源码构建二选一）。
- [ ] 签名与 mac 分发各有明确结论（做 / 不做 + 理由），写进 README 或 handoff。

## 测试用例

- [ ] `git tag vX.Y.Z && git push --tags` 后 CI release job 全绿（含 `pack-windows.sh` 的 bash 3.2 / Git Bash 两态实测）。
- [ ] VM 安装冒烟清单（R-1 handoff 已列）。
- [ ] 更新端到端用本地目录 feed 模拟两个版本（`UpdateManager` 支持本地路径）。

## 关联文档

- `docs/handoff/261110-r1-c1-updater-packaging-handoff.md`（诚实清单 + 下一步 1–4）
- `docs/roadmap.md` M11 行；`.github/workflows/ci.yml`；`scripts/pack-windows.sh`
- `kanban/issues/R-1-m10-packaging.md`、`kanban/issues/C-1-m7-cloud-library-updater-pin.md`

## Verification

- How to run/verify: 见上「测试用例」；CI run 链接与 VM 冒烟记录回填本卡。
