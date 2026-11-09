# Prtsc 交接备忘 — 2026-11-10 E-1 M9 屏幕取色器

> batch4 单卡交付；结构仿 `261110-d1-g26-delayed-capture-handoff.md`。

## 0. TL;DR

- **E-1 完成**：overlay 选框阶段按 `C` 进/出取色模式——11×11 像素最近邻放大镜（每格 ≈12 设备像素 + 网格线 + 中心格高亮框）+ HEX/RGB 两行浮签跟随光标；**点击 = 复制 HEX 到剪贴板（`IClipboardService.TrySetText`）+ 记入会话 `ColorHistory` + 抛 `StrokePicked`（MainWindow 挂 `PendingStrokeColor`，进入编辑器时经 `StylePanelViewModel.Stroke` 既有路径写入）+ 退出取色模式回选框**（不确认选框）。
- **取色源裁决落地**：直接在 overlay 手里的本屏 `CapturedImage.Bitmap`（物理像素位图）上按 image 空间坐标采样，**零 DPI 换算**；光标 DIP → image 坐标走 OverlayWindow 既有路径 `round(dip × ScaleFactor)`，越界一律钳制。
- **历史**：会话级 `ColorHistory`（AppServices 单例，非 config 持久化）最近 10 色、RGB 去重上浮、挤旧、`Changed` 事件；入口在 StylePanel 预设 8 色下方的历史行（空则整行隐藏，5 列小色块 × 2 行），点历史色 = 设为描边色。
- **语义细则（简报裁决 + 一处解释性落地，见 §2.1）**：C 退出回选框；Esc 维持既有「清除选框 / 取消整个捕获」（先退取色态再走既有分支）；复制成功浮签 900ms「已复制」态（输入冻结）后自动回选框。
- 测试 880 → **959 全绿**（Core 558 = +31；App 138×2TFM = +24/TFM；Mac 80；Linux 45）；快照新增 `artifacts/ui/50-overlay-picker.png`。

## 1. 文件地图（本批增量）

```
src/Prtsc.Core/Editor/ColorSampler.cs            单点 Sample + 11×11 SampleNeighborhood（行优先、贴边钳制、null/零尺寸防御）；NeighborhoodRadius=5
src/Prtsc.Core/Editor/ColorFormatter.cs          ToHex（#rrggbb 小写，对齐 EditorConfigMapper.ToCssColor）+ ToRgb（rgb(r, g, b)）；alpha 不参与
src/Prtsc.Core/Editor/ColorHistory.cs            会话历史：容量 10、去重上浮、Changed 事件、快照语义（Colors 返回副本）
src/Prtsc.App/Controls/ColorMagnifier.cs         取色放大镜自绘控件（Control.Render，同 SelectionAdorner 风格）：121 色块 + 网格线 + 中心高亮 + 浮签；ICustomHitTest 整面接输入；CopiedFlash 态
src/Prtsc.App/Controls/MagnifierLayout.cs        摆放纯几何：网格以光标为中心（不贴边搬移，保「中心格=光标像素」不变量），浮签下→上翻转 + 安全边距钳制
src/Prtsc.App/Views/OverlayWindow.axaml(.cs)     +ColorMagnifier 层（盖在 ToolRingLayer 上）+ PickerHint 提示条；C 键切换（review I-1 起绑 KeyUp，见 §2.3）；EnterPickMode/ExitPickMode/OnPickColor；Closed 时摘位图引用
src/Prtsc.App/Controls/SelectionAdorner.cs       静态画笔迁 Immutable 变体（见 §2.2b，cross-thread compositor 偶发炸点）
src/Prtsc.App/ViewModels/MainWindowViewModel.cs  +PendingStrokeColor（ObservableProperty）；EnterEditor 末尾 ApplyPendingStroke（经 StylePanel.Stroke 既有路径，消费即清空）；ctor +colorHistory 参数
src/Prtsc.App/ViewModels/EditorViewModel.cs      ctor +colorHistory 透传给 StylePanelViewModel
src/Prtsc.App/ViewModels/StylePanelViewModel.cs  +HistoryColors/HasColorHistory（订阅 ColorHistory.Changed；Dispose 退订）
src/Prtsc.App/Controls/StylePanel.axaml(.cs)     +历史行（5 列小色块，ToolTip 显示色值，点=设描边色走 OnSwatchClick 既有路径）；RefreshSwatchStates 覆盖历史行
src/Prtsc.App/Themes/Controls.axaml              +swatch.small 变体（36×20）
src/Prtsc.App/AppServices.cs                     +ColorHistory 单例注册；MainWindowViewModel 工厂传入
src/Prtsc.App/Views/MainWindow.axaml.cs          region overlay 构造时传 clipboard + colorHistory；StrokePicked → PendingStrokeColor；OnAnyCancelled 清挂起色；displayShots 释放延一跳 Background（review I-2）
src/Prtsc.App/Localization/Loc.cs                +Overlay.PickerHint / Overlay.PickerCopied / Panel.History（×2 语言表）
tests/Prtsc.Core.Tests/Editor/                   ColorSamplerTests(12) + ColorFormatterTests(8) + ColorHistoryTests(11) = 31
tests/Prtsc.App.Tests/MagnifierLayoutTests.cs    7（摆放几何）
tests/Prtsc.App.Tests/ColorPickerWiringTests.cs  10（PendingStrokeColor→StylePanel 路径 + 历史→面板联动 + 退订）
tests/Prtsc.App.Tests/ColorPickerOverlayTests.cs 7（headless 输入链：C 切换/点击取色/剪贴板/历史/Esc 语义/隐身层零干扰）
tests/Prtsc.App.Tests/UiSnapshotTests.cs         +50-overlay-picker.png
kanban/issues/E-1-m9-color-picker.md             Done + Verification
```

