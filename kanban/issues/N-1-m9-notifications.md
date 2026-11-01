# N-1 M9 桌面通知

- **优先级**: P2 ｜ **复杂度**: S ｜ **开发者**: 待领取 ｜ **状态**: To Do
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
