# L-2 M10 i18n 收尾 — 抽串审计 + key 一致性单测 handoff（2026-11-10）

## 0. TL;DR

L-2 是收尾批（复杂度 S）。PM 裁决（已写进 issue 完成备注）：取串机制定为 **`Loc.cs` 双语词典 + DynamicResource**（卡原文允许「Avalonia 资源字典或 resx 均可」），**不做 resx 迁移**；子任务 2/3（全量抽串、zh-CN 首发 + 语言切换）已被 UI-1/S-1 吸收。本批只做两件事：

1. **key 一致性/回退单测（CI 兜底，核心交付）**：新增 `tests/Prtsc.App.Tests/LocKeyConsistencyTests.cs` 4 用例（×2 TFM = 8）。覆盖：En/Zh key 集合双向相等 + value 非空；引用完整性（引用 ⊆ 定义，单方向）；缺 key 回退链（zh 缺 → en 兜底 → key 本身）；带参 `T(key, args)` 走 string.Format。
2. **硬编码文案审计修复**：全量扫描 src/Prtsc.App（.axaml 属性 + .cs 字符串字面量启发式），修复 6 处残留（§2），不抽类别逐条记录（§2.3）。

配套改动：`Prtsc.App.csproj` 增加 `<InternalsVisibleTo Include="Prtsc.App.Tests" />`（与 Mac/Linux 平台工程既有约定一致）；测试仓库根定位提升为 `Support/TestRepoRoot.cs`。当前账目：En/Zh 各 292 key（双向相等）、引用 274 个、缺失 0、静态未引用 16（§2.4）。全量测试 Core 502 + App 104×2TFM + Mac 80 + Linux 45 全绿。

## 1. 机制备忘（不重构，只钉死现状）

- `Loc.T(key)`：按 `Current` 选表（zh-CN → `Strings.Zh`，其余 → `Strings.En`）→ 缺则回 `Strings.En` → 再缺返回 key 本身。
- `Loc.T(key, args)`：`string.Format(T(key), args)` —— key 缺失时对返回的 key 本身做 Format（无占位符 = 原样返回 key）。
- 切语言 = `SetPreference`/`ApplyStoredPreference` → `Apply` 把整表按 `L.` 前缀写进 `Application.Resources`，XAML 用 `{DynamicResource L.Xxx}` 自动刷新，免重启。
- key 不带 `L.` 前缀（Loc 统一加）；托盘菜单代码侧经私有 helper `L(key)` 包 `Loc.T`。

## 2. 硬编码文案审计清单

### 2.1 扫描口径与方法

- **范围**：`src/Prtsc.App` 全部 `.axaml` + `.cs`（排除 `obj/`）。
- **方法**：两条腿 —— ① XAML 属性正则（`Content/Text/Title/Watermark/Header/ToolTip.Tip="..."`）筛非资源引用的字面量；② .cs 全量字符串字面量提取 + 启发式过滤（跳过日志调用、注释、`Loc.T`/`L(` 调用本身、config 值形状），剩余逐条人工判定。
- **修复规则**：新 key 进 `Strings.En`/`Strings.Zh` 双表（en + zh-CN 同时写好），XAML 用 DynamicResource、代码用 `Loc.T`；key 命名跟既有分区（Common./Tray./Settings./Editor./Status./Library./Picker./Cloud.）。

### 2.2 发现即修（本批修复的 6 处 / 17 个新 key）

