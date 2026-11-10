# B-2 WindowPicker 缩略图

- **优先级**: P3 ｜ **复杂度**: S ｜ **状态**: Done（真机项 QA-1 §P 留人工冒烟）｜ **来源**: M6c 交接遗留 backlog
- **Development Log 补充（2026-11-11）**：headless 玩法两坑——① `await Dispatcher.UIThread.InvokeAsync` 把任务完成绑在 UI 循环泵上，改 `Post` fire-and-forget 后加载任务在后台线程即可完成；② 测试方法体跑在 session 循环线程上，多段 Dispatch/Show 组合会在 `session.Dispose()` 自等挂死（dotnet-stack 实证 MoveNext→Dispose），改单 Dispatch 内联 `GetResult + RunJobs` 后 3 例 260ms 全绿。多段 Dispatch 模式对同套件其余测试保持不变（它们单 Dispatch 内无 UI 依赖）。
- **关联**: `src/Prtsc.App/Views/WindowPickerDialog.axaml(.cs)`

## 目标

窗口选择对话框加 96×54 实时缩略图（当前只有标题列表，认窗口靠猜）。

## 子任务

- [x] 缩略图获取：后台逐窗 `ICaptureService.CaptureWindow`（串行，避免 N 路并发抓屏），Core `PickerThumbnails.FitSize/Resize` 等比缩到 ≤96×54（小图不放大），SKBitmap→PNG 字节流→Avalonia Bitmap。
- [x] 失败/最小化降级：捕获异常→null→占位首字母（进程名首个字母数字大写，无则 "?"），绝不崩溃；`TryCreate` 全兜底 catch。
- [x] 代次失效：Refresh/关窗 bump 代次，旧结果不回写新行、过期位图由闭包负责 Dispose（所有权随 Post 移交，无跨代泄漏）。

## 验收标准

- [x] Picker 列表每项显示窗口缩略图（headless 接线测试：成功窗出图 ≤96×54）。
- [x] 模糊/最小化窗口降级为占位图标不崩溃（捕获抛异常路径测试钉住；真机视觉入 QA-1）。

## 测试用例

- [x] Core `PickerThumbnailsTests` 12 例（FitSize 决策表：16:9 满盒/4K/方图/竖图/小图不放大/恰好等于盒子/宽钳 round/退化输入 1px；Resize 尺寸正确源不改/不放大）。
- [x] App `WindowPickerThumbnailTests` 3 例（headless）：成功+抛异常降级+尺寸上限+占位首字母；Close 换代抑制全部回写；Refresh 换新行重载。
- [ ] 真机冒烟（真实窗口图清晰度/最小化窗口降级/多显示器 DPI）记入 QA-1 §P。

## Development Log

### 2026-11-11

- Core：`PickerThumbnails`（FitSize 纯函数 + Resize SKSurface 等比绘制，镜像 ThumbnailWriter 手法）。
- App：`WindowPickerDialog` Row 升级 INotifyPropertyChanged（Thumbnail/HasThumbnail/Initial，SetThumbnail/DisposeThumbnail 位图所有权归 Row）；item 模板改横向 108×60 缩略图框（Image+占位首字母 Panel）；`LoadThumbnailsAsync` 串行逐窗捕获→TryCreate→`Dispatcher.UIThread.Post` 回写（代次三查：循环头/捕获后/闭包内）；ctor 增 ICaptureService 注入（MainWindow 传 `_capture`）；`CurrentLoadTask`/`Rows` internal 供测试。
- **测试踩坑**：首版 `await Dispatcher.UIThread.InvokeAsync(...).ConfigureAwait(false)`——Avalonia 12 的 `InvokeAsync(Action)` 返回 DispatcherOperation 不可 ConfigureAwait；且 headless 下 await 它会把任务完成绑在 UI 循环泵上。改 `Post`（fire-and-forget）后任务在后台线程即可完成，测试线程直 await 无死锁。
- **环境坑**：一次被杀的测试运行留下僵尸 testhost 锁住 bin DLL，后续 `dotnet test` 卡 MSBuild 拷贝——kill 残留进程后恢复。
- 测试：Core 773→785（+12）；App 203→206×2TFM（+3）。单 TFM 反复全绿；windows TFM 双 TFM 合跑实测 206/206 绿；net10.0 腿在双 TFM 合跑偶发 abort（stash 基线复现的存量并行 flake，B-6 issue 已记录根因）。

## Verification

- Related files: `src/Prtsc.Core/Windows/PickerThumbnails.cs`, `src/Prtsc.App/Views/WindowPickerDialog.axaml(.cs)`, `src/Prtsc.App/Views/MainWindow.axaml.cs`
- How to run/verify: `dotnet test tests/Prtsc.Core.Tests --filter PickerThumbnailsTests`；`dotnet test tests/Prtsc.App.Tests -f net10.0 --filter WindowPickerThumbnailTests`；真机开窗口捕获 picker 目视
- Results: Core 785/785；App 206/206（net10.0 与 net10.0-windows 均实测）；Mac 91/91；Linux 46/46；真机冒烟入 QA-1 §P
