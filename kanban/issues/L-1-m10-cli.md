# L-1 M10 CLI

- **优先级**: P2 ｜ **复杂度**: M ｜ **开发者**: 待领取 ｜ **状态**: To Do
- **依赖**: D-1（--delay 参数语义对齐）
- **关联**: flameshot CLI（`flameshot gui|full|screen|region -d -p -c`）；handoff 既定方向「headless 导出直接复用 `ImageExporter`」

## 目标

flameshot 式命令行：脚本化/第三方启动器调用截图。

## 子任务

1. 参数解析：`prtsc gui|full|region|window --delay <s> --path <dir> --clipboard --format png|jpeg|webp`。
2. 单实例模型：已有实例经命名管道转发命令后退出（无实例则自带 headless 模式）。
3. headless 路径：抓帧（GDI/WGC 复用 Platform.Windows）→ `ImageExporter` 直接落盘/剪贴板，不开 GUI。
4. D-1 预留的 `--delay` 实装。

## 验收标准

- `prtsc full --clipboard` 在无 GUI 交互下 2s 内完成；`--path` 落盘文件名走 `FilenamePatterns`。

## 测试用例

- 参数解析纯函数单测（含非法值回退）；管道转发协议单测；真机冒烟记入 QA-1。
