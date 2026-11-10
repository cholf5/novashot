# B-1 粘贴支持 PNG 剪贴板格式

- **优先级**: P2 ｜ **复杂度**: S ｜ **状态**: Done ｜ **来源**: M6c 交接 §5 遗留
- **关联**: `src/Prtsc.Platform.Windows/Clipboard/WindowsClipboardService.cs`

## 目标

当前粘贴只认 CF_DIBV5/CF_DIB；浏览器/Electron 应用（Chrome、微信截图等）常只放 PNG 格式，导致从它们粘贴失败。

## 子任务

1. `RegisterClipboardFormat("PNG")` 读取路径，DIB 缺失时回退 PNG 字节解码。
2. 写入侧评估是否同时放 PNG 格式（提升对其他工具的互操作）。

## 验收标准

- Chrome 里右键复制图片 → Prtsc Ctrl+V 成功进入编辑器。

## 测试用例

- PNG 字节 → SKBitmap 解码单测；格式优先级判定单测；真机互操作记入 QA-1（TC13）。

## Verification（2026-11-10 审查记录）

- 「解码失败继续试下一候选」行为微变（M6c 原实现畸形 DIB 整体 null 收场）：平台层 Win32 进不了测试架构，由 QA-1 TC13 真机覆盖。
- PNG 读路径无载荷上界；Skia 分配失败干净返 null（审查实证）。后续如加防护，在 `PngCodec.Decode` 入口按 IHDR 宽高上限检查。
- 大图 PNG 编码阈值裁决（I-1）：同步编码在 UI 线程，实测（Apple Silicon）1080p ~72ms / 4K ~278ms / 4K 噪声最坏 ~992ms。取**像素阈值 4MP**（`ClipboardImageFormats.InteropEncodeMaxPixels`）而非移线程——移线程要么引入「Copy 返回时 PNG 尚未就位」的时序窗口（互操作读者读不到），要么做 Win32 延迟渲染（WM_RENDERFORMAT 消息泵，复杂度不成比例）；阈值语义简单，超界只降互操作不减功能。
