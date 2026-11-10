# 261110 看板对账 + 编译器警告清零（handoff）

> 起因：用户问「这个项目还有没有什么没完成的东西」，盘点后发现两类**非功能**缺口——
> ① 看板/路线图/issue 状态与事实脱节（Backlog 挂着已完成卡、Done 卡状态仍是 To Do、M9/M10 全交付但 roadmap 写「待开工」）；
> ② 31 条编译器警告堆着（多为 Avalonia/SkiaSharp/xUnit 升级后的 obsolete API）。
> 本批只做这两块，不碰功能语义；真机验收与发布收尾仍归 QA-1 / V-1。

## 1. 看板对账（docs only）

| 文件 | 改动 |
|---|---|
| `kanban/board.md` | Backlog 删 B-2/B-6（已在 Done）；新增 **V-1**（v1.0 发布收尾）、**Q-1**（栈溢出 flake）、**LINUX-2**（Wayland）；Done 列补 X-1 漏关卡 + 本批条目；板头加对账说明 |
| `docs/roadmap.md` | M8/M9/M10 从「未完成」移入「已完成」（各一行汇总）；「未完成」只剩 M11 收尾门（QA-1 + V-1）；缺口来源清单逐项打勾；顺序建议改为「Q-1 先于 V-1」 |
| `kanban/issues/S-1-m8-settings-gui.md` | 状态 `To Do` → `Done（2026-11-02）`，补 Development Log（六分区/草稿语义/热键录制/1 Critical + 4 Important 修复）+ Verification |
| `kanban/issues/X-1-invert-tool.md` | 状态 `Backlog` → `Done`：反色随 GAP-1 实装（`InvertAnnotation` + I 热键 + 双语 + `InvertAnnotationTests`），此前只是卡没关 |
| `kanban/issues/Q-1-radio-binding-stack-overflow.md` | 新建：栈溢出 flake 根因、崩溃栈、复现率、三个修复候选、验收口径 |
| `kanban/issues/V-1-v1-release-wrapup.md` | 新建：版本号/tag/CI 首跑/README+用户文档/签名/mac 分发七个子任务，此前这些只在 R-1 handoff 的「下一步」里 |

## 2. 编译器警告清零（31 → 0）

`dotnet build Prtsc.slnx --no-incremental`：**0 Warning / 0 Error**。

| 警告 | 位置 | 处置 |
|---|---|---|
| AVLN5001 `Window.SystemDecorations` obsolete | `OverlayWindow.axaml` | → `WindowDecorations="None"`（Avalonia 12 新属性，枚举值 `None` 同名） |
| AVLN5001 `TextBox.Watermark` obsolete ×4 | `SettingsWindow.axaml` ×2、`CloudSettingsWindow.axaml` ×2 | → `PlaceholderText`；CloudSettings 两处本次未报但同族，一并换掉 |
| CS8602 三次 | `CliApplication.cs:50`、`MainWindowViewModel.cs:247/267` | `CliApplication`：`parse.Success` 后取本地 `command` 并加 null 兜底（不变式本是非空，兜底改为可读错误而非 NRE）；VM 两处存盘路径取本地 `store` 并显式判空（`TakeConfigDirty` 已保证非空，仅为可空性收敛） |
| CS8604 ×2 | `CliPipeProtocolTests.cs` | `args!`（`Assert.True(TryDecode...)` 之后本就非空） |
| xUnit1031 ×4 | `UiSnapshotTests.cs:163`、`UploadCoordinatorTests.cs:109/137/190` | Upload 三处改 `await store.LoadAsync()`（真修）；UiSnapshot 一处**保留阻塞**——快照测试手工泵 `Dispatcher.RunJobs`，改 async 会让续体落到线程池线程触发 invalid thread，故加 `#pragma warning disable xUnit1031` + 原因注释（刻意的、无 SynchronizationContext 不死锁） |
| CS0618 `SKFilterQuality` / `Resize(...)` ×3 | `UiSnapshotTests.cs` | → `SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear)`（`SKFilterMode.Cubic` 在 SkiaSharp 3.119.4 不存在，编译实证；快照不做像素断言，质量档位无影响） |
| CS0618 `Bitmap.Save(string,int?)` | `UiSnapshotTests.cs` | → `frame.Save(path, PngBitmapEncoderOptions.Default)`；补 `using Avalonia.Media.Imaging` |
| CS0067 事件未触发 | `FakeXLibApi.cs` | 显式空访问器 `event ... { add { } remove { } }`（假实现本就不产生 X 错误，保留接口契约） |
| CS8778 常量可能溢出 nint | `MacClipboardServiceTests.cs:216` | `unchecked((nint)(0x7FFF0000L * 4))`（64 位下值本身合法，只是 32 位语义警告） |

