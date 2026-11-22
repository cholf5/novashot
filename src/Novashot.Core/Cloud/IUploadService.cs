namespace Novashot.Core.Cloud;

/// <summary>一次上传请求：目标文件名、字节与 MIME。构造时即校验参数。</summary>
public sealed record UploadRequest
{
    public UploadRequest(string fileName, byte[] content, string contentType)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("File name is empty.", nameof(fileName));
        if (content is not { Length: > 0 })
            throw new ArgumentException("Content is empty.", nameof(content));
        if (string.IsNullOrWhiteSpace(contentType))
            throw new ArgumentException("Content type is empty.", nameof(contentType));

        FileName = fileName;
        Content = content;
        ContentType = contentType;
    }

    public string FileName { get; }
    public byte[] Content { get; }
    public string ContentType { get; }
}

/// <summary>上传结果：成功带公开链接；失败带可读消息（不抛异常返回）。</summary>
public sealed record UploadResult(bool Ok, string? Url, string? Error)
{
    public static UploadResult Success(string? url) => new(true, url, null);
    public static UploadResult Fail(string error) => new(false, null, error);
}

/// <summary>云上传服务。实现必须是纯 HTTP 数据层（无 Win32 / 无 Avalonia），失败返回 Fail 而不是抛异常。</summary>
public interface IUploadService
{
    Task<UploadResult> UploadAsync(UploadRequest request, CancellationToken ct = default);
}

/// <summary>未配置云渠道时的兜底实现：始终 Fail，带指引到设置的中文消息。</summary>
public sealed class NullUploadService : IUploadService
{
    public Task<UploadResult> UploadAsync(UploadRequest request, CancellationToken ct = default) =>
        Task.FromResult(UploadResult.Fail("云上传未配置：请在设置中选择 R2 或 Google Drive 并填写凭据。"));
}
