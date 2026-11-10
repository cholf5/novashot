# B-7 Library 删除记录时可选连磁盘文件一起删

- **优先级**: P3 ｜ **复杂度**: S ｜ **状态**: Done（2026-11-11） ｜ **来源**: L-2 审计确认的行为裁决（safer 默认）+ 删除确认对话框文案语义修正需求
- **关联**: `src/Prtsc.App` LibraryWindow（删除确认对话框）、Core Library 数据层（library.json 记录删除）

## 目标

现状：Library 删除只删 library.json 记录、磁盘文件保留（L-2 审计时定的 safer 默认）。删除确认对话框文案与实际行为语义已对齐（只说删记录），但用户没有「连文件一起删」的入口。本卡给删除加可选联动：默认仍只删记录，用户显式勾选才删磁盘文件。

## 子任务

1. 删除确认对话框加「同时删除磁盘文件」勾选项（默认不勾，保持 safer 语义；zh/en 文案进 Loc 双语表）。
2. 勾选后：删除记录 + 删磁盘文件（文件不存在时静默降级只删记录，不报错）。
3. 删除失败的错误处理（文件被占用/无权限）：不崩，提示保留记录或降级——设计时定，报告里给裁决。

## 验收标准

- 默认勾选不勾 → 行为与现状完全一致（只删记录）。
- 勾选 → 记录消失且文件从磁盘删除；文件已不存在时不报错。

## 测试用例

- 删除决策纯逻辑单测（记录删除 / 文件删除失败降级）。
- 真机勾选行为入 QA-1（Library 段）。

## Verification（2026-11-11 本会话收尾）

- **落点**：Core `LibraryFileDeletePolicy`（纯决策表：NotRequested / NoLocalFile / FileAlreadyGone / Deleted / FailedButRecordDeleted）+ `LibraryFiles.TryDelete`（尽力删除，失败吞 IOException 系返 false；**FileNotFoundException/DirectoryNotFoundException → true**——「目标不存在」按验收「不报错」静默，且跨平台口径一致：POSIX 上 File.Delete 对 ENOENT 抛 FileNotFoundException、缺父目录抛 DirectoryNotFoundException，Windows 为 no-op）+ `LibraryStore.RemoveAsync(id, deleteLocalFile, ct, tryDelete?)` 新 overload（ioLock 内**记录先提交**再删文件；tryDelete 供测试注入失败/验证调用时机）；App `LibraryViewModel.DeleteAsync(item, deleteFile=false)`（失败时状态栏 `Library.FileDeleteFailed` 警告 + logger 留痕）+ LibraryWindow 确认框 CheckBox（仅 LocalPath 非空时显示，默认不勾；IsChecked **点击一刻读取**）+ Loc 双语 `Library.DeleteAlsoFile` / `Library.FileDeleteFailed`。
- **裁决记录**（子任务 3）：文件删除失败 → **记录保持已删、不回滚**（library.json 是记录索引，复活记录违背用户已确认的删除意图），UI 状态栏提示 + logger warning，不崩；文件不存在 → 静默只删记录（FileAlreadyGone / 缺父目录同口径）。M6 备案：勾选态下静态 detail 行「文件仍保留在磁盘」与勾选意图并存——detail 描述默认行为、勾选框是显式覆盖，属刻意保留不改动态文案。
- **TDD 实证**：RED 阶段抓到真缺陷「删除 IO 未以 deleteLocalFile 为前提」（默认模式误删磁盘文件）——修复为 `deleteLocalFile && exists && TryDelete` 前置短路，并以 store 层 `Remove_DefaultOption_NeverTouchesDeleteIO`（throwing seam）钉住回归位。
- **独立 review**：1 Critical + 2 Important + 6 Minor，**全处置**：C1 勾选框 IsChecked 在对话框构建时采样（点删除时读的是固化 false——勾选功能形同虚设，测试全绿因直驱 VM/Store 绕过 View 层）→ 改为 Click 处理器内读 checkbox；I1 `TryDelete` 异常过滤器零覆盖 → `LibraryFilesTests` 4 例（真实 IO 成功/不存在路径两形态/目录路径不抛），实证发现并修正 POSIX ENOENT 语义；I2 确认框 OK/Cancel 误用 `Close()` 关掉的是 Library 主窗口 owner（G-17 起既有缺陷，对照 HEAD 证实非本次引入）→ 顺手改为 `dialog.Close()`；M1 store 层钉「默认不触删除 IO」+ seam 收参断言；M5 LibraryWindow 生产构造未传 logger → 补上；M2/M3/M4 备案（未知 Id 复用 NotRequested 已在枚举注释声明 / 锁内同步 IO 与 TOCTOU 良性、标签竞窗只影响结果标签 / `RemoveAsync(id, default)` 字面量勿写的提示）；M6 裁决备案见上。
- **测试**：`LibraryFileDeletePolicyTests` 6 例（决策表全分支 + 空路径 Theory）+ `LibraryStoreDeleteFileTests` 9 例（真文件删除/文件已失/纯上传记录/注入失败记录仍删/seam 仅文件存在时被调且收 entry.LocalPath/未知 Id/默认 overload 不动文件兜底）+ `LibraryFilesTests` 4 例 + `LibraryViewModelDeleteTests` 3 例（默认保留文件/勾选连删/失败警告文案 `Loc.T` 对齐）。全量 Core 741→760 + App 190→193×2TFM + Mac 91 + Linux 46 全绿。
- **真机**：QA-1 TC18（勾选连删/默认只删/文件已被外部移走勾选删除静默降级）。