## 3. Q-1 实证（本批顺带产出）

清警告时跑测试踩到存量 flake，顺手补了决定性证据（已写进 Q-1 卡）：

- `dotnet test tests/Prtsc.App.Tests -f net10.0` 8 跑 3 abort，崩溃前 `Passed: 187/188/192`（总数 206）。
- 栈：`RadioButtonGroupManager.OnCheckedChanged → ToggleButton.get_IsChecked → EffectiveValue.SetAndRaiseCore → UntypedBindingExpressionBase.PublishValue → InpcPropertyAccessor.SendCurrentValue → PropertyAccessorNode.WriteValueToSource`（环），中间夹 `OnNodeError`。
- 位置：`CompositionPanel.axaml` L25–31 三枚 `GroupName="BgKind"` + `IsChecked Mode=TwoWay`。
- **结论：与本次警告改动无关**——B-6 审查轮已用 stash 干净树复现过同样 abort。

## 4. 测试

| 套件 | 结果 |
|---|---|
| Core | 785/785 绿 |
| App（net10.0） | 206/206 绿；**Q-1 偶发 abort 仍在**（修复前无法保证每次全绿） |
| Mac | 91/91 绿 |
| Linux | 46/46 绿 |

App 的 windows TFM 本机（macOS）无法执行，仅确认编译通过（0 warning）。

## 5. 未做（留给后续卡）

- ~~**Q-1 修复**~~ → 已做，见 §6。
- **V-1**：版本号、tag、CI 首跑、README/用户文档、签名、mac 分发——都需要远端或真机权限。
- **QA-1 / QA-L1**：88 项真机清单仍是 0/88（§F 新增一条 Q-1 修复后的背景 chip 手感冒烟项）。
- 本机 `main` 仍领先 `origin/main` 10 个 commit（本批再 +1），未推送。

## 6. Q-1 修复（同批续做）

**改动**：`CompositionPanel` 背景三态 chip 的 `IsChecked` 从 `TwoWay` 降为 `OneWay`，输入改走 `Click` + `Tag`，code-behind 调 VM 既有咽喉点 `Apply`；三枚 chip 加 `Name`（供测试按名查）与 `Tag`。保留 `GroupName`（与工具坞 `GroupName="Tool"` + OneWay 的同口径）。

**为什么这样修**：环的结构前提是「目标侧能写回源」。断掉回写后，组管理器取消兄弟只是纯视觉更新，通知链不会重新进入 `IsCheckedChanged`。没选「换自定义单选控件」是因为改动面大、收益小。

**验证（A/B 同机同命令）**：

| 阶段 | 结果 |
|---|---|
| 修前（stash 掉两处源码后重建） | 15 跑 **3 abort**（停在 186/187/187） |
| 修后 | 15 跑 **0 abort** + 另 10 跑 0 abort（共 25 连跑） |
| 新增回归 | `BackgroundKindChipTests` 3 例（目标侧不回写 / 点击切态+互斥回显+`CanvasStateChanged` 恰一次 / 100 次连切不递归） |
| 全量 | Core 785 + App 209（206+3）×net10.0 + Mac 91 + Linux 46；`dotnet build` 0 warning / 0 error |
| 快照 | `PRTSC_SNAPSHOT=1 … --filter UiSnapshot` 通过，`03-editor-canvas-tab-en.png` 目检 Gradient 选中态正常 |

