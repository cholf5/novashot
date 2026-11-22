using Novashot.Core.Capture;
using Novashot.Core.Common;
using Novashot.Core.Editor.Export;

namespace Novashot.Core.Cli;

/// <summary>解析结果：成功带 <see cref="Command"/>；失败带 <see cref="Error"/>（usage error，退出码 2）。</summary>
public sealed record CliParseResult(bool Success, CliCommand? Command, string? Error)
{
    public static CliParseResult Ok(CliCommand command) => new(true, command, null);
    public static CliParseResult Fail(string error) => new(false, null, error);
}

/// <summary>
/// L-1 CLI 参数解析纯函数（flameshot 精神）：<c>novashot gui|full|region|window --delay --path --clipboard --format --geometry</c>。
/// 输出 en 文案（CLI 输出不经 Loc 表）；非法输入一律带原因失败（调用方以 usage error 报告，不静默回退）。
/// 选项大小写不敏感；--geometry 只配 region；值取自下一 token（以 "--" 开头视为缺值）。
/// </summary>
public static class CliArgs
{
    /// <summary>--delay 钳制范围（秒）。CLI 语境是简单延时，与 D-1 的 {0,3,5} 合法值表无关。</summary>
    public const int MinDelaySeconds = 0;
    public const int MaxDelaySeconds = 60;

    public const string HelpText = """
        Novashot — flameshot-style command line capture.

        Usage:
          novashot [mode] [options]

        Modes:
          gui       Launch the GUI tray app (default with no arguments).
                    If an instance is already running, bring its window to front and exit.
          full      Capture the entire virtual desktop and export headlessly (no GUI).
          region    Capture a rectangle. Headless use requires --geometry X,Y,W/H (physical pixels,
                    virtual-desktop coordinates); without it, a running GUI instance opens the
                    region selection overlay, otherwise the command fails.
          window    Capture a window. With a running GUI instance the interactive capture overlay
                    is opened (hover a window and click to snap it, or drag for a region);
                    without one the command fails (no GUI is started).

        Options:
          -d, --delay <seconds>          Wait before capturing. 0-60, clamped. Default 0.
          -p, --path <dir>               Save the capture to <dir>; file name uses the configured
                                         filename pattern (novashot_<timestamp>.png by default).
                                         Missing directories are created.
          -c, --clipboard                Copy the capture to the system clipboard.
          -f, --format <png|jpeg|webp>   Export format. Default png.
              --geometry <X,Y,W,H>       Region rectangle (region mode only). Physical pixels,
                                         e.g. --geometry 0,0,1920,1080.
          -h, --help                     Show this help and exit.

        If neither --path nor --clipboard is given, the capture goes to the clipboard.

        Exit codes: 0 = success, 1 = failure, 2 = usage error.
        """;