## 2. 关键坑（新会话必读）

1. **SKBitmap 生命周期 = 渲染线程 SIGSEGV（本批最大坑，review I-2 后表述收敛）**：`ColorMagnifier.Source` 持有 overlay 抓帧位图的**非所有权引用**。旧测试/新测试都习惯「构造完 overlay 立刻 `bmp.Dispose()`」——旧代码构造时已 PNG 往返复制（`SkiaAvaloniaConvert.ToAvaloniaBitmap`）所以安全；放大镜改为直接持引用后，位图提前释放 + 渲染线程带着 dirty 标记进来 = macOS `sk_bitmap_get_info` SIGSEGV（headless 实测复现，6GB crash dump 钉死）。防线（review 后如实表述）：**确定性可达路径已堵死**——
   - 测试里位图与 overlay 同寿（`overlay.Closed += (_,_) => bmp.Dispose()`），与生产一致（MainWindow 在 Confirm/Cancel 后 Dispose displayShots，且 review I-2 起再推迟一跳 `DispatcherPriority.Background`，给 compositor 一个完整周期回收在途帧）；
   - `ColorMagnifier.Render`/`OnPointerMoved`/`OnPointerPressed`/`HitTest` 全部先查 `IsVisible` + 判空 + 零尺寸；
   - `OverlayWindow.Closed` 时 `_magnifier.Source = null`（先于 posted Dispose ≥1 个 dispatcher hop）。
   **残余窗口如实声明**：若某后端在非 UI 线程调用 `Control.Render`，理论上仍存在「已过 IsVisible 检查的在途帧 × 位图释放」的 µs 级窗口（SIGSEGV 非托管异常）——Background 优先级延一跳把它压到实践不可达，但不是逻辑上的绝对封死。**以后任何给 overlay 加自绘采样控件的，都必须走这套防线**。
