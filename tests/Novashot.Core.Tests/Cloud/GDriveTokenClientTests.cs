using System.Net;
using System.Text;
using Novashot.Core.Cloud;
using Novashot.Core.Config;

namespace Novashot.Core.Tests;

/// <summary>
/// GDrive refresh_token → access_token（M7a）：POST oauth2.googleapis.com/token，
/// form-urlencoded 凭据；token 缓存到过期前 60 秒。
/// </summary>
public class GDriveTokenClientTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 11, 1, 8, 0, 0, TimeSpan.Zero);

    private static GDriveConfig SampleConfig() => new()
    {
        ClientId = "client-id",
        ClientSecret = "client-secret",
        RefreshToken = "refresh-token",
    };

    private static HttpResponseMessage TokenResponse(string accessToken = "at-123", int expiresIn = 3600) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(
            $$"""{"access_token":"{{accessToken}}","expires_in":{{expiresIn}},"token_type":"Bearer"}""",
            Encoding.UTF8, "application/json"),
    };

    [Fact]
    public async Task GetAccessTokenAsync_PostsFormCredentials_AndReturnsToken()
    {
        var now = FixedNow;
        var handler = new FakeHandler(_ => TokenResponse());
        var client = new GDriveTokenClient(SampleConfig(), new HttpClient(handler), clock: () => now);

        var token = await client.GetAccessTokenAsync();

        Assert.Equal("at-123", token.AccessToken);
        Assert.Equal(FixedNow.AddSeconds(3600), token.ExpiresAt);

        var request = handler.Requests.Single();
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://oauth2.googleapis.com/token", request.RequestUri!.ToString());
        Assert.Equal("application/x-www-form-urlencoded", request.Content!.Headers.ContentType!.ToString());

        var body = Encoding.UTF8.GetString(handler.Bodies.Single());
        Assert.Contains("client_id=client-id", body);
        Assert.Contains("client_secret=client-secret", body);
        Assert.Contains("refresh_token=refresh-token", body);
        Assert.Contains("grant_type=refresh_token", body);
    }

    [Fact]
    public async Task GetAccessTokenAsync_ApiError_ThrowsWithStatusAndBody()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("""{"error":"invalid_grant"}""", Encoding.UTF8, "application/json"),
        });
        var client = new GDriveTokenClient(SampleConfig(), new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAccessTokenAsync());

        Assert.Contains("400", ex.Message);
        Assert.Contains("invalid_grant", ex.Message);
    }

    [Fact]
    public async Task GetAccessTokenAsync_CachesUntilExpiry()
    {
        var now = FixedNow;
        var handler = new FakeHandler(_ => TokenResponse());
        var client = new GDriveTokenClient(SampleConfig(), new HttpClient(handler), clock: () => now);

        await client.GetAccessTokenAsync();
        now = FixedNow.AddMinutes(55); // 距过期还有 5 分钟 → 用缓存
        await client.GetAccessTokenAsync();
        Assert.Single(handler.Requests);

        now = FixedNow.AddMinutes(59.5); // 进入 60s 缓冲 → 重新取
        await client.GetAccessTokenAsync();
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task GetAccessTokenAsync_MissingExpiresIn_TokenNotCached()
    {
        var now = FixedNow;
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"access_token":"at-1","token_type":"Bearer"}""", Encoding.UTF8, "application/json"),
        });
        var client = new GDriveTokenClient(SampleConfig(), new HttpClient(handler), clock: () => now);

        await client.GetAccessTokenAsync();
        await client.GetAccessTokenAsync();

        Assert.Equal(2, handler.Requests.Count);
    }
}
