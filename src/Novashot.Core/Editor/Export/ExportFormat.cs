namespace Novashot.Core.Editor.Export;

/// <summary>导出编码格式。JPEG 有损无 alpha；PNG 无损；WebP 有损（quality 可调）。</summary>
public enum ExportFormat
{
    Png,
    Jpeg,
    WebP,
}
