# Kanban Board — Prtsc

> 建板 2026-11-01（M6d 完成后）。M1–M6d 历史见 Done 列汇总卡与 `docs/handoff/`。
> 卡片约定沿用 cholf5 看板工作流：目标 / 子任务 / 开发者 / 复杂度(S/M/L) / 验收标准 / 测试用例 / 关联文档 / 依赖，明细在 `issues/<卡号>.md`。
> 里程碑总表与缺口来源见 `docs/roadmap.md`。

## Backlog
- X-1 反色工具（PixelEffect 基建现成，可选）（P3）
- B-1 粘贴支持 PNG 剪贴板格式（RegisterClipboardFormat("PNG")，浏览器/Electron 互操作）（P2）
- B-2 WindowPicker 96×54 缩略图（P3）
- B-3 跨屏选框（多显示器 overlay 覆盖虚拟桌面）（P2）
- B-4 crop 后 reshape 手柄（复用 BoxHandles）（P3）
- B-6 工具栏按钮自定义（flameshot 式可见工具配置）（P3）

## To Do
- C-1 M7 云上传/Library/Updater/Pin（R2=S3 兼容 PUT + GDrive multipart + Library(library.json+缩略图) + Updater + Pin 贴图；凭据键 Prtsc_R2_*/Prtsc_GDrive_*，不迁移 WinShot 凭据）（P1）
- S-1 M8 设置中心 GUI（常规/启动/热键录制/QuickSave/导出质量滑杆/编辑器默认值/WGC 光标开关；保存后热键重注册；吸收 backlog 导出滑杆与光标开关两项）（P1）
- D-1 M9 延时截图（托盘菜单 3s/5s + Capture.DelaySeconds；overlay 不得先于快照出现；winshot/flameshot 均有而 prtsc 缺）（P1）
- E-1 M9 屏幕取色器（放大镜 + HEX/RGB 浮签 + 点击复制，可设为描边色；flameshot 招牌交互）（P2）
- N-1 M9 桌面通知（托盘气球通知，导出/QuickSave/上传/复制触发，ShowNotification 字段已备）（P2）
- I-1 M9 Ctrl+O 导入图片文件（复用粘贴路径；注意 CapturedImage 所有权坑）（P2）
- L-1 M10 CLI（prtsc full|region|window --delay --path --clipboard --format；单实例命名管道转发）（P2）
- L-2 M10 i18n（resx 抽串 + zh-CN 首发，key 一致性单测）（P2）
- R-1 M10 Velopack 打包（安装器 + 便携版；与 C-1 Updater 子任务协调采用 vpk UpdateManager）（P1）

## In Progress
- 

## In Review
- 

## Testing / QA
- QA-1 M4–M6d 真机人工冒烟（11 工具标注 + 选择/撤销 + 样式面板 + 裁剪(X/比例/A 取色) + 合成面板 + Copy/Ctrl+V/Ctrl+Q + Word/微信/Chrome 剪贴板互通 + 三种捕获 + 托盘 + WindowPicker；自 M4 起持续积压，全部只过了编译+单测）（P0）

## Blocked
- 

## Done
- M1–M6d 里程碑汇总（脚手架/捕获/选框 → 标注工具集 → 手绘与区域效果 → 合成管线/剪贴板/裁剪+边缘取色；测试基线 351/351，详见 issues/done-m1-m6d.md）（2026-11-01）
