using System.Text;
using System.Text.Json;
using Novashot.Core.Config;
using Microsoft.Extensions.Logging;

namespace Novashot.Core.Cloud;

/// <summary>
/// Google Drive v3 multipart 上传：POST https://www.googleapis.com/upload/drive/v3/files?uploadType=multipart，
/// multipart/related（part1 = metadata JSON {name,parents}，part2 = 图片字节，Bearer access token）。
/// MakePublic = true 时再 POST permissions {anyone, reader}，拼 webViewLink 公开链接。
/// </summary>
public sealed class GDriveUploader : IUploadService
{
    public const string UploadEndpoint = "https://www.googleapis.com/upload/drive/v3/files?uploadType=multipart";

    private readonly GDriveConfig _config;
    private readonly HttpClient _http;
    private readonly Func<DateTimeOffset> _clock;
    private readonly bool _makePublic;
    private readonly ILogger<GDriveUploader>? _logger;
    private readonly GDriveTokenClient _tokenClient;

    public GDriveUploader(GDriveConfig config, HttpClient? http = null,
        Func<DateTimeOffset>? clock = null, bool makePublic = false, ILogger<GDriveUploader>? logger = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _http = http ?? CloudHttp.Client;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _makePublic = makePublic;
        _logger = logger;
        _tokenClient = new GDriveTokenClient(config, _http, clock, logger);
    }

    /// <summary>上传元数据 JSON：{name, parents?}（FolderId 空 = 我的云端硬盘根目录，省略 parents）。</summary>
    public static string BuildMetadata(GDriveConfig config, string fileName)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("name", fileName);
            if (!string.IsNullOrWhiteSpace(config.FolderId))
            {
                writer.WritePropertyName("parents");
                writer.WriteStartArray();
                writer.WriteStringValue(config.FolderId.Trim());
                writer.WriteEndArray();
            }
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public async Task<UploadResult> UploadAsync(UploadRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_config.ClientId))
            return UploadResult.Fail("Google Drive 未配置：缺少 ClientId。");
        if (string.IsNullOrWhiteSpace(_config.ClientSecret))
            return UploadResult.Fail("Google Drive 未配置：缺少 ClientSecret。");
        if (string.IsNullOrWhiteSpace(_config.RefreshToken))
            return UploadResult.Fail("Google Drive 未配置：缺少 RefreshToken。");

        string fileId;
        try
        {
            var token = await _tokenClient.GetAccessTokenAsync(ct).ConfigureAwait(false);
            fileId = await UploadMultipartAsync(request, token.AccessToken, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException
            or JsonException or TaskCanceledException)
        {
            _logger?.LogWarning(ex, "GDrive upload failed");
            return UploadResult.Fail($"Google Drive 上传失败：{ex.Message}");
        }

        if (!_makePublic)
            return UploadResult.Success(null);

        try
        {
            var token = await _tokenClient.GetAccessTokenAsync(ct).ConfigureAwait(false);
            var url = await MakePublicAsync(fileId, token.AccessToken, ct).ConfigureAwait(false);
            return UploadResult.Success(url);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // 上传已成功，公开化失败不算失败：返回成功但无链接
            _logger?.LogWarning(ex, "GDrive permission failed for {FileId}; upload succeeded", fileId);
            return UploadResult.Success(null);
        }
    }

    private async Task<string> UploadMultipartAsync(UploadRequest request, string accessToken, CancellationToken ct)
    {
        var boundary = "novashot_boundary_" + Guid.NewGuid().ToString("N");
        var metadata = BuildMetadata(_config, request.FileName);

        using var content = new MultipartContent("related", boundary);
        var metadataPart = new StringContent(metadata, Encoding.UTF8, "application/json");
        var mediaPart = new ByteArrayContent(request.Content);
        mediaPart.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(request.ContentType);
        content.Add(metadataPart);
        content.Add(mediaPart);

        using var message = new HttpRequestMessage(HttpMethod.Post, UploadEndpoint) { Content = content };
        message.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        _logger?.LogDebug("GDrive multipart POST {FileName} ({Bytes} bytes)", request.FileName, request.Content.Length);
        using var response = await _http.SendAsync(message, ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Google Drive 上传接口返回 HTTP {(int)response.StatusCode} {response.StatusCode}: {body}");

        using var doc = JsonDocument.Parse(body);
        var fileId = doc.RootElement.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
        if (string.IsNullOrEmpty(fileId))
            throw new InvalidOperationException("Google Drive 上传响应缺少 id 字段。");
        return fileId;
    }

    private async Task<string> MakePublicAsync(string fileId, string accessToken, CancellationToken ct)
    {
        var endpoint = $"https://www.googleapis.com/drive/v3/files/{fileId}/permissions";
        using var content = new StringContent("""{"role":"reader","type":"anyone"}""", Encoding.UTF8, "application/json");
        using var message = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = content };
        message.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await _http.SendAsync(message, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new HttpRequestException($"Google Drive 权限接口返回 HTTP {(int)response.StatusCode} {response.StatusCode}: {body}");
        }
        return $"https://drive.google.com/file/d/{fileId}/view";
    }
}
