using System.Text.Json;
using Novashot.Core.Config;
using Microsoft.Extensions.Logging;

namespace Novashot.Core.Cloud;

/// <summary>一次成功换取的 access token 及其过期时刻。</summary>
public sealed record GDriveAccessToken(string AccessToken, DateTimeOffset ExpiresAt);

/// <summary>
/// Google OAuth2：refresh_token → access_token（POST https://oauth2.googleapis.com/token）。
/// token 缓存到过期前 60 秒；凭据缺失或 HTTP/解析失败抛 HttpRequestException/InvalidOperationException，
/// 由上传器转成 UploadResult.Fail。
/// </summary>
public sealed class GDriveTokenClient
{
    private static readonly TimeSpan ExpiryBuffer = TimeSpan.FromSeconds(60);

    private readonly GDriveConfig _config;
    private readonly HttpClient _http;
    private readonly Func<DateTimeOffset> _clock;
    private readonly ILogger? _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private GDriveAccessToken? _cached;

    public GDriveTokenClient(GDriveConfig config, HttpClient? http = null,
        Func<DateTimeOffset>? clock = null, ILogger? logger = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _http = http ?? CloudHttp.Client;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _logger = logger;
    }

    public async Task<GDriveAccessToken> GetAccessTokenAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var now = _clock();
            if (_cached is { } cached && now < cached.ExpiresAt - ExpiryBuffer)
                return cached;

            var form = new Dictionary<string, string>
            {
                ["client_id"] = _config.ClientId,
                ["client_secret"] = _config.ClientSecret,
                ["refresh_token"] = _config.RefreshToken,
                ["grant_type"] = "refresh_token",
            };
            using var content = new FormUrlEncodedContent(form);
            using var response = await _http.PostAsync(
                "https://oauth2.googleapis.com/token", content, ct).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"Google Drive token 接口返回 HTTP {(int)response.StatusCode} {response.StatusCode}: {body}");

            using var doc = JsonDocument.Parse(body);
            var accessToken = doc.RootElement.TryGetProperty("access_token", out var tokenEl)
                ? tokenEl.GetString()
                : null;
            if (string.IsNullOrEmpty(accessToken))
                throw new InvalidOperationException("Google Drive token 响应缺少 access_token 字段。");

            // expires_in 缺失或不可解析 → 不缓存（ExpiresAt = now），每次现取
            var expiresIn = doc.RootElement.TryGetProperty("expires_in", out var expEl)
                && expEl.TryGetInt32(out var seconds) ? seconds : 0;
            var token = new GDriveAccessToken(accessToken, now.AddSeconds(expiresIn));
            _cached = token;
            _logger?.LogDebug("GDrive access token refreshed, expires in {Seconds}s", expiresIn);
            return token;
        }
        finally
        {
            _lock.Release();
        }
    }
}
