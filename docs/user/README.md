# Prtsc 用户手册

按使用顺序读即可；每页只讲一个主题，操作路径以「设置 → 某分区」这样的界面位置为准。

| 页面 | 内容 |
|---|---|
| [01 捕获](01-capture.md) | 三种捕获、窗口选择器、延时截图、截图启动器、跨屏选框 |
| [02 标注](02-annotate.md) | 15 个工具、选区手柄、样式面板、撤销/删除 |
| [03 合成与输出](03-compose-export.md) | 画布参数、导出、快存、剪贴板、贴图窗 |
| [04 云上传与历史](04-cloud-library.md) | R2 / Google Drive、Library 历史、删除策略 |
| [05 设置中心](05-settings.md) | 六个分区逐项说明、托盘与自启、语言、更新 |
| [06 命令行](06-cli.md) | CLI 模式与选项、单实例语义、退出码、脚本示例 |
| [07 故障排查](07-troubleshooting.md) | 权限、Wayland、热键失效、SmartScreen、日志位置 |

## 术语

- **捕获（capture）**：抓取屏幕像素的动作，可全屏 / 区域 / 窗口。
- **overlay / 选框叠加层**：截图后盖在屏幕上的半透明层，用来拉选框、调手柄、取色。
- **编辑器**：标注与合成界面，左侧画布、右侧面板、底部工具坞。
- **合成（composition）**：把截图放进带背景/内边距/圆角/阴影/边框的画布里，导出时一并渲染。
- **QuickSave**：`Ctrl+Q` 不弹对话框直接落盘到固定目录。
- **Library**：截图历史（索引 + 缩略图），不是相册，只管 Prtsc 自己产生过的记录。
- **feed**：Velopack 更新源目录，应用从这里读 `releases.win.json` 判断新版本。

## 默认热键一览

| 快捷键 | 作用 | 可改 |
|---|---|---|
| `PrintScreen` | 全屏截图 | ✔ 设置 → 热键 |
| `Ctrl+PrintScreen` | 区域截图 | ✔ |
| `Ctrl+Shift+PrintScreen` | 窗口截图 | ✔ |
| `Ctrl+O` / `Ctrl+S` / `Ctrl+Q` / `Ctrl+C` / `Ctrl+V` | 导入 / 导出 / 快存 / 复制 / 粘贴 | ✘ |
| `Ctrl+U` / `Ctrl+L` / `Ctrl+P` | 上传 / 历史 / 贴图 | ✘ |
| `Ctrl+Z` / `Ctrl+Y` | 撤销 / 重做 | ✘ |
| `V R E O A L T N S P H M B I X` | 切换标注工具 | ✘ |
