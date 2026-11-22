# B-8 Mac 剪贴板解码畸形载荷防护

- **优先级**: P2 ｜ **复杂度**: S ｜ **状态**: Done（2026-11-10） ｜ **来源**: B-1 独立审查 M-4 发现（越卡界未动，实证属实）
- **关联**: `src/Novashot.Platform.Mac/Clipboard/MacClipboardService.cs`（TryGetImage）、`src/Novashot.Core/Clipboard/PngCodec.cs`（B-1 新建，现成可复用）

## 目标

`MacClipboardService.TryGetImage` 对 public.png 载荷直接 `SKBitmap.Decode(png)` 无 try 包裹。B-1 审查者用仓库同版本 SkiaSharp 3.119.4 实证：畸形字节（垃圾/空数组/仅魔数/截断 IDAT）**均抛 `ArgumentNullException('codec')`** 而非返 null。剪贴板载荷是跨应用不可信数据，恶意/损坏 PNG 粘贴路径存在崩溃面。

## 子任务

1. 先核实调用链：TryGetImage 外层（粘贴入口）是否已有兜底 catch——若有，行为是「崩 vs 吞」仍需裁决；若无，直接崩溃面确认。
2. 修复：改用 Core `PngCodec.Decode`（畸形返 null 不抛，B-1 已有 5 例单测钉住）或等价 try/catch；TIFF 路径如有同类裸解码一并收编。
3. 若外层确有兜底且裁决为「保留现有语义」，卡片记 Verification 后关闭，不强行改。

## 验收标准

- 畸形 PNG 载荷（模拟跨应用粘贴）→ TryGetImage 返 null/降级，不抛异常出服务边界。

## Verification（2026-11-10 本会话收尾）

- **子任务 1 裁决（无外层兜底 → 走子任务 2）**：粘贴入口 `MainWindowViewModel.PasteFromClipboard`（Ctrl+V/工具栏 Paste）直接调 `_clipboard.TryGetImage()`，无 try/catch——异常会沿 RelayCommand 直上 UI 线程。服务边界内转 null 是唯一正确层。
- **修复落点**：
  - PNG 路径：`SKBitmap.Decode(png)` → Core `PngCodec.Decode`（B-1 既有，畸形返 null）→ 继续 TIFF 兜底，绝不出服务边界。
  - TIFF 路径（Skia 无 TIFF codec，走 NSBitmapImageRep 手工拷贝）：新增 `IsPixelCopySafe(w,h,bpr)` 守卫——正几何 / 单边上限（先比后乘，防 64 位 NSInteger 乘法回绕）/ 行宽 `[w*4, w*16]`（放行 16bit/通道+对齐 padding）/ 总像素 ≤ 1e8；拷贝前 `GetPixels()==Zero`（SKBitmap 分配失败）返 null；拷贝循环偏移改 64 位算术。
  - **收尾审查追修**（WIP 后自查）：① 守卫乘法 `(long)w*4`、`(long)w*h` 在极大 NSInteger 下 unchecked 回绕成负数可绕过比较——改单边上限先行再乘；② 拷贝循环 `y*(int)bpr` int 截断/回绕 = OOB 读（`bpr` 可为不匹配怪值）——偏移一律 long；③ 新增行宽上限 `w*16`（读不越 src 的充分条件，rep 分配=bpr*h 字节）。
- **测试**：服务级真剪贴板畸形载荷 Manual 用例（真板写 public.png/public.tiff 畸形字节：垃圾/仅魔数/真实 PNG 砍半/构造 IFD 天文数字几何）+ `IsPixelCopySafe` 决策表纯单测（含回绕陷阱 0x20000000000×2、行宽上限两界）+ 真板 Manual 类收敛到 `RealPasteboard` collection 串行锁（排障实证：NSPasteboard 全局单例，并行用例互踩）。畸形 PNG 三态抛 `ArgumentNullException('codec')`（SkiaSharp 3.119.4）断言不再出边界。
- **真机**：Manual 真板用例即真机覆盖，本机 macOS 26.5.2 实跑全绿（91/91，其中 Manual 9），不另开 QA-1 人工项。零长度载荷短路（PasteboardDataBytes length==0 → null）与空数组等价断言钉在 Core PngCodecTests（既有）。
- **测试基线**：Mac 腿 91/91（82 自动 + 9 Manual）；Core/App 不涉。

## 测试用例

- 畸形字节 → 解码层返 null 单测（可复用/引用 PngCodecTests 既有断言；Mac 服务层如可在 mac 腿加服务级测试）。
