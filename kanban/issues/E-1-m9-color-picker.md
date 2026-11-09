# E-1 M9 屏幕取色器

- **优先级**: P2 ｜ **复杂度**: M ｜ **开发者**: batch4 实现者 ｜ **状态**: Done
- **依赖**: 无（建议在 D-1 后做，同为捕获层小件）
- **关联**: flameshot 招牌交互（放大镜取色 + 色值历史）；prtsc 已有 `EdgeColorExtractor`（像素访问路径可参考）

## 目标

flameshot 最显眼的交互：截图时鼠标处放大镜 + 实时色值，点击即取色。

## 子任务

1. 选框阶段按 `C` 进入/退出取色模式：放大镜（11×11 像素最近邻放大 + 网格 + 中心格高亮）跟随光标。
2. 浮签显示 HEX / RGB；点击 = 复制 HEX 到剪贴板 + 记入会话历史 + 抛 `StrokePicked`（外层经 StylePanel 既有路径写入编辑器描边色）+ 退出取色模式回选框（不确认选框）。
3. 色值历史（会话内最近 10 个，去重；面板历史行可再取 = 设为描边色）。
4. DPI：直接在 overlay 手里的本屏物理像素位图上按 image 空间坐标采样，零换算（125%/150% 天然正确）。

## 验收标准

- 取色值与实际屏幕像素一致（DPI 缩放 125%/150% 下也正确）。—— 采样路径按裁决保证；单测钉采样与坐标映射逻辑。
- 复制后任意输入框可粘贴 HEX。—— `IClipboardService.TrySetText`（CF_UNICODETEXT）路径。
- 色值历史面板可再取。—— StylePanel 预设 8 色下方历史行，点历史色 = `vm.Stroke` 既有设置路径。

## Verification（batch4，2026-11-10）

- **Core（RED→GREEN）**：`ColorSampler`（单点/邻域采样、越界钳制、1×1 位图、null 防御）、`ColorFormatter`（#rrggbb 小写对齐 ToCssColor / rgb(r, g, b)、alpha 不参与、可被 SKColor.Parse 回读）、`ColorHistory`（容量 10、RGB 去重上浮、挤旧、Changed 事件、快照语义）——31 个新单测。
- **App**：`MagnifierLayout` 摆放纯几何 7 测；`ColorPickerWiringTests` 10 测（PendingStrokeColor → EnterEditor → StylePanel.Stroke 既有路径、消费即清空、取消不泄漏、历史→面板联动/退订）；`ColorPickerOverlayTests` 7 个 headless 输入链测试（C 进/出模式、点击取色不产出选框、HEX 入剪贴板入历史、Esc 维持既有清选框/取消语义、隐身层不影响常态拖框）。
- **交互裁决落地**：C 切换模式；模式内点击取色后回选框；复制成功浮签 900ms「已复制」态后自动回选框；Esc 维持既有「清选框/取消整个捕获」（先退取色态再走既有分支）。
- **测试**：880 → 959 全绿（Core 558 + App 138×2TFM + Mac 80 + Linux 45；Mac 曾见 1 例已知偶发，重跑全绿）。
- **快照**：`PRTSC_SNAPSHOT=1` 新增 `artifacts/ui/50-overlay-picker.png`（放大镜 + 浮签视觉核对）。
- **真机待办**（进 handoff 诚实清单）：128/192 dpi 屏对比系统取色器；剪贴板粘贴；放大镜在 2x 屏的观感。
- **Review pass（2026-11-10，独立审查通过）**：I-1 C 键自动重复——Avalonia 12 无 IsRepeat/Timestamp，开关改绑 KeyUp（长按只翻转一次，headless 连发测试钉住）；I-2 抓帧位图释放再延一跳 `DispatcherPriority.Background`（compositor 回收在途帧）；M-1 浮签文字每帧整形一次、M-2 网格线注释改准、M-3 补 scale=1.5/2.0 映射断言、M-4 补 Enter 分支测试、M-7 旧测试位图同寿、M-8 删死代码 IsPicking；M-5/6/9 由 PM 记录不修。测试 959 → 963 全绿。
