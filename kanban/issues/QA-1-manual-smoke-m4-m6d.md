# QA-1 M4–M6d 真机人工冒烟（持续积压，P0）

- **优先级**: P0 ｜ **复杂度**: M ｜ **开发者**: 用户（真机）｜ **状态**: Testing / QA
- **依赖**: 无（随版本推进持续追加）
- **关联**: 历届 handoff「用户要求」节：不主动运行 Prtsc.exe，人工冒烟由用户执行；本卡是 M11 v1.0 的验收门之一

## 目标

M4 起所有功能只过了「编译 + 单测」，从未真机验证。v1.0 前必须清账。

## 测试清单（累积）

**M4–M5（标注）**
- TC1 四种捕获（全屏/区域/窗口热键）+ 选框 8-handles 调整
- TC2 11 工具全流程：V R E O A L T N S P H M B + Ctrl+滚轮（描边宽/效果强度）
- TC3 Select 移动/缩放/删除 + Undo/Redo(50) + 样式面板 + 浮动文本编辑 + Number 重排
- TC4 曲箭头控制点手柄

**M6a–M6b（合成/导出）**
- TC5 24 渐变背景 / Padding / 圆角 / 阴影 / Inset / 边框面板即时预览
- TC6 导出 PNG/JPEG/WebP；Ctrl+S 后 Auto-copy 状态栏提示

**M6c（剪贴板/QuickSave）**
- TC7 Copy → Word / 微信 / Chrome 粘贴互通；Chrome/Electron 复制 → Ctrl+V（已知风险：非 DIB 格式可能失败，B-1）
- TC8 Ctrl+Q 落盘到 `~/Pictures/Prtsc`（自定义目录 + 三种命名模式）

**B-1（PNG 剪贴板格式，2026-11-10 追加）**
- TC13 Chrome 右键复制图片 → Prtsc Ctrl+V 进编辑器（读路径：DIB 缺失/解不动时回退注册格式 "PNG"，TC7 已知风险闭环）；顺带回归 TC7 老读者粘贴不受影响
- TC14 Prtsc Copy → 只认 PNG 字节的读者（Figma 等）粘贴成功且透明背景不拍平（写路径：CF_DIBV5+CF_DIB+"PNG" 三格式同放，"PNG" 尽力放、放置失败不报错；像素 ≤4MP 才附 "PNG"，>4MP 只放 DIB——UI 线程同步编码阈值，I-1）；再验 Word/微信粘贴不受多放 "PNG" 格式影响

**M6d（裁剪/取色）**
- TC9 X 拖框裁剪 + 比例 ComboBox 吸附 + 越界标注 clip 表现 + 撤销裁剪恢复
- TC10 "A" 边缘取色：浅色窗口截图 → 背景变对应纯色

**M9（I-1 导入图片，2026-11-10 追加；细项见冒烟清单 §L）**
- TC11 Ctrl+O / ⋯ 菜单「打开图片文件…」：PNG/JPEG/WebP/BMP/GIF 解码 → 标注/导出全流程；「全部文件」选非图 → UnsupportedFormat 提示；取消无副作用
- TC12 HDROP 粘贴回归：资源管理器复制图片文件 → Ctrl+V 进编辑器（G-11 判定真值改共用 `ImageFileTypes`）

**前置条件**
- Windows 真机 + DPI 缩放 ≥125% 各验一轮；多显示器至少双屏各一轮。

## 验收标准

- 全部 TC 通过并在此卡记录（含 P0/P1/P2 问题分类）；发现的问题按 kanban 流程建卡。
