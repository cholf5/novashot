# N-1 M9 桌面通知

- **优先级**: P2 ｜ **复杂度**: S ｜ **开发者**: batch5 (ZCode) ｜ **状态**: Done
- **依赖**: 无
- **关联**: `StartupConfig.ShowNotification`（字段已备未接）；winshot tray 通知；flameshot notificationwidget

## 目标

保存/复制/上传成功后的系统级反馈（当前只有编辑器状态栏，托盘常驻场景下用户看不到）。

## 子任务

1. Windows Toast/气球通知封装进 `Prtsc.Platform.Windows`（Core 定义 `INotificationService` + Fake）。
2. 触发点：导出成功、QuickSave 落盘、复制到剪贴板、上传成功（含 URL）。
3. 受 `ShowNotification` 开关控制；通知点击可打开文件所在目录（做得到的话）。

## 验收标准

- QuickSave 后无焦点窗口也能收到通知；关闭开关后完全静默。

## 测试用例

- 触发条件判定（哪些事件 × 开关状态）纯逻辑单测；真机冒烟记入 QA-1。

## 完成备注（2026-11-10，batch5）

按 PM 裁决执行（简报 batch5）：

1. **平台范围**：Windows 实弹（WinRT `Windows.UI.Notifications.ToastNotificationManager`，AUMID=`Prtsc`）；
   **mac/Linux 本卡只装配 Core 的 `NullNotificationService`（静默）**，不做
   UNUserNotificationCenter / AppIndicator 通知（handoff 记录，未排期）。
2. **未打包降级**：`CreateToastNotifier("Prtsc")` 在无快捷方式机器上可能抛 → `ToastNotificationService.Show`
   内 try-catch 降级为 Warning 日志，fire-and-forget 契约（绝不影响导出/复制/上传）；真机验证路径进 QA-1 §K。
3. **触发面四处**：导出（`MainWindow.ExportCurrentAsync` → `vm.NotifyExported(path)`）、
   QuickSave 落盘（VM 成功分支，通知带落盘路径）、复制（`Copy` 成功分支——手动 Ctrl+C / 工具栏；
   **导出管线内的自动复制走 `CopySilently()` 不弹复制 toast**，review I-1 抑制双弹）、
   上传成功（`outcome.Url` 分支正文带 URL；URL 缺失退文件名）。
4. **开关下沉 Core 纯函数**：`NotificationPolicy.ShouldNotify(setting, evt)`（事件枚举 × 总开关
   决策表，per-event 关闭预留扩展位）；App 层统一走 `SuccessNotifier.Notify`（判 policy + 集中
   try-catch，「失败不扰」一处兜底）。设置页勾选框沿用已有 `Settings.General.ShowNotification`（双语在册）。
5. **点击行为**：toast `launch` 属性带文件路径（Core 纯函数 `ToastXml.Build` 拼装，可测）；
   `Activated` 回调 `explorer /select,<path>` 打开所在目录，尽力而为（无 path 的上传/复制类静默）。
6. **测试**：Core +20（决策表 4×2 + 全枚举守卫 + Null 服务 + ToastXml 转义/round-trip）、
   App +15×2TFM（四处接线、关开关零调用、无服务不炸、服务抛异常不扰、TFM 分腿装配矩阵、
   review I-1 导出管线恰 1 条 toast 钉死 + 上传 fallback 正文退文件名）、
   Mac +2 / Linux +1（装配钉 Null）。基线只增不减。
7. **Review pass（2026-11-10 独立审查）**：I-1 默认配置双/三 toast 判违背验收精神 → `Copy(notifyCopied)`
   拆分 + 管线内 `CopySilently()`（手动复制仍通知，不动 NotificationPolicy）；M-1/M-2/M-4 随手清；
   M-3/M-5 PM 记录。终态 1020 全绿。详见 `docs/handoff/261110-n1-notifications-handoff.md` Review pass 段。
7. **Review pass（2026-11-10 独立审查）**：I-1 默认配置双/三 toast 判违背验收精神 → `Copy(notifyCopied)`
   拆分 + 管线内 `CopySilently()`（手动复制仍通知，不动 NotificationPolicy）；M-1/M-2/M-4 随手清；
   M-3/M-5 PM 记录。终态 1020 全绿。详见 `docs/handoff/261110-n1-notifications-handoff.md` Review pass 段。
8. 真机项（toast 实弹、点击开目录、未打包降级、专注助手拦截）→ QA-1 §K + handoff 诚实清单。
