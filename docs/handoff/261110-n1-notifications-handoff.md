# N-1 Handoff — M9 桌面通知（2026-11-10）

## TL;DR

**N-1 整卡 Done**：保存/复制/上传成功后的系统级桌面通知。

- **Core**：`INotificationService`（`Show(title, message, filePath = null)`，fire-and-forget 契约）+
  `NullNotificationService`（静默默认实现）+ `NotificationPolicy.ShouldNotify(setting, evt)`
  （事件枚举 × 总开关决策表纯函数）+ `NotificationEvent`（Exported/QuickSaved/Copied/Uploaded）+
  `ToastXml.Build`（WinRT toast XML 拼装纯函数，从平台服务抽到 Core 以便单测）。
- **Platform.Windows**：`Notifications/ToastNotificationService` —— WinRT
  `ToastNotificationManager`（TFM 自带 Windows SDK 引用面，零新包），AUMID=`Prtsc`；
  `Activated` 回调 `explorer /select,<path>` 打开文件所在目录（尽力而为）；一切异常自吞降级为日志。
- **App 接线四处**：导出成功（MainWindow → `vm.NotifyExported(path)`）、QuickSave 落盘（VM，带 path）、
  复制到剪贴板（VM Copy 成功分支，手动 Ctrl+C 与导出后自动复制同走此路）、上传成功（`outcome.Url`
  分支正文带 URL）。全部经 `App/Notifications/SuccessNotifier`（唯一出口：判 policy + 集中 try-catch）。
- **开关**：`StartupConfig.ShowNotification`（默认 true；设置页勾选框 S-1 已有，本卡只接线）。
- **mac/Linux**：本卡装配 `NullNotificationService`（静默）——UNUserNotificationCenter /
  AppIndicator 通知未排期（如实记录，见「诚实清单」）。

测试基线只增不减：Core 558→**578**，App 142→**157**×2TFM，Mac 80→**82**，Linux 45→**46**，
合计 **1020 全绿**（review pass 后终态，见文末 Review pass 段），build 0 Error（windows TFM 在 mac 上
EnableWindowsTargeting 交叉编译通过）。

## 文件地图

| 文件 | 内容 |
|---|---|
| `src/Prtsc.Core/Notifications/NotificationEvent.cs` | 事件枚举（4 值，per-event 扩展预留） |
| `src/Prtsc.Core/Notifications/NotificationPolicy.cs` | `ShouldNotify(bool setting, NotificationEvent evt)` 决策表纯函数；未知事件（未来加值）沿用总开关 |
| `src/Prtsc.Core/Notifications/INotificationService.cs` | 接口（fire-and-forget 契约注释）+ `NullNotificationService`（含 `Instance` 单例） |
| `src/Prtsc.Core/Notifications/ToastXml.cs` | Toast XML 拼装纯函数：ToastGeneric 双行 + 可选 `launch` 属性；`SecurityElement.Escape` 全文本/属性转义 |
| `src/Prtsc.Core/CoreServiceCollectionExtensions.cs` | `AddPrtscCore` 默认注册 Null（TryAdd） |
| `src/Prtsc.Platform.Windows/Notifications/ToastNotificationService.cs` | WinRT Toast 实现（AUMID 常量 `AppUserModelId="Prtsc"`；XML→`Windows.Data.Xml.Dom.XmlDocument`；`Activated` → explorer /select；全路径 try-catch 降级日志） |
| `src/Prtsc.Platform.Windows/WindowsServiceCollectionExtensions.cs` | `RemoveAll<INotificationService>()` + TryAdd Toast（覆盖 Core 默认 Null） |
| `src/Prtsc.Platform.Mac/MacServiceCollectionExtensions.cs`、`LinuxServiceCollectionExtensions.cs` | 显式 TryAdd Null（Core 已默认注册，幂等，钉装配意图） |
| `src/Prtsc.App/Notifications/SuccessNotifier.cs` | App 层唯一通知出口：`service? + configStore? + evt + 文案` → policy → Show；集中 try-catch（「失败不扰」单点兜底） |
| `src/Prtsc.App/ViewModels/MainWindowViewModel.cs` | 注入 `INotificationService?`（ctor 尾参，默认 null 兼容旧构造）；`NotifyExported` 公开方法 + QuickSave/Copied/Uploaded 三个私有通知；上传成功两分支都通知（URL / 文件名） |
| `src/Prtsc.App/Views/MainWindow.axaml.cs` | 导出成功路径 `_vm.NotifyExported(path)`（StatusText 之后、记 Library 之前） |
| `src/Prtsc.App/AppServices.cs` | `MainWindowViewModel` 工厂传 `GetService<INotificationService>()` |
| `src/Prtsc.App/Localization/Loc.cs` | 新增 8 key 双语：`Notify.Exported/QuickSaved/Copied/Uploaded` × Title/Message |
| `tests/Prtsc.Core.Tests/Notifications/NotificationPolicyTests.cs` | 决策表 4×2 + 全枚举×开关守卫 + 枚举 4 值钉死（共 10） |
| `tests/Prtsc.Core.Tests/Notifications/NotificationServiceTests.cs` | Null 服务可调用/不抛/Instance 同引用（3） |
| `tests/Prtsc.Core.Tests/Notifications/ToastXmlTests.cs` | XML 合法性/双文本/launch 属性/特殊字符转义 round-trip/ToastGeneric（7） |
| `tests/Prtsc.App.Tests/NotificationWiringTests.cs` | 四处接线：成功通知、关开关零调用、无服务不炸、Null 服务 headless 不炸、服务抛异常不扰主流程、I-1 导出管线单 toast 钉死、上传 fallback 正文退文件名（14） |
| `tests/Prtsc.App.Tests/NotificationAssemblyTests.cs` | TFM 分腿装配矩阵：windows→Toast、net10.0→Null（`#if WIN_PLATFORM`） |
| `tests/Prtsc.App.Tests/Prtsc.App.Tests.csproj` | 新增 windows TFM 的 `WIN_PLATFORM` DefineConstants（与 App 同款） |
| `tests/Prtsc.Mac.Tests/MacServiceCollectionTests.cs`、`tests/Prtsc.Linux.Tests/LinuxServiceCollectionTests.cs` | 装配钉 Null（含 Core 先行的完整顺序）（+2 / +1） |
| `docs/qa/261102-smoke-checklist.md` | 追加 §K：N-1 真机段（toast 实弹/点击开目录/未打包降级/专注助手/开关） |