| # | 位置 | 原文 | 修复 |
|---|------|------|------|
| 1 | `MainWindow.axaml.cs` ExportCurrentAsync | `Title = "Export image"`（保存对话框标题） | `Editor.ExportDialogTitle` 导出图片 |
| 2 | 同上 | `new("PNG image"/"JPEG image"/"WebP image")`（保存对话框文件类型下拉） | `Editor.ExportTypePng/Jpeg/Webp` PNG 图片 等 |
| 3 | 同上 | `"Export failed: no local file path."` / `"Export failed: " + ex.Message` | `Status.ExportFailedNoPath` / `Status.ExportFailed` |
| 4 | 同上 | `$"Saved {path}"` / `$"Saved {path} (also copied to clipboard)"` | `Status.Exported` / `Status.ExportedCopied` |
| 5 | `MainWindow.axaml.cs` 全屏/区域/窗口捕获 catch | `"Capture failed: "`、`"Region capture failed: "`、`"Window capture failed: " + ex.Message` | `Status.CaptureFailed` / `Status.WindowFailed` 新 key；`Status.RegionFailed` 复用既有 key（原硬编码文本与它一字不差） |
| 6 | `LibraryWindow.axaml.cs` 删除确认对话框 | 整段硬编码中文：`Title="确认删除"`、`确定删除「{fn}」？`、`仅删除历史记录`、`本地文件 {f} 也将删除`、按钮 `取消`/`删除` | `Library.DeleteConfirmTitle/Record/FileStays/RecordOnly` 新 key + 复用 `Common.Cancel`/`Library.Delete`；**语义修正**：原文案「本地文件也将删除」与实际行为不符 —— `LibraryStore.RemoveAsync` 只删记录不删文件，新文案「图片文件仍保留在磁盘上」与事实一致（正是原死键 `Library.DeleteConfirm` 的文案，该键已删除，由三个精确 key 取代） |
| 7 | `WindowPickerDialog.axaml.cs` Row | `"(untitled)"`、`"[最小化]"` | `Picker.Untitled`（未命名）/ `Picker.Minimized`（已最小化） |
| 8 | `CloudSettingsWindow.axaml.cs` OnTestConnectionClick | `"先选择 R2 或 Google Drive 渠道再测试。"` | `Cloud.PickProviderFirst`（zh 原样保留品牌名，en 译出） |

新 key 共 17 个（Editor 4 + Status 6 + Library 4 + Picker 2 + Cloud 1），删 1 个死键；En/Zh 各 292 key。

### 2.3 不抽类别（判定口径，验收抽查用）

按简报口径，以下字面量是**用户可见但不抽**或**非用户可见**：

- **产品名/品牌名**：`Title="Prtsc*"`（窗口名，MainWindow 运行时被 `ApplyLocalizedTitle` 覆盖）、`Text="Prtsc"` 品牌字、`Prtsc v{version}` 托盘 tooltip、`"Cloudflare R2"` / `"Google Drive"`（第三方品牌，zh 文案里也保留英文原名）。
- **纯符号/数字**：FloatingToolRing `✕`/`✓`、CompositionPanel 自动取色按钮 `A`（已有 tooltip `Panel.BackgroundAutoTip` 承载语义）、滑杆刻度 `0/48/96` 等、工具环热键角标 `V/R/E/...`（与 `Tool.*` tooltip 里的 `(V)` 对应的单字符）、`✓ `/`✗ ` 前缀。
- **config 键名/格式串**：`auto/date/increment/timestamp`、`none/gdrive`、`center/inside/outside`、`style`、`capture/editor/export/general/hotkeys/quicksave`、`*.png` 等 Patterns、`1:1` 比例项、`thumb_{id}.png`。
- **技术标识**：`x:Name`/`FindControl` 名（`NavGeneral`、`WindowList`、`CancelButton`...）、`KeyCombo` 键名（Ctrl/Shift/Enter/PageUp...——键盘物理键名，zh 用户预期为英文）、`Segoe UI` 字体名。
- **日志/异常原文**：所有 `_logger.Log*`、Serilog 模板、`outcome.Error`/`ex.Message` 直通（`Status.CopyFailed` 等 key 的 `{0}` 参数）。
- **数据格式（无词可译）**：`{w}×{h}` 尺寸、`B/KB/MB` 单位、时间戳行格式。

### 2.4 刻意预留但当前未被引用的 key（16 个，反向不断言）

`Common.Close/Copy/Open/Auto`（4）、`Capture.Ready/RegionOn/Settings/Language/LanguageSystem`（5）、`Cloud.Save`、`Picker.Empty`、`Library.Search`、`Editor.Delete`（axaml 里的命中是 ViewModel 命令 `Editor.DeleteSelectionCommand`，非 loc key）、`Panel.Opacity/Position/NoSelection`。这些是 UI 重设计后的预留/历史键。另有 2 个 key（`Settings.Hint.HotkeyInvalid/HotkeyDuplicate`）是**动态引用**（`ValidateHotkeys` 返回 key 名传给 `Loc.T`），静态扫描归不到引用侧但实际在用。key 一致性测试**只断言「引用 ⊆ 定义」**，未引用不判错（否则 false positive）——简报明确裁决，已写进测试注释。

## 3. 测试设计（`LocKeyConsistencyTests`，4 用例 ×2 TFM）

