# 右侧面板合并 · 代码审查记录（2026-11-22）

对象：工作区改动（面板合并，见 [设计](2026-11-22-merge-style-canvas-panel.md)），基于 `48c0ecb`。
方式：com-inline-execution 自审 + com-requesting-code-review 交互审查。

## 统计

- 审查文件：MainWindow.axaml / MainWindow.axaml.cs / Loc.cs / SettingsWindow.axaml.cs / UiSnapshotTests.cs + QA 清单与看板 3 处文档
- 代码问题：P0 ×0，P1 ×0（diff 内），P2 ×1（副作用）；另记录 P1 既有环境坑 ×1（非本次引入）
- 已验证：构建 0 警告 0 错误；Core 798/798；App 218/218×net10.0 + 224/224×windows TFM；Linux 45/46、Mac 65/91（见下方非问题 4/5）；快照 windows TFM 腿全量生成并目检通过

## 问题与处置

| 级别 | 问题 | 处置 |
|---|---|---|
| P2 | `artifacts/ui/40-launcher.png`、`50-overlay-picker.png` 是 gitignore 之前入库的历史文件、仍被 git 追踪，快照重跑必翻动工作区（内容 benign：Launcher W/H 反映宿主桌面分辨率 1920×1080→2560×1440） | **已修**：`git rm --cached` 取消追踪（磁盘保留），gitignore 生效 |
| P1（既有） | AGENTS.md 快照命令 `-f net10.0` 在 Windows 宿主必炸：`AppServices.cs:84` 非 windows TFM 对非 mac/Linux 宿主故意退回 mac 装配（fail-fast），DI 取 `ICaptureService` 时 `CGDisplayApi` 静态构造抛 `DllNotFoundException`；昨夜 00:22 的残缺快照产物即此坑残留 | **已修**：AGENTS.md 命令改为 `-f net10.0-windows10.0.19041.0` 并注明根因；mac/Linux 宿主用 net10.0 腿不受影响 |

## 已确认的非问题

1. 全仓无已删符号残留引用（`Panel.Style`/`Panel.Canvas`/`OnPanelTabClick`/`StyleTab`/`CanvasTab`/`StyleHost`/`CanvasHost`）；`LocKeyConsistencyTests` 绿。
2. Q-1 背景三态 chip（OneWay 回显 + Click）与 E-2 `Apply`/`CanvasStateChanged`/防抖写盘绑定结构零触碰；快照目检 chip 互斥、选中环正常。
3. `10/11-panel-*.png` 特写对控件直接开窗、不依赖 Tab，合并后语义仍成立，保留。
4. Linux 腿 1 失败（`XdgAutostartTests.DesktopPath_FollowsXdgLayout`）：`DesktopPath` 用宿主相关 `System.IO.Path.Combine`，Windows 宿主必产 `\`，POSIX-only 断言，与本次无关，属交叉编译语义（AGENTS.md 同类），不顺手改。
5. Mac 腿 26 失败：全部 `DllNotFoundException: libc`，mac 原生 P/Invoke 用例只能在 macOS 宿主跑（AGENTS.md 已写明），Windows 宿主预期如此。
6. App net10.0 首跑 2 失败（含 `LocApplyStoredPreferenceTests`）：headless session 偶发互踩（AGENTS.md 已知 flake 家族），失败用例与删 key 正交，后续 3 次全量绿。
7. `03-editor-canvas-tab-en.png` 其余引用全在历史文档（handoff ×2、Q-1 卡存档），历史记录不改写。