## 关键裁决与坑

1. **AUMID 约束（本卡最大外部依赖）**：WinRT toast 未打包时需要注册过的 AUMID。AUMID 钉 `Prtsc`：
   - **vpk 安装版**有开始菜单快捷方式 → `CreateToastNotifier("Prtsc")` 可用，toast 正常弹；
   - **未打包**（`dotnet run` / 便携 zip 无快捷方式）→ AUMID 未注册，`CreateToastNotifier`/`Show` 抛
     COMException 类异常 → 服务内 try-catch 降级为 Warning 日志。**绝不崩溃**是硬契约（fire-and-forget）。
2. **WinRT `ToastNotification.Activated` 的投影签名是 `TypedEventHandler<ToastNotification, object>`**
   （非泛型历史签名；SDK 投影并没有给强类型 `ToastActivatedEventArgs`）——回调里 `args as
   ToastActivatedEventArgs` 取 `Arguments`（launch 属性原值，WinRT XML DOM 已实体解码，路径含 `&` 无损）。
3. **XML 拼装下沉 Core**（`ToastXml.Build`）：`SecurityElement.Escape` 转义 `& < > " '`，
   属性用 `"` 包裹安全；测试用 XDocument 解析断言 round-trip（含 `C:\dir&sub \<weird>.png`）。
4. **注册顺序**：`AppServices` 先 `AddPrtscCore`（TryAdd Null 默认）后平台 → Windows 腿用
   `RemoveAll<INotificationService>()` 再 TryAdd Toast 显式覆盖；mac/Linux 的 TryAdd Null 是幂等声明。
   装配矩阵有 TFM 分腿单测钉死（App.Tests 新增 `WIN_PLATFORM` define，与 App 工程同款条件）。
5. **「失败不扰」集中在 `SuccessNotifier`**：config 读取、policy、服务调用三段全在一个 try-catch 里，
   服务实现自身也自吞（双保险）。调用点零 try-catch、零 if-else 长链。
6. **上传通知分支**：简报指明「outcome.Url 分支，正文带 URL」；实现为 outcome.Ok 两个成功分支都通知
   （URL 复制成功 → 正文 URL；URL 缺失/复制失败 → 正文退文件名）——上传成功是同一事件，少一个静默死角。
7. **复制通知的自动复制路径**（review I-1 已修）：导出默认 `Export.AutoCopyToClipboard=true`。
   初版自动复制同走 `Copy` 命令 → 双 toast（配 AutoUpload 三条）；review 判定违背验收精神，已拆
   `Copy(bool notifyCopied)`——管线自动复制走 `CopySilently()` 不通知，手动复制（Copy 命令/Ctrl+C）仍通知；
   抑制点在调用侧而非 `NotificationPolicy`（全局关 Copied 会误伤手动复制）。回归钉
   `ExportPipeline_WithAutoCopy_ExactlyOneToast` / `CopySilently_DoesNotNotify_ManualCopyStillDoes`。
8. **设置项 hint 复查**：`Settings.General.ShowNotification` 勾选框双语标签在册（"Show capture
   notifications"/"截图完成通知"），与实际行为（保存/复制/上传成功通知）一致；本分区其它 CheckBox
   均无 hint 样式，未新增。
