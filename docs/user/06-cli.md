# 06 命令行

`prtsc` 与 GUI 共用同一份配置、同一套捕获/导出实现。Windows 上就是安装目录里的 `Prtsc.exe`（可自行加进 PATH）；macOS 上是 `Prtsc.app/Contents/MacOS/Prtsc`；Linux 用源码构建出的可执行文件。

## 模式

| 模式 | 行为 |
|---|---|
| `prtsc`（无参数）/ `prtsc gui` | 启动 GUI；**已有实例在跑**则把主窗口前置（原实例接管），本进程退出 |
| `prtsc full` | 全屏截图；没有 GUI 也能 headless 完成 |
| `prtsc region --geometry X,Y,W,H` | 区域截图；有 GUI 实例时可以不带 `--geometry`（会弹出选框），没有实例时必须给几何 |
| `prtsc window` | 窗口截图；需要已运行的 GUI 实例弹选择器，没有实例直接报错（不会偷偷拉起 GUI） |

`--help` 打印完整用法，内容与下表一致。

## 选项

| 选项 | 说明 |
|---|---|
| `-d, --delay <秒>` | 抓帧前等待，0–60（越界钳制）；省略 = headless 不延时、转发给 GUI 时用设置里的默认延时 |
| `-p, --path <目录>` | 落盘目录，不存在自动创建；文件名按配置的命名模式 |
| `-c, --clipboard` | 复制到剪贴板 |
| `-f, --format <png\|jpeg\|webp>` | 导出格式，默认 png |
| `--geometry X,Y,W,H` | 仅 `region`；物理像素，虚拟桌面坐标（多屏时 X/Y 可为负） |
| `-h, --help` | 帮助 |

`--path` 与 `--clipboard` 都不给时，默认走剪贴板（对齐 flameshot）。

## 单实例语义

- 第一次启动的进程持有命名管道（按用户隔离）
- 后续任何命令都会把**原始命令行**整条转发给已运行实例，由它执行，退出码透传
- 因此脚本里连续调用不会堆进程；也不会出现「两个托盘图标」
- GUI 无实例时冷启动探测有约 0.1s 上限，不会拖慢正常启动

## 退出码

| 码 | 含义 |
|---|---|
| `0` | 成功 |
| `1` | 失败（捕获失败、导出失败、转发失败、交互模式无 GUI 等） |
| `2` | 用法错误（未知选项、缺值、非法几何等） |

## 脚本示例

```bash
# 每小时存一张全屏到归档目录（文件名自动带时间戳）
0 * * * * prtsc full --path ~/Pictures/archive

# 只为剪贴板：延时 3 秒，等下拉菜单展开
prtsc full --delay 3 --clipboard

# 指定区域导出 WebP（双屏副屏左上角 1920×1080）
prtsc region --geometry 1920,0,1920,1080 -f webp -p /tmp/shots

# 已有 GUI 在跑时弹选框（不指定几何），成功后把路径写进日志
prtsc region -p ~/Pictures && echo "saved"
```

Windows PowerShell 里同理，注意把路径引号包好：

```powershell
prtsc full --clipboard --delay 3
prtsc region --geometry 0,0,1280,720 -p "$env:USERPROFILE\Pictures"
```

## 注意

- CLI 的输出语言固定为英文（`stdout` 是脚本契约，不随界面语言变化）；日志只写文件，不混进 stdout
- `--delay` 是简单等待，不是托盘那个带倒计时的延时（后者有角标 UI）；Ctrl+C 可中断等待
- headless 执行时若平台层不可用（如 macOS 未授权屏幕录制、Linux 没有 DISPLAY），会以退出码 1 失败并在 stderr 给出原因