2. **Avalonia 12 渲染线程会对 IsVisible=false 的控件触发 Render（dirty 残留时）**——别信「不可见就不渲染」的直觉，自绘控件 Render 第一行永远 `if (!IsVisible) return;`。
2b. **自绘控件的静态渲染资源必须用 Immutable 变体**（`ImmutableSolidColorBrush`/`ImmutablePen`，`Avalonia.Media.Immutable`）：mutable Brush/Pen 是 AvaloniaObject（线程仿亲），第一个碰到它的线程成为 owner；headless 多 session / 多窗口 compositor 在别的线程 AddRef 时炸「The calling thread cannot access this object」（ColorPickerOverlayTests 偶发 7 连炸实测钉死，8 连跑全绿验证修复）。每帧新建的画刷（放大镜 121 色块）同样用 Immutable——顺带避免 compositor 资源累积。SelectionAdorner 的静态画笔本次一并迁移。
3. **C 键空闲已核实 + 触发时机（review I-1）**：OverlayWindow 选框阶段原来只绑 Esc/Enter，C 无冲突；编辑器阶段无关（本功能只在 overlay）。开关绑在 **KeyUp** 而非 KeyDown——Avalonia 12 的 `KeyEventArgs` 无 `IsRepeat`/`Timestamp`（12.1.0 反编译核实），平台自动重复只连发 KeyDown，绑 KeyUp 后长按 C 只翻转一次、不闪屏不落随机态。交互含义：按住 C 时模式不变，松开才切换（用户可感知差异极小）。
4. **Avalonia 12 headless 输入 API 变了**：`KeyPress` 现签名是 `(Key, RawKeyModifiers, PhysicalKey, string? keySymbol)`，physicalKey 与 keySymbol 都必填，且 **`KeyPress` 只发 KeyDown，KeyUp 是独立的 `KeyRelease`**（12.1.0 反编译核实）——模拟完整按键要两个都调（测试 helper `TapKey`）；`Window` 不再 IDisposable（别 `using var`）。
5. **PendingStrokeColor 的语义**：进编辑器时应用（此时必无选中标注 → 落 CurrentStyle，与预设色点击同语义）；消费即清空；`OnAnyCancelled` 显式清空防泄漏进下一次会话。若用户取色后不确认也不取消（直接再按热键开新捕获），挂起色会留给下一次 EnterEditor——边缘场景，真机体验再定。
6. **放大镜网格刻意不贴边搬移**：网格永远以光标为中心（中心格 = 光标所指像素），出界部分由 overlay 裁剪（flameshot 同款）；贴边搬移会让「中心格高亮」与实际采样点脱节。避让只做在浮签上（下→上翻转 + 横向钳 8px 安全边距，`MagnifierLayout` 纯函数有测试）。
7. **HEX 大小写**：`ColorFormatter.ToHex` 小写 `#rrggbb`（对齐 Core `ToCssColor` 既有 config 风格，简报指定）；StylePanel 的 `StrokeHex` 显示仍是既有大写——两处各自内部一致，剪贴板内容 = 浮签所见。
8. **历史行只记取色点击**，不记预设色选择（预设 8 色已在面板上，再进历史是噪音）——设计取舍，写死在 `OnPickColor` 一处。
9. **「已复制」态**：复制成功后放大镜停留 900ms（DispatcherTimer），期间输入冻结（OnPointerMoved/Pressed 直接 return），浮签显示 `Overlay.PickerCopied`（"Copied {0}"）；期间按 C 可立即退出。剪贴板失败（Linux 无 xclip 等）则无闪留、立即退回选框——不报错不弹窗（取色主流程不被剪贴板故障打断，色已进历史可再取）。

## 3. 诚实清单（未经真机验证）

