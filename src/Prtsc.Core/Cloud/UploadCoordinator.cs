using Prtsc.Core.Annotation;
using Prtsc.Core.Clipboard;
using Prtsc.Core.Config;
using SkiaSharp;
using Prtsc.Core.Editor;
using Prtsc.Core.Editor.Export;
using Prtsc.Core.Library;
using Prtsc.Core.Utils;
using Microsoft.Extensions.Logging;

namespace Prtsc.Core.Cloud;

/// <summary>一次编排的结果：Ok/Error 来自上传服务，Entry 为已入库的 Library 记录（失败时 null）。</summary>
public sealed record UploadOutcome(bool Ok, string? Url, string? Error, LibraryEntry? Entry);

/// <summary>
/// 上传编排（M7b）：渲染编码 → 上传 → 记 Library。
/// 与 UI 解耦：MainWindowViewModel 只调本类，成功后再做"复制 URL"这类交互。
/// </summary>
public sealed class UploadCoordinator
{
    private readonly IUploadService _upload;
    private readonly IConfigStore _configStore;
    private readonly LibraryStore _library;
    private readonly string _thumbsDirectory;
    private readonly ILogger? _logger;

    public UploadCoordinator(IUploadService upload, IConfigStore configStore, LibraryStore library,
        ILogger? logger = null, string? thumbsDirectory = null)
    {
        _upload = upload ?? throw new ArgumentNullException(nameof(upload));
        _configStore = configStore ?? throw new ArgumentNullException(nameof(configStore));
        _library = library ?? throw new ArgumentNullException(nameof(library));
        _thumbsDirectory = thumbsDirectory ?? AppDataPaths.Thumbs;
        _logger = logger;
    }

    /// <summary>
    /// 渲染当前文档（含合成状态）为字节并上传；成功后写入 Library 索引。
    /// </summary>
    public async Task<UploadOutcome> UploadDocumentAsync(
        AnnotationDocument document, EditorState? state,
        string? exportFormat = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        var config = _configStore.Current;

        var format = QuickSave.ParseFormat(exportFormat ?? config.Export.DefaultFormat);
        byte[] bytes;
        using (var data = ImageExporter.Encode(document, format, config.Export.JpegQuality, state))
            bytes = data.ToArray();

        var fileName = FilenamePatterns.Generate(
            config.QuickSave.Pattern, folder: string.Empty, extension: ImageExporter.ExtensionFor(format));

        UploadResult result;
        try
        {
            result = await _upload.UploadAsync(
                new UploadRequest(fileName, bytes, ContentTypeFor(format)), ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Upload of {File} threw unexpectedly", fileName);
            result = UploadResult.Fail(ex.Message);
        }

        if (!result.Ok)
        {
            _logger?.LogWarning("Upload of {File} failed: {Error}", fileName, result.Error);
            return new UploadOutcome(false, null, result.Error, null);
        }

        var size = state is null
            ? new SKSizeI(document.Width, document.Height)
            : Composition.Measure(document, state);
        var entry = new LibraryEntry
        {
            CreatedAt = DateTimeOffset.Now,
            FileName = fileName,
            LocalPath = string.Empty, // 上传不落用户可见的本地文件；Library 仅留元数据
            RemoteUrl = string.IsNullOrEmpty(result.Url) ? null : result.Url,
            Provider = NormalizeProvider(config.Cloud.Provider),
            Width = size.Width,
            Height = size.Height,
            SizeBytes = bytes.Length,
        };
        try
        {
            entry = await _library.AddAsync(entry, ct).ConfigureAwait(false);
            WriteThumbnailSafely(entry, bytes);
        }
        catch (Exception ex)
        {
            // 上传已成功：Library 记录失败只降级为警告，不吞掉成功结果；Entry 置空表示未入库
            _logger?.LogWarning(ex, "Library record failed for {File} after successful upload", fileName);
            entry = null;
        }

        return new UploadOutcome(true, result.Url, null, entry);
    }

    /// <summary>上传入口的"未配置/无渠道"文案统一从这来。</summary>
    public static string NotConfiguredHint => "云上传未配置：请在 设置 → 云上传 选择 R2 或 Google Drive 并填写凭据。";

    /// <summary>缩略图失败不影响上传结果（仅警告）。</summary>
    private void WriteThumbnailSafely(LibraryEntry entry, byte[] imageBytes)
    {
        try
        {
            using var bitmap = SKBitmap.Decode(imageBytes);
            if (bitmap is null) return;
            ThumbnailWriter.Write(_thumbsDirectory, $"thumb_{entry.Id}.png", bitmap);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Thumbnail write failed for {Id}", entry.Id);
        }
    }

    /// <summary>渠道名归一：空/none → null；其余小写去空白。</summary>
    public static string? NormalizeProvider(string? provider) =>
        string.IsNullOrWhiteSpace(provider) || provider.Trim().Equals("none", StringComparison.OrdinalIgnoreCase)
            ? null
            : provider.Trim().ToLowerInvariant();

    public static string ContentTypeFor(ExportFormat format) => format switch
    {
        ExportFormat.Jpeg => "image/jpeg",
        ExportFormat.WebP => "image/webp",
        _ => "image/png",
    };
}
