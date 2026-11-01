# R-1 M10 Velopack 打包

- **优先级**: P1 ｜ **复杂度**: M ｜ **开发者**: 待领取 ｜ **状态**: To Do
- **依赖**: C-1 的 Updater 子任务需先与本卡协调方案（自研轮询 vs vpk UpdateManager），**先定再写**
- **关联**: 旧交接 M8 既定方向 Velopack；winshot `build/windows/installer/project.nsi`（NSIS 参照）

## 目标

可分发的 Windows 安装器 + 便携版，发布通道就绪。

## 子任务

1. `vpk` 打包流程：dotnet publish (self-contained 或框架依赖，先定) → Velopack 安装器 + 便携 zip。
2. 与 C-1 协调：若采用 vpk UpdateManager，C-1 的 Updater 改为接入 Velopack；`UpdateConfig.CheckOnStartup/SkippedVersion` 接通。
3. CI：GitHub Actions build+test+pack，Release 附产物与校验和。
4. 安装器元数据：图标、开始菜单项、自启注册表（`RegistryAutostartService` 已有）。

## 验收标准

- 干净 Windows 虚拟机安装 → 截图/标注/导出全流程可用；卸载残留可接受且有说明。
- 更新检查在打包版内可用（或明确降级为仅版本提示并记录）。

## 测试用例

- CI 打包产物可安装冒烟；版本号单调性检查。