**踩坑（写回归用例时踩到的）**：把 `CapturedImage` 放进 `Build()` 的 `using` 里 → 返回后位图已释放，Skia 在布局/测量阶段直接 abort 宿主，无异常无堆栈，极易误判成 Q-1 复发。正解：位图必须活到窗口关闭之后；已写进测试注释。**排查手法备注**：先用 `File.AppendAllText` 打点定位到「崩在 `RaiseEvent` 内部」，再降到独立 console 宿主（`Avalonia.Headless` 直接可跑）看它反而正常，才揪出是测试自己的生命周期 bug。

**未覆盖**：App 的 windows TFM 本机跑不了，只在 CI/Windows 上验证；`SettingsWindowXamlTests` 的跨线程 Radio 孤例（U-1 记录）本轮没再现（修后 25 连跑未复现），若后续又在 Windows 腿出现，按同一口径查是否有残留 TwoWay。

## 7. README + 用户文档（V-1 的 4/5 项，同批续做）

仓库此前**没有 README**，也没有任何面向用户的手册（只有面向开发者的 handoff / 看板 / QA 清单）。

- `README.md`（根）：一句话定位 + 特性清单（捕获/15 工具表/合成输出/云与历史/CLI/设置/i18n）+ 平台支持矩阵与四条已知限制（Wayland、未签名、mac 屏幕录制权限、mac/Linux 无系统通知）+ 安装（Setup/Portable/源码 + Linux 需 xclip）+ 快速上手与两层快捷键表（编辑器 / overlay）+ CLI 用法与选项表 + 配置文件与数据目录（三平台、四类文件）+ 从源码构建与 `pack-windows.sh` + 测试命令与当前用例数（785/209/91/46）+ 项目结构 + 文档索引 + 许可（如实写「尚未添加 LICENSE」）。
- `docs/user/`：`README.md`（索引 + 术语 + 默认热键）+ 7 篇——`01-capture`（三种捕获、overlay 操作表、跨屏、窗口选择器、延时、启动器）、`02-annotate`（工具热键表、通用操作、Style/Canvas 面板逐项、crop reshape）、`03-compose-export`（顶栏、合成参数三行为、三种落盘、QuickSave 命名、剪贴板读写策略、Pin、导入）、`04-cloud-library`（R2/Drive 配置步骤、测试连接、上传时机、Library 操作、删除策略表、数据位置）、`05-settings`（六分区逐项 + 托盘菜单 + config.json）、`06-cli`（模式表、选项表、单实例语义、退出码、cron/PowerShell 示例）、`07-troubleshooting`（黑帧/权限/Wayland、热键失效、xclip、SmartScreen、toast 不弹、更新不提示、粘贴失败、重置、报问题模板）。
- 事实核对方式：文档里的路径、范围值、键位、文案全部对照源码（`PrtscConfig` / `FilenamePatterns` / `CompositionPanel.axaml` 滑杆范围 / `MainWindow.axaml` KeyBindings / `CliArgs.HelpText` / `Loc.cs`）确认，不凭印象写。
- 顺手修一处用户可见不一致：设置页 QuickSave 三种模式示例写的是 `prtsc-20261102-094213` / `prtsc-20261102` / `prtsc-0042`，实际实现是 `prtsc_2026-11-02_09-42-13` / `prtsc_2026-11-02` / `prtsc_001`（分隔符、时间形态、序号位数都不同）→ 双语 Loc 同步改正。
- 未做：英文版 README/user docs（当前仓库文档体系全中文，按「与代码库一致」处理）；LICENSE 需拍板。
