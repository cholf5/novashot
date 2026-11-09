using Prtsc.Core.Editor.Export;

namespace Prtsc.Core.Cli;

/// <summary>
/// L-1 CLI 捕获模式。Gui = 无参数 / <c>gui</c>（启动 GUI 托盘应用）；Full / Region / Window 为 flameshot 式捕获子命令。
/// </summary>
public enum CliCaptureMode
{
    Gui,
    Full,
    Region,
    Window,
}

/// <summary>
/// 解析后的 CLI 命令（L-1）。纯数据；语义分流由宿主做：
/// CLI 进程无实例时只允许 headless 分支（<see cref="IsHeadlessExport"/>）；
/// GUI 接收方对 <see cref="IsInteractive"/>（region 缺 geometry / window）触发交互捕获流程。
/// </summary>
public sealed record CliCommand
{
    public CliCaptureMode Mode { get; init; } = CliCaptureMode.Gui;

    /// <summary>
    /// 抓帧前的延时秒数（简单延时语义，非 D-1 倒计时状态机）；0-60 之外已钳制。
    /// null = 命令行省略 --delay：headless 无延时；转发交互模式传 null 给咽喉点 →
    /// <see cref="Prtsc.Core.Capture.CaptureDelayOptions.Resolve"/> 取 config 默认（转发 ≡ 本地触发，终审 I-1）。
    /// 显式 <c>--delay 0</c> = 直抓（压过 config 默认）。
    /// </summary>
    public int? DelaySeconds { get; init; }

    /// <summary>region 模式的物理像素矩形（虚拟桌面坐标，X/Y 可负）。缺省 = 交互选区（仅 GUI 接收方可用）。</summary>
    public Prtsc.Core.Common.PixelRect? Geometry { get; init; }

    /// <summary>--path 目标目录（落盘文件名走 <see cref="Prtsc.Core.Utils.FilenamePatterns"/> 既有模式）。</summary>
    public string? Path { get; init; }

    /// <summary>复制到剪贴板。--path 与 --clipboard 都没给时解析器默认置 true（对齐 flameshot）。</summary>
    public bool Clipboard { get; init; }

    /// <summary>导出编码格式，默认 PNG。</summary>
    public ExportFormat Format { get; init; } = ExportFormat.Png;

    /// <summary>--help / -h：打印用法后退出（本地处理，不转发）。</summary>
    public bool Help { get; init; }

    /// <summary>可 headless 直接执行的命令：full / region+geometry。</summary>
    public bool IsHeadlessExport =>
        Mode == CliCaptureMode.Full || (Mode == CliCaptureMode.Region && Geometry is not null);

    /// <summary>需 GUI 交互的命令：region 缺 geometry（overlay 选区）/ window（选窗）。</summary>
    public bool IsInteractive =>
        !IsHeadlessExport && Mode is CliCaptureMode.Region or CliCaptureMode.Window;
}