    public static CliParseResult Parse(string[]? args)
    {
        args ??= Array.Empty<string>();

        var mode = CliCaptureMode.Gui;
        var modeSeen = false;
        var help = false;
        // null = 省略 --delay（与显式 --delay 0 的「立即抓」是两种意图，终审 I-1 后必须可区分）
        int? delay = null;
        PixelRect? geometry = null;
        string? path = null;
        var clipboard = false;
        var format = ExportFormat.Png;
        var exportTargetGiven = false;

        for (var i = 0; i < args.Length; i++)
        {
            var token = args[i];

            if (token is "-h" or "--help")
            {
                help = true;
                continue;
            }

            // 首个非选项 token = 模式（flameshot 子命令风格）；模式之前的捕获选项一律拒绝（防手滑丢模式）
            if (!modeSeen && !token.StartsWith('-'))
            {
                modeSeen = true;
                switch (token.ToLowerInvariant())
                {
                    case "gui": mode = CliCaptureMode.Gui; break;
                    case "full": mode = CliCaptureMode.Full; break;
                    case "region": mode = CliCaptureMode.Region; break;
                    case "window": mode = CliCaptureMode.Window; break;
                    default:
                        return CliParseResult.Fail($"Unknown mode '{token}'. Expected gui | full | region | window.");
                }
                continue;
            }

            if (!modeSeen)
                return CliParseResult.Fail("Missing capture mode (gui | full | region | window). Run 'novashot --help' for usage.");

            switch (token.ToLowerInvariant())
            {
                case "-c" or "--clipboard":
                    clipboard = true;
                    exportTargetGiven = true;
                    break;

                case "-d" or "--delay":
                {
                    if (!TryReadValue(args, ref i, out var value))
                        return CliParseResult.Fail("Missing value for --delay.");
                    if (!int.TryParse(value, out var seconds))
                        return CliParseResult.Fail($"Invalid --delay value '{value}'. Expected an integer number of seconds.");
                    delay = Math.Clamp(seconds, MinDelaySeconds, MaxDelaySeconds);
                    break;
                }

                case "-p" or "--path":
                {
                    if (!TryReadValue(args, ref i, out var value))
                        return CliParseResult.Fail("Missing value for --path.");
                    if (string.IsNullOrWhiteSpace(value))
                        return CliParseResult.Fail("Invalid --path value: directory must not be empty.");
                    path = value;
                    exportTargetGiven = true;
                    break;
                }

                case "-f" or "--format":
                {
                    if (!TryReadValue(args, ref i, out var value))
                        return CliParseResult.Fail("Missing value for --format.");
                    var parsedFormat = value.ToLowerInvariant() switch
                    {
                        "png" => ExportFormat.Png,
                        "jpeg" => ExportFormat.Jpeg,
                        "webp" => ExportFormat.WebP,
                        _ => (ExportFormat?)null,
                    };
                    if (parsedFormat is null)
                        return CliParseResult.Fail($"Invalid --format value '{value}'. Expected png | jpeg | webp.");
                    format = parsedFormat.Value;
                    break;
                }

                case "--geometry":
                {
                    if (!TryReadValue(args, ref i, out var value))
                        return CliParseResult.Fail("Missing value for --geometry.");
                    if (!TryParseGeometry(value, out var rect, out var error))
                        return CliParseResult.Fail($"Invalid --geometry value '{value}': {error}");
                    geometry = rect;
                    break;
                }

                default:
                    return CliParseResult.Fail($"Unknown option '{token}'. Run 'novashot --help' for usage.");
            }
        }

        // mode 定位约束：--geometry 只配 region（非法组合 → usage error）
        if (geometry is not null && mode != CliCaptureMode.Region)
            return CliParseResult.Fail("--geometry is only valid with the region mode.");

        // flameshot 对齐：未显式给导出目标时默认剪贴板（仅捕获模式；gui/无参数不适用）
        if (!exportTargetGiven && mode != CliCaptureMode.Gui)
            clipboard = true;

        return CliParseResult.Ok(new CliCommand
        {
            Mode = mode,
            DelaySeconds = delay,
            Geometry = geometry,
            Path = path,
            Clipboard = clipboard,
            Format = format,
            Help = help,
        });
    }

    /// <summary>
    /// 已知选项 token 全集（review M-1）：带值选项的下一个 token 若是其中之一，
    /// 判定为「缺值」而非吞成值——<c>novashot full -p -c</c> 不得把 <c>-c</c> 吞成路径值。
    /// 注意不能用「以 - 开头且非纯整数」一刀切拒绝：<c>--geometry -10,-20,300,400</c> 是合法负值。
    /// </summary>
    private static readonly HashSet<string> KnownOptionTokens = new(StringComparer.Ordinal)
    {
        "-c", "--clipboard", "-d", "--delay", "-p", "--path", "-f", "--format", "--geometry", "-h", "--help",
    };

    /// <summary>读取带值选项的下一个 token；缺失、以 "--" 开头、或命中已知选项 token → false。</summary>
    private static bool TryReadValue(string[] args, ref int index, out string value)
    {
        value = string.Empty;
        if (index + 1 >= args.Length) return false;
        var candidate = args[index + 1];
        if (candidate.StartsWith("--") || KnownOptionTokens.Contains(candidate)) return false;
        value = candidate;
        index++;
        return true;
    }

    /// <summary>"X,Y,W,H"（允许逗号后空格）→ <see cref="PixelRect"/>；复用 G-26 的 <see cref="RegionRectSpec"/> 语义。</summary>
    private static bool TryParseGeometry(string value, out PixelRect rect, out string error)
    {
        rect = default;
        error = string.Empty;

        var parts = value.Split(',');
        if (parts.Length != 4)
        {
            error = "expected four comma-separated integers X,Y,W,H.";
            return false;
        }

        if (!RegionRectSpec.TryParse(parts[0], parts[1], parts[2], parts[3], out rect, out var kind))
        {
            error = kind switch
            {
                RegionRectErrorKind.NotANumber => "X, Y, W and H must be integers.",
                RegionRectErrorKind.NonPositiveSize => "width and height must be greater than zero.",
                _ => "invalid rectangle.",
            };
            return false;
        }
        return true;
    }
}
