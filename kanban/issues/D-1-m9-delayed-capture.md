# D-1 M9 延时截图

- **优先级**: P1 ｜ **复杂度**: S ｜ **开发者**: 待领取 ｜ **状态**: To Do
- **依赖**: 无
- **关联**: flameshot `flameshot gui -d <ms>` / capture launcher；winshot capture launcher 延时选项；`PrtscConfig.Hotkeys`

## 目标

抓悬浮提示、下拉菜单、右键菜单这类「点别处就消失」的场景——winshot 与 flameshot 都有、prtsc 完全没有的能力。

## 子任务

1. config：`CaptureConfig.DelaySeconds`（0/3/5 或整数字段，默认 0）。
2. 托盘菜单「延时截图」子项（3s/5s）+ 设置页入口（S-1 落地后）。
3. 触发路径：热键/菜单 → 倒计时（可选屏幕角标倒计时）→ 快照 → 进入选框流程；**overlay 与倒计时 UI 必须先隐藏/不出现，快照完成后才显示**。
4. CLI 预留 `--delay` 参数解析（L-1 实装时对齐）。

## 验收标准

- 3s 延时内能展开一个下拉菜单并被完整抓入选框；overlay 不污染快照。
- 延时期间再次按热键可取消（或明确设计为不可取消，二选一写进文案）。

## 测试用例

- 倒计时状态机纯逻辑单测（启动/取消/到点触发捕获回调）。
- 真机冒烟：抓下拉菜单（记入 QA-1）。
