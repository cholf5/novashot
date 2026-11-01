using System.Net;
using Prtsc.Core.Config;

namespace Prtsc.Core.Cloud;

/// <summary>
/// 云凭据连接测试（G-25，对齐 winshot HeadBucket / token 换取）：
/// - R2：HeadBucket（200 或 403 = 可达；403 表示凭据有效但无 List 权限，桶所有者常态）；
/// - GDrive：refresh token 换 access token（<see cref="GDriveTokenClient"/> 同端点）。
/// 未配置直接返回失败，不发请求、不抛异常。
/// </summary>
public static class CloudConnectionTester
{
    /// <summary>R2 HeadBucket：HEAD https://{AccountId}.r2.cloudflarestorage.com/{Bucket}</summary>
    public static async Task<(bool ok, string message)> TestR2Async(
        R2Config config, HttpClient? http = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (string.IsNullOrWhiteSpace(config.AccountId) || string.IsNullOrWhiteSpace(config.Bucket) ||
            string.IsNullOrWhiteSpace(config.AccessKeyId) || string.IsNullOrWhiteSpace(config.SecretAccessKey))
            return (false, "R2 未配置完整（需要 Account ID / Bucket / Access Key ID / Secret Access Key）。");

        var url = $"https://{config.AccountId.Trim()}.r2.cloudflarestorage.com/{config.Bucket.Trim()}";
        var target = new R2Uploader(config, http).BuildUploadTarget(
            new UploadRequest("prtsc-connection-test.bin", [1], "application/octet-stream"));

        using var message = new HttpRequestMessage(HttpMethod.Head, url);
        message.Headers.Host = new Uri(url).Host;
        message.Headers.TryAddWithoutValidation("x-amz-date", target.AmzDate);
        message.Headers.TryAddWithoutValidation("x-amz-content-sha256", target.PayloadHash);
        message.Headers.TryAddWithoutValidation("Authorization", target.Authorization);

        try
        {
            using var client = http ?? CloudHttp.Client;
            using var response = await client.SendAsync(message, ct).ConfigureAwait(false);
            return response.StatusCode switch
            {
                HttpStatusCode.OK => (true, "R2 连接成功。"),
                HttpStatusCode.Forbidden => (true, "R2 可达（凭据有效，无 List 权限）。"),
                _ => (false, $"R2 连接失败：HTTP {(int)response.StatusCode} {response.ReasonPhrase}"),
            };
        }
        catch (Exception ex)
        {
            return (false, "R2 连接失败：" + ex.Message);
        }
    }

    /// <summary>GDrive：refresh token 换 access token（token 端点可达且凭据有效即算通）。</summary>
    public static async Task<(bool ok, string message)> TestGDriveAsync(
        GDriveConfig config, HttpClient? http = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (string.IsNullOrWhiteSpace(config.ClientId) || string.IsNullOrWhiteSpace(config.ClientSecret) ||
            string.IsNullOrWhiteSpace(config.RefreshToken))
            return (false, "Google Drive 未配置完整（需要 Client ID / Client Secret / Refresh Token）。");

        try
        {
            var token = await new GDriveTokenClient(config, http).GetAccessTokenAsync(ct).ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(token.AccessToken)
                ? (false, "Google Drive 连接失败：token 为空。")
                : (true, "Google Drive 连接成功（refresh token 有效）。");
        }
        catch (Exception ex)
        {
            return (false, "Google Drive 连接失败：" + ex.Message);
        }
    }
}
