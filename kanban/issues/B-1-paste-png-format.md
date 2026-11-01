# B-1 粘贴支持 PNG 剪贴板格式

- **优先级**: P2 ｜ **复杂度**: S ｜ **状态**: Backlog ｜ **来源**: M6c 交接 §5 遗留
- **关联**: `src/Prtsc.Platform.Windows/Clipboard/WindowsClipboardService.cs`

## 目标

当前粘贴只认 CF_DIBV5/CF_DIB；浏览器/Electron 应用（Chrome、微信截图等）常只放 PNG 格式，导致从它们粘贴失败。

## 子任务

1. `RegisterClipboardFormat("PNG")` 读取路径，DIB 缺失时回退 PNG 字节解码。
2. 写入侧评估是否同时放 PNG 格式（提升对其他工具的互操作）。

## 验收标准

- Chrome 里右键复制图片 → Prtsc Ctrl+V 成功进入编辑器。

## 测试用例

- PNG 字节 → SKBitmap 解码单测；格式优先级判定单测；真机互操作记入 QA-1（TC7）。
