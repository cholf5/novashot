using Prtsc.Core.Annotation;
using Prtsc.Core.Capture;
using Prtsc.Core.Clipboard;
using Prtsc.Core.Config;
using Prtsc.Core.Editor.Export;
using Prtsc.Core.Utils;
using Prtsc.Core.Common;

namespace Prtsc.Core.Cli;

/// <summary>
/// L-1 headless 导出的一次执行结果（CLI stdout / 管道响应共用这份描述）。
/// </summary>
public sealed record CliExportResult
{
    public bool Success { get; init; }
    /// <summary>单行结果描述（含路径/字节数或失败原因），供 stdout / 管道 message 直接使用。</summary>
    public string Message { get; init; } = string.Empty;
    public string? SavedPath { get; init; }
    public long? FileBytes { get; init; }
    public bool CopiedToClipboard { get; init; }

    public static CliExportResult Ok(string message, string? savedPath = null, long? fileBytes = null, bool copied = false)
        => new() { Success = true, Message = message, SavedPath = savedPath, FileBytes = fileBytes, CopiedToClipboard = copied };

    public static CliExportResult Fail(string message) => new() { Success = false, Message = message };

    /// <summary>映射为管道响应（转发路径的 CLI 退出码：成功 0 / 失败 1）。</summary>
    public CliPipeResponse ToResponse() =>
        Success ? CliPipeResponse.Success(Message) : CliPipeResponse.Fail(CliExitCodes.Failure, Message);
}

/// <summary>
/// L-1 headless 导出执行器（Core、平台无关、不碰 Avalonia）：抓帧 → ImageExporter 编码 →
/// 落盘（--path，文件名走 FilenamePatterns 既有模式）/ 剪贴板（--clipboard），两者可并存。
/// CLI 进程（无实例自执行）与 GUI 接收方（转发命令）共用同一个执行器。
/// 导出语义：原始截图直出（AnnotationDocument 无标注渲染，不加编辑器 padding/背景——对齐 flameshot CLI）。
/// --delay = 简单延时（Task.Delay + 取消令牌），不复用 D-1 的 UI 倒计时状态机（边界记 handoff）。
/// </summary>
public sealed class CliCaptureExecutor
{
    private readonly ICaptureService _capture;
    private readonly IConfigStore _store;
    private readonly IClipboardService _clipboard;

    /// <summary>延时实现（默认 Task.Delay；测试注入免真实等待）。</summary>
    private readonly Func<int, CancellationToken, Task> _delay;

    public CliCaptureExecutor(ICaptureService capture, IConfigStore store, IClipboardService clipboard,
        Func<int, CancellationToken, Task>? delay = null)
    {
        _capture = capture ?? throw new ArgumentNullException(nameof(capture));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
        _delay = delay ?? ((seconds, ct) => Task.Delay(TimeSpan.FromSeconds(seconds), ct));
    }

    public async Task<CliExportResult> ExecuteAsync(CliCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        // 防御性分流：交互模式（region 缺 geometry / window）与 gui 不能走 headless。
        // 正常路径下 CLI 宿主在调用前已分流，这里兜底给清晰错误。
        if (!command.IsHeadlessExport)
        {
            return command.Mode switch
            {
                CliCaptureMode.Region => CliExportResult.Fail(
                    "Headless region capture requires --geometry X,Y,W,H (physical pixels, virtual-desktop coordinates)."),
                CliCaptureMode.Window => CliExportResult.Fail(
                    "Window capture needs interactive selection and requires a running GUI instance."),
                _ => CliExportResult.Fail($"Mode '{command.Mode}' has no headless export path."),
            };
        }

        try
        {
            if (command.DelaySeconds > 0)
                await _delay(command.DelaySeconds, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return CliExportResult.Fail("Cancelled before capture (delay interrupted).");
        }

        CapturedImage? image;
        try
        {
            ct.ThrowIfCancellationRequested();
            image = command.Mode == CliCaptureMode.Full
                ? _capture.CaptureFullscreen()
                : _capture.CaptureRegion(command.Geometry!.Value);
        }
        catch (OperationCanceledException)
        {
            return CliExportResult.Fail("Cancelled.");
        }
        catch (Exception ex)
        {
            return CliExportResult.Fail($"Capture failed: {ex.Message}");
        }

        try
        {
            return await ExportAsync(image, command, ct).ConfigureAwait(false);
        }
        finally
        {
            image.Dispose();
        }
    }

    private async Task<CliExportResult> ExportAsync(CapturedImage image, CliCommand command, CancellationToken ct)
    {
        var quality = _store.Current.Export.JpegQuality;
        var savedPath = (string?)null;
        long? fileBytes = null;
        var copied = false;
        var problems = new List<string>();

        // 1. --path 落盘（FilenamePatterns 既有模式；目录不存在自动创建）
        if (command.Path is { } folder)
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                Directory.CreateDirectory(folder);
                var extension = ImageExporter.ExtensionFor(command.Format);
                var name = FilenamePatterns.Generate(_store.Current.QuickSave.Pattern, folder, extension);
                var filePath = Path.Combine(folder, name);

                // ImageExporter 复用（M8 handoff 既定方向）：无标注 AnnotationDocument 渲染 = 原图。
                // AnnotationDocument 非拥有 Source、不实现 IDisposable；位图生命周期归 CapturedImage
                var document = new AnnotationDocument(image.Bitmap);
                using var data = ImageExporter.Encode(document, command.Format, quality);
                using (var stream = File.Create(filePath))
                {
                    data.SaveTo(stream);
                }
                savedPath = filePath;
                fileBytes = data.Size;
            }
            catch (OperationCanceledException)
            {
                problems.Add("Cancelled during export.");
            }
            catch (Exception ex)
            {
                problems.Add($"Saving to '{folder}' failed: {ex.Message}");
            }
        }

        // 2. --clipboard（拷贝即快照，位图所有权仍归本次执行，finally 统一 Dispose）
        if (command.Clipboard)
        {
            ct.ThrowIfCancellationRequested();
            if (_clipboard.TrySetImage(image.Bitmap))
                copied = true;
            else
                problems.Add("Failed to copy to clipboard.");
        }

        if (problems.Count > 0)
        {
            var prefix = savedPath is not null
                ? $"Saved to {savedPath} ({fileBytes} bytes) but export incomplete: "
                : string.Empty;
            // 已落盘的部分事实要保留在结果里（Partial 成功路径，方便脚本与测试辨认）
            return CliExportResult.Fail(prefix + string.Join(" ", problems)) with
            {
                SavedPath = savedPath,
                FileBytes = fileBytes,
            };
        }

        var parts = new List<string>(2);
        if (savedPath is not null)
            parts.Add($"Saved to {savedPath} ({fileBytes} bytes)");
        if (copied)
            parts.Add($"Copied to clipboard ({image.Width}x{image.Height})");
        return CliExportResult.Ok(string.Join("; ", parts), savedPath, fileBytes, copied);
    }
}