9. 基线口径：简报写 963（Core 558 / App 142×2 / Mac 80 / Linux 45）＝按子项相加 967，与总标 963 差 4，
   属简报内部算术口径差；本批各子套件相对子项基线**全部只增**（+20 / +12×2 / +2 / +1），实跑合计 1014。

## 诚实清单（未真机/未验证项）

- **Windows toast 实弹**（本机 macOS，未弹过一条真 toast）：vpk 安装版四触发点的 toast 弹出、
  双语文案观感、`explorer /select` 点击开目录。逻辑各环节有单测（XML/接线/装配），实弹项在 QA-1 §K。
- **未打包降级实弹**：`dotnet run` 场景 AUMID 未注册 → 预期 Warning 日志 + 不崩溃；未在真 Windows 机跑过。
- **专注助手/勿扰/通知权限关闭**时的系统拦截行为未验证（预期流程零感知）。
- **安装版 AUMID 可用性**依赖 vpk 安装器确实创建开始菜单快捷方式（R-1 已做）——AUMID=`Prtsc` 与快捷方式
  目标名的一致性未在装机环境核对，若 toast 静默不弹，先查此项（`Get-StartApps | findstr Prtsc` 可看 AUMID）。
- mac/Linux 原生通知（UNUserNotificationCenter / AppIndicator/libnotify）**本卡明确不做**（PM 裁决），
  装配 Null 静默；后续如做，加平台实现 + 各自装配扩展覆盖即可，Core 接口与 policy 不用动。
- 默认配置一次导出弹 2 条 toast（导出 + 自动复制）的真人观感未验（坑 7）。

## 下一步

1. Windows 真机/VM 跑 QA-1 §K（可并入 R-1 的 VM 安装冒烟一趟做完）。
2. mac/Linux 原生通知开卡（Core 接口已备好，只差平台实现 + 装配替换）。

## Review pass（2026-11-10 独立审查后跟进）

独立审查结论「需修复（轻）」，1 Important + 3 个一行级 Minor 随本轮清掉（发现清单
`/tmp/prtsc-sdd/batch5-review-findings.md`，commit `fix(app): N-1 review pass`）：

- **I-1（必修）**：默认配置一次导出确定性弹 2 条 toast（AutoUpload 时 3 条），自动复制 toast 零新信息，
  违背「系统级反馈」的验收精神。按审查方案修：`MainWindowViewModel.Copy` 拆 `Copy(bool notifyCopied)`
  （RelayCommand 走 `true`）+ 公开 `CopySilently()`；`MainWindow.ExportCurrentAsync` 的自动复制改调
  `CopySilently()`，手动复制（工具栏/Ctrl+C，同一命令）不变仍通知。**未动 `NotificationPolicy`**
  （全局关 Copied 会误伤手动复制；per-event 扩展位留给未来设置页 per-event 开关的正当需求）。
  补 wiring 测试钉死：`ExportPipeline_WithAutoCopy_ExactlyOneToast`（导出+自动复制 → 恰好 1 条、
  且是导出那条带路径）、`CopySilently_DoesNotNotify_ManualCopyStillDoes`（静默不炸 + 手动复制反馈保住）。
- **M-1**：上传 fallback 分支（PublicUrl 空 → Url=null → 正文退文件名）补接线测试
  `Upload_Success_NoUrl_NotifiesWithFileName`（断言正文无 http、以 .png 文件名结尾）。
- **M-2**：复制通知正文断言 `Assert.Contains("8", …)` 过弱（匹配任何含 8 的文案）。实测踩坑：
  复制的是**合成结果**（默认 padding 40 → 88×86），不是原始抓帧 8×6，审查建议的字面 "8 × 6" 同样不成立
  ——改为与 VM 同管线渲染取尺寸后断言 `$"{image.Width} × {image.Height}"`（钉真实宽高对且不随画布默认值变脆）。
- **M-4**：QA-1 §K toast 数量预期 2 条 → **1 条**（注明双弹已抑制、2 条 = 回归；AutoUpload 时 2 条）。
- M-3（导出 View 调用点无 headless 自动化）/ M-5（fallback 空串纯防御，实际到不了）由 PM 记录，本卡不改。
- 审查通过项（未改）：触发点全部成功路径触发、ToastXml 转义/模板、WinRT 线程安全与降级、
  explorer /select 转义、policy 纯函数、三腿装配、Loc 双语、上传双分支偏差判定「合理，接受」。

测试：1014 → **1020 全绿**（App +3×2TFM：I-1 两例 + M-1 一例；Core/Mac/Linux 不变），build 0 Error。
已知 flake 不涉本卡：SettingsWindowXamlTests（并行偶发，重跑即绿，本轮实遇一次）。
