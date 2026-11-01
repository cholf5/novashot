using System.Text.Json.Serialization;

namespace Prtsc.Core.Library;

/// <summary>Library 历史索引的一条记录（截图的元数据；缩略图在缩略图目录）。</summary>
public sealed class LibraryEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = string.Empty;

    /// <summary>本地导出文件完整路径（可能已被用户删除；打开时容错）。</summary>
    [JsonPropertyName("localPath")]
    public string LocalPath { get; set; } = string.Empty;

    /// <summary>上传后的公开链接（未上传为 null）。</summary>
    [JsonPropertyName("remoteUrl")]
    public string? RemoteUrl { get; set; }

    /// <summary>上传渠道（r2 / gdrive；未上传为 null）。</summary>
    [JsonPropertyName("provider")]
    public string? Provider { get; set; }

    [JsonPropertyName("width")]
    public int Width { get; set; }

    [JsonPropertyName("height")]
    public int Height { get; set; }

    [JsonPropertyName("sizeBytes")]
    public long SizeBytes { get; set; }
}
