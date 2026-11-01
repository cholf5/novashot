using System.Diagnostics;
using Microsoft.Extensions.Logging;
using SkiaSharp;
using Prtsc.Core.Clipboard;

namespace Prtsc.Platform.Linux.Clipboard;

/// <summary>
/// X11 剪贴板：进程外 xclip 兜底（避免自维护 X selection 所有权线程；xclip -l 1 常驻代持）。
/// - 写图：PNG 进 clipboard，xclip -l 1 代持直到被读取（进程退出前有效）。
/// - 读图：优先 image/png target，失败回退 image/tiff…实际常用 target 由 xclip -l 1 统一。
/// - 文本：UTF-8 string。
/// 检测：PATH 上找不到 xclip → 全部安全降级 false/null（记 warning）。
/// </summary>
public sealed class LinuxClipboardService : IClipboardService
{
    private readonly ILogger<LinuxClipboardService>? _logger;
    private readonly string? _xclipPath;
    private readonly string? _xselPath;

    public LinuxClipboardService(ILogger<LinuxClipboardService>? logger = null)
    {
        _logger = logger;
        _xclipPath = ResolveOnPath("xclip");
        _xselPath = ResolveOnPath("xsel");
        if (_xclipPath is null && _xselPath is null)
            _logger?.LogWarning("Neither xclip nor xsel found on PATH; clipboard will not work (install xclip)");
    }

    /// <summary>xclip/xsel 任一可用。测试可注入。</summary>
    public bool IsAvailable => _xclipPath is not null || _xselPath is not null;

    public bool TrySetImage(SKBitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        if (bitmap.Width <= 0 || bitmap.Height <= 0) return false;
        if (_xclipPath is null)
        {
            _logger?.LogWarning("TrySetImage skipped: xclip not available");
            return false;
        }

        using var png = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        if (png is null) return false;
        return RunXclipImage(png.ToArray());
    }

    public SKBitmap? TryGetImage()
    {
        if (_xclipPath is null) return null;
        var png = RunXclipRead(["-t", "image/png"]);
        if (png is { Length: > 0 })
        {
            var bmp = SKBitmap.Decode(png);
            if (bmp is not null) return bmp;
        }
        // 回退：不带 target 读默认（可能是 PNG 位流，也可能是别的）；Decode 失败即放弃
        var fallback = RunXclipRead([]);
        if (fallback is { Length: > 0 })
        {
            var bmp = SKBitmap.Decode(fallback);
            if (bmp is not null) return bmp;
        }
        return null;
    }

    public bool TrySetText(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        if (_xclipPath is null)
        {
            _logger?.LogWarning("TrySetText skipped: xclip not available");
            return false;
        }
        return RunXclipText(System.Text.Encoding.UTF8.GetBytes(text));
    }

    /// <summary>
    /// X11 无 CF_HDROP 对应物（文件复制走 text/uri-list，仅限本机文件且语义不同）。
    /// 与 mac 一致恒返回 null；粘贴回退链路里不会命中。
    /// </summary>
    public string? TryGetImageFilePath() => null;

    // ---- process helpers ----

    private bool RunXclipImage(byte[] png)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = _xclipPath!,
                ArgumentList =
                {
                    "-selection", "clipboard",
                    "-t", "image/png",
                    "-l", "1",           // 代持 1 次读取
                    "-i", "-",           // stdin
                },
                RedirectStandardInput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            })!;
            p.StandardInput.BaseStream.Write(png);
            p.StandardInput.BaseStream.Close();
            if (!p.WaitForExit(10_000)) { TryKill(p); return false; }
            return p.ExitCode == 0;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "xclip image write failed");
            return false;
        }
    }

    private bool RunXclipText(byte[] utf8)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = _xclipPath!,
                ArgumentList = { "-selection", "clipboard", "-l", "1", "-i", "-" },
                RedirectStandardInput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            })!;
            p.StandardInput.BaseStream.Write(utf8);
            p.StandardInput.BaseStream.Close();
            if (!p.WaitForExit(10_000)) { TryKill(p); return false; }
            return p.ExitCode == 0;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "xclip text write failed");
            return false;
        }
    }

    private byte[]? RunXclipRead(IReadOnlyList<string> extraArgs)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = _xclipPath!,
                ArgumentList = { "-selection", "clipboard", "-o" },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            })!;
            // extraArgs 在 -o 前才是 target 过滤：直接拼接（ArgumentList 顺序如上即可——-o 是输出）
            using var ms = new MemoryStream();
            p.StandardOutput.BaseStream.CopyTo(ms);
            if (!p.WaitForExit(10_000)) { TryKill(p); return null; }
            return p.ExitCode == 0 ? ms.ToArray() : null;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "xclip read failed");
            return null;
        }
    }

    private static void TryKill(Process p)
    {
        try { p.Kill(); } catch { }
    }

    internal static string? ResolveOnPath(string name)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in pathEnv.Split(':', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir, name);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}
