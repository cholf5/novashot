using System.Net;
using System.Text;
using Novashot.Core.Config;
using Microsoft.Extensions.Logging;

namespace Novashot.Core.Cloud;

/// <summary>
/// R2 上传结果中间量：请求 URL / 对象键 / 签名相关值 / 公开链接。
/// 暴露出来供测试用独立预计算向量锁定签名正确性。
/// </summary>
public sealed record R2UploadTarget(
    string RequestUri,
    string ObjectKey,
    string PayloadHash,
    string AmzDate,
    string Authorization,
    string? PublicUrl);

/// <summary>
/// Cloudflare R2 上传（S3 兼容 path-style PUT）：
/// https://{AccountId}.r2.cloudflarestorage.com/{Bucket}/{Directory}/{FileName}
/// 签名用 <see cref="SigV4Signer"/>（signed headers: host;x-amz-content-sha256;x-amz-date，region=auto，service=s3）。
/// 成功且配置了 PublicUrl 时返回 {PublicUrl}/{Directory}/{FileName} 公开链接。
/// </summary>
public sealed class R2Uploader : IUploadService
{
    public const string Region = "auto";
    public const string Service = "s3";
    public const string SignedHeaders = "host;x-amz-content-sha256;x-amz-date";

    private readonly R2Config _config;
    private readonly HttpClient _http;
    private readonly Func<DateTimeOffset> _clock;
    private readonly ILogger<R2Uploader>? _logger;

    public R2Uploader(R2Config config, HttpClient? http = null, Func<DateTimeOffset>? clock = null,
        ILogger<R2Uploader>? logger = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _http = http ?? CloudHttp.Client;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _logger = logger;
    }

    /// <summary>构造签名后的上传目标。now 可注入以便向量测试。</summary>
    public R2UploadTarget BuildUploadTarget(UploadRequest request, DateTimeOffset? now = null)
    {
        var key = NormalizeKey(_config.Directory, request.FileName);
        var host = $"{_config.AccountId.Trim()}.r2.cloudflarestorage.com";
        var uri = "/" + _config.Bucket.Trim() + "/" + UriEncodeSegments(key);

        var timestamp = (now ?? _clock()).ToUniversalTime();
        var amzDate = timestamp.ToString("yyyyMMdd'T'HHmmss'Z'");
        var dateStamp = timestamp.ToString("yyyyMMdd");
        var payloadHash = SigV4Signer.HashHex(request.Content);

        var headers = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["host"] = host,
            ["x-amz-content-sha256"] = payloadHash,
            ["x-amz-date"] = amzDate,
        };
        var canonical = SigV4Signer.BuildCanonicalRequest(
            "PUT", uri, "", headers, SignedHeaders, payloadHash);
        var scope = $"{dateStamp}/{Region}/{Service}/aws4_request";
        var stringToSign = SigV4Signer.BuildStringToSign(amzDate, scope, canonical);
        var signingKey = SigV4Signer.BuildSigningKey(_config.SecretAccessKey, dateStamp, Region, Service);
        var signature = SigV4Signer.HmacHex(signingKey, Encoding.UTF8.GetBytes(stringToSign));
        var authorization = SigV4Signer.BuildAuthorizationHeader(
            _config.AccessKeyId, dateStamp, Region, Service, SignedHeaders, signature);

        var publicUrl = NormalizePublicUrl(_config.PublicUrl) is { } baseUrl
            ? $"{baseUrl}/{UriEncodeSegments(key)}"
            : null;

        return new R2UploadTarget($"https://{host}{uri}", key, payloadHash, amzDate, authorization, publicUrl);
    }

    public async Task<UploadResult> UploadAsync(UploadRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_config.AccountId))
            return UploadResult.Fail("R2 未配置：缺少 AccountId。");
        if (string.IsNullOrWhiteSpace(_config.Bucket))
            return UploadResult.Fail("R2 未配置：缺少 Bucket。");
        if (string.IsNullOrWhiteSpace(_config.AccessKeyId))
            return UploadResult.Fail("R2 未配置：缺少 AccessKeyId。");
        if (string.IsNullOrWhiteSpace(_config.SecretAccessKey))
            return UploadResult.Fail("R2 未配置：缺少 SecretAccessKey。");

        var target = BuildUploadTarget(request);
        try
        {
            using var content = new ByteArrayContent(request.Content);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(request.ContentType);

            using var message = new HttpRequestMessage(HttpMethod.Put, target.RequestUri) { Content = content };
            message.Headers.Host = new Uri(target.RequestUri).Host;
            message.Headers.TryAddWithoutValidation("x-amz-date", target.AmzDate);
            message.Headers.TryAddWithoutValidation("x-amz-content-sha256", target.PayloadHash);
            message.Headers.TryAddWithoutValidation("Authorization", target.Authorization);

            _logger?.LogDebug("R2 PUT {Uri} ({Bytes} bytes)", target.RequestUri, request.Content.Length);
            using var response = await _http.SendAsync(message, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                var snippet = body.Length > 300 ? body[..300] + "…" : body;
                return UploadResult.Fail($"R2 上传失败：HTTP {(int)response.StatusCode} {response.ReasonPhrase} {snippet}".TrimEnd());
            }
            return UploadResult.Success(target.PublicUrl);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            _logger?.LogWarning(ex, "R2 upload failed for {Uri}", target.RequestUri);
            return UploadResult.Fail($"R2 上传失败：{ex.Message}");
        }
    }

    /// <summary>目录 + 文件名 → 对象键：分隔符归一化 '/'，去首尾分隔与空白段。</summary>
    internal static string NormalizeKey(string? directory, string fileName)
    {
        var segments = new List<string>();
        foreach (var part in (directory ?? "").Split('/'))
        {
            var trimmed = part.Trim();
            if (trimmed.Length > 0) segments.Add(trimmed);
        }
        segments.Add(fileName.Trim());
        return string.Join('/', segments);
    }

    /// <summary>按 '/' 分段做 RFC 3986 编码（保留分隔符），得到签名/请求一致的 URI 路径。</summary>
    internal static string UriEncodeSegments(string key) =>
        string.Join('/', key.Split('/').Select(SigV4Signer.UriEncode));

    /// <summary>PublicUrl 去尾 '/'；空/纯空白 → null（不拼公开链接）。</summary>
    internal static string? NormalizePublicUrl(string? publicUrl)
    {
        var trimmed = (publicUrl ?? "").Trim().TrimEnd('/');
        return trimmed.Length > 0 ? trimmed : null;
    }
}

/// <summary>上传器共享的静态 HttpClient（进程内复用连接池，避免每上传一个 socket）。</summary>
internal static class CloudHttp
{
    public static HttpClient Client { get; } = new();
}
