# B-8 Mac 剪贴板解码畸形载荷防护

- **优先级**: P2 ｜ **复杂度**: S ｜ **状态**: Backlog ｜ **来源**: B-1 独立审查 M-4 发现（越卡界未动，实证属实）
- **关联**: `src/Prtsc.Platform.Mac/Clipboard/MacClipboardService.cs`（TryGetImage）、`src/Prtsc.Core/Clipboard/PngCodec.cs`（B-1 新建，现成可复用）

## 目标

`MacClipboardService.TryGetImage` 对 public.png 载荷直接 `SKBitmap.Decode(png)` 无 try 包裹。B-1 审查者用仓库同版本 SkiaSharp 3.119.4 实证：畸形字节（垃圾/空数组/仅魔数/截断 IDAT）**均抛 `ArgumentNullException('codec')`** 而非返 null。剪贴板载荷是跨应用不可信数据，恶意/损坏 PNG 粘贴路径存在崩溃面。

## 子任务

1. 先核实调用链：TryGetImage 外层（粘贴入口）是否已有兜底 catch——若有，行为是「崩 vs 吞」仍需裁决；若无，直接崩溃面确认。
2. 修复：改用 Core `PngCodec.Decode`（畸形返 null 不抛，B-1 已有 5 例单测钉住）或等价 try/catch；TIFF 路径如有同类裸解码一并收编。
3. 若外层确有兜底且裁决为「保留现有语义」，卡片记 Verification 后关闭，不强行改。

## 验收标准

- 畸形 PNG 载荷（模拟跨应用粘贴）→ TryGetImage 返 null/降级，不抛异常出服务边界。

## 测试用例

- 畸形字节 → 解码层返 null 单测（可复用/引用 PngCodecTests 既有断言；Mac 服务层如可在 mac 腿加服务级测试）。