1. **macOS 真机**（本机 darwin，只跑了单测 + headless）：128 dpi 内置屏与 192 dpi 外接屏各取一次对比 macOS 数字测色器（Digital Colour Meter，注意它默认可能显示 sRGB 而非 native 值）；2x 屏上每格 12 设备像素的观感（理论 66 DIP 见方）。
2. **Windows 真机从没跑过**（延续现状）：C 键冲突（Windows 上无全局冲突预期）、CF_UNICODETEXT 粘贴进记事本/浏览器地址栏。
3. **Linux**：xclip 缺失时取色仍可用（复制静默失败、色入历史）——路径已测（Fake），真机未验。
4. 放大镜浮签在多屏边缘（overlay 各屏独立，跨屏边缘即本屏边缘）的避让只在本屏几何内计算——真机看一眼。
5. 取色模式下工具环隐藏、选框 hint 与取色 hint 互斥切换（两个 Border 按模式显隐）——切语言中途的刷新依赖 DynamicResource，覆盖没问题。
6. `PRTSC_SNAPSHOT=1 artifacts/ui/50-overlay-picker.png` 是直接驱动放大镜属性的视觉态（模式接线由 headless 测试锁定），不是完整模式态截图。

## 4. Review pass（2026-11-10 独立审查，发现清单见 /tmp/prtsc-sdd/batch4-review-findings.md）

审查结论：通过，无 Critical；2 Important + 9 Minor。本批随交付清掉 I-1/I-2 与 M-1/2/3/4/7/8，M-5/M-6/M-9 由 PM 记录不修（M-5 会话级实为进程级、M-6 多屏取色模式互不同步、M-9 挂起色边缘场景——均已在 §2/§3 有据可查）。

- **I-1（C 键长按自动重复）**：Avalonia 12 `KeyEventArgs` 无 `IsRepeat`/`Timestamp`（反编译核实，审查建议二选一里 Timestamp 不可用）→ 落地 **KeyUp 触发**：`OnKeyUp` 处理 `Key.C`，自动重复只连发 KeyDown 故长按只翻转一次；`OnKeyDown` 的 C 分支删除。钉住场景的新测试 `C_AutoRepeatKeyDownBurst_TogglesOnlyOnKeyUp`（RaiseEvent 直灌 5 连发 KeyDown 不翻转、每次 KeyUp 恰好翻转一次）。
- **I-2（位图释放加固一跳）**：`MainWindow.CaptureRegionAsync` 的 displayShots 释放从 `Dispatcher.UIThread.Post` 再推迟一跳到 `DispatcherPriority.Background`（新局部函数 `DisposeShotsAfterRenderSettles`，confirm/cancel/setup-fail 三条路径统一走它），给 compositor 完整周期回收在途帧；§2.1 措辞已从「杜绝」收敛为如实描述（确定性路径堵死 + µs 级残余窗口压到实践不可达）。
- **M-1**：放大镜浮签文字每帧整形一次（`BuildLabel` 产 `LabelVisual`，Size 测量与 Draw 共用同一组 FormattedText，原来 ×4 现在 ×2 且不再重复建）。
- **M-2**：网格线粗细注释改准（1 DIP，2x 屏即 2 设备像素；PM 裁决保持 DIP 语义不动实现）。
- **M-3**：`ClickInPickMode_SamplesExactPhysicalPixel_AtNonUnitScale` Theory（1.5/2.0）钉住 `round(dip × ScaleFactor)` 映射——1.5 下 (334,268)DIP → (501,402) 物理，2.0 下 (250,200)→(500,400)。
- **M-4**：`EnterInPickMode_ExitsPickMode_AndConfirmsSelection` 直接钉 Enter 分支（退取色态 + 确认整屏 region）。
- **M-7**：旧测试 `OverlayWindowInputTests.NewOverlay` 位图改 Closed 同寿（三道防线规矩回溯到既有测试）。
- **M-8**：删无消费方的 `OverlayWindow.IsPicking`。
- 测试 959 → **963 全绿**（Core 558 / App 142×2TFM / Mac 80 / Linux 45；App net10.0 连跑 6 次 + 本批全量两轮全绿）。

## 5. 下一步

- QA-1 真机冒烟补 E-1 项：128/192 dpi 对比系统取色器；复制 HEX 粘贴进输入框；历史行再取色。
- L-1 CLI 若做 `--pick-color`，复用点：`ColorSampler`/`ColorFormatter`（纯 Core，无 UI 依赖），放大镜 UI 不可复用（overlay 专属）。
