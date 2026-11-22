using Novashot.Core.Annotation;
using Novashot.Core.Config;
using Novashot.Core.Editor;
using Novashot.Core.Editor.Export;
using Novashot.Core.Utils;

namespace Novashot.Core.Clipboard;

/// <summary>
/// QuickSave（M6c）：按 config（QuickSave.Folder/Pattern + Export.DefaultFormat/JpegQuality）
/// 无对话框直存当前合成结果。目录空 = ~/Pictures/Novashot；目录不存在自动创建。
/// </summary>
public static class QuickSave
{
    /// <summary>解析目标目录：空/空白 → ~/Pictures/Novashot，否则原样返回（不建目录）。</summary>
    public static string ResolveFolder(string configuredFolder)
    {
        if (string.IsNullOrWhiteSpace(configuredFolder))
        {
            var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            return Path.Combine(pictures, "Novashot");
        }
        return configuredFolder;
    }

    /// <summary>
    /// 生成完整保存路径（含目录自动创建）。文件名走 <see cref="FilenamePatterns"/> 三模式。
    /// </summary>
    public static string BuildFilePath(NovashotConfig config, DateTime? now = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        var folder = ResolveFolder(config.QuickSave.Folder);
        Directory.CreateDirectory(folder);
        var extension = ImageExporter.ExtensionFor(ParseFormat(config.Export.DefaultFormat));
        var name = FilenamePatterns.Generate(config.QuickSave.Pattern, folder, extension, now);
        return Path.Combine(folder, name);
    }

    /// <summary>渲染 + 落盘，返回完整路径。目录不存在会自动创建。</summary>
    public static string Save(NovashotConfig config, AnnotationDocument document, EditorState? state = null, DateTime? now = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(document);
        var format = ParseFormat(config.Export.DefaultFormat);
        var path = BuildFilePath(config, now);
        ImageExporter.Save(document, path, format, config.Export.JpegQuality, state);
        return path;
    }

    /// <summary>config 字符串 → 导出格式；未知值回退 PNG（与 <see cref="ImageExporter.FormatFromExtension"/> 语义一致）。</summary>
    public static ExportFormat ParseFormat(string name) => name?.ToLowerInvariant() switch
    {
        "jpeg" or "jpg" => ExportFormat.Jpeg,
        "webp" => ExportFormat.WebP,
        _ => ExportFormat.Png,
    };
}