1. **`En_and_Zh_have_identical_key_sets_and_no_blank_values`**：两表 key 集合 Except 双向相等（失败消息列出差异 key 与所在侧）；所有 value `IsNullOrWhiteSpace` 为空则失败；两表非空作 sanity。
2. **`Every_referenced_key_is_defined_in_both_tables`**：扫描 `src/Prtsc.App` 的 .axaml（`{Dynamic,Static}Resource L.x`）与 .cs（`Loc.T("...")`、私有 `L("...")`、`FindResource("L.x")`/`Resources["L.x"]`，括号配平时跳过字符串字面量内部括号），提取 key 形状（`^[A-Z]\w*(\.\w+)+$`）的字面量，断言每个 key 在 En 与 Zh 都在册；**引用数为 0 时显式失败**（防文件挪走/正则过时后静默通过）。仓库根定位：`Support/TestRepoRoot.Path`（从 `AppContext.BaseDirectory` 向上找 `Prtsc.slnx`，原 `UiSnapshotTests.RepoRoot` 实现提升为公共 helper，UiSnapshotTests 改用之）。
3. **`T_missing_key_falls_back_to_en_then_returns_key_itself`**：注入测试 key 只进 `Strings.En`（`internal`，经 `Prtsc.App.csproj` 新增 `InternalsVisibleTo`，与 Mac/Linux 平台工程同约定）→ zh 下 `T` 返回 en 文案 → zh 侧补齐后优先 zh → 两侧都缺返回 key 本身；finally 清理注入 key 并还原 `Loc.Initialize("en")`。
4. **`T_with_args_formats_via_string_format`**：真实 key（`Status.Captured`）+ 注入含 `{0}` 占位符的 key 走 Format；缺 key + 参数 = 对 key 本身 Format（无占位符原样返回）。

**刻意不断言的反方向**（定义 ⊆ 引用）与**已知盲区**写进了测试类注释：动态拼 key（`ValidateHotkeys` 返回 key 名传 `Loc.T`）、预留 key；括号配平假设调用不跨行（漏扫只影响覆盖面，不影响「引用必在册」的正确性）。

## 4. 诚实清单（坑与边界）

- **文案语义修正了一处产品行为描述**：删除确认对话框原 zh 文案声称「本地文件也将删除」，与 `LibraryStore.RemoveAsync`（只删记录）矛盾。本批按实际行为改为「图片文件仍保留在磁盘上」。若产品本意是"连文件一起删"，那是 G-17 的行为 bug，应改代码而不是改文案 —— 留给 PM 知悉。
- **`Status.Exported` 与 `Status.QuickSaved` 的 en 文案相同**（"Saved {0}"）：两个功能语境（导出 vs 热键快速保存）各自持 key，方便日后独立改词；没有为省 key 做跨功能复用/重命名。
- **新增 4 用例进入了 `HeadlessUi` collection**（DisableParallelization）：回退测试要注入/移除 `Strings.En/Zh`（internal 静态表）并改 `Loc.Current`，必须串行。已知存量 flake `SettingsWindowXamlTests`（并行/时序偶发）与本批无关，重跑即绿；本批跑过 6+ 轮全绿。
- **`Picker.Minimized` 的 zh 译文用了半角方括号 `[已最小化]`**，未用全角 `［］`：窗口列表是等宽小字排版，半角与相邻尺寸串（`W×H`）视觉一致。
- **审计正则启发式有人工兜底**：.cs 字面量扫描会漏多行字符串/插值内嵌引号等形态，剩余 90 条命中全部人工过目归类（§2.3）；没有未分类的漏网项。
- **简报数字勘误**：简报写「727/727（Core 502 / App 100×2TFM / Mac 80 / Linux 45）」——分项之和实为 827（727 疑为 D-1 review pass 时的旧总数）；本批以分项为准，改后 831（App 104×2）。
- **没动的**：`Loc.Initialize/Apply/Resolve/T` 语义零改动；`kanban/board.md` 未动（PM 负责）；未派生 subagent。

## 5. 复跑指引

```bash
dotnet build Prtsc.slnx
dotnet test tests/Prtsc.App.Tests/Prtsc.App.Tests.csproj --filter "FullyQualifiedName~LocKeyConsistency"   # 8 (4×2TFM)
dotnet test   # 全量：Core 502 + App 104×2TFM + Mac 80 + Linux 45
```
新增 key 时只需在 `Loc.cs` 双表同时加（一致性测试兜底）；新增引用若用非 `Loc.T(...)`/`L(...)`/`DynamicResource L.*` 的新入口，记得扩 `LocKeyConsistencyTests` 的扫描正则。
