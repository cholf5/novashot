using System.Net;
using System.Net.Http;
using System.Text;
using Novashot.Core.Cloud;
using Novashot.Core.Config;

namespace Novashot.Core.Tests;

/// <summary>
/// G-25（winshot 连接测试）：R2 = HeadBucket（200/403 都算通，403=无 List 权限但凭据有效）；
/// GDrive = access token 换取（refresh token 有效即通）。未配置时返回失败不抛异常。
/// </summary>
public class CloudConnectionTesterTests
{
    private static R2Config R2(string accountId = "", string bucket = "", string key = "", string secret = "") =>
        new() { AccountId = accountId, Bucket = bucket, AccessKeyId = key, SecretAccessKey = secret };

    private static GDriveConfig GDrive(string clientId = "", string secret = "", string refresh = "") =>
        new() { ClientId = clientId, ClientSecret = secret, RefreshToken = refresh };

    [Fact]
    public async Task R2_NotConfigured_FailsWithoutThrow()
    {
        using var http = new HttpClient(new FakeHttpHandler(_ => throw new AssertionError("should not send")));
        var result = await CloudConnectionTester.TestR2Async(R2(), http);
        Assert.False(result.ok);
        Assert.Contains("未配置", result.message);
    }

    [Fact]
    public async Task GDrive_NotConfigured_FailsWithoutThrow()
    {
        using var http = new HttpClient(new FakeHttpHandler(_ => throw new AssertionError("should not send")));
        var result = await CloudConnectionTester.TestGDriveAsync(GDrive(), http);
        Assert.False(result.ok);
        Assert.Contains("未配置", result.message);
    }

    [Fact]
    public async Task R2_HeadBucket200_Succeeds()
    {
        using var http = new HttpClient(new FakeHttpHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)));
        var result = await CloudConnectionTester.TestR2Async(
            R2("acct", "bucket", "key", "secret"), http);
        Assert.True(result.ok);
    }

    [Fact]
    public async Task R2_HeadBucket403_CountsAsReachable()
    {
        // HeadBucket 403 = 凭据有效但无 list 权限（桶所有者常见），winshot 同语义算通
        using var http = new HttpClient(new FakeHttpHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.Forbidden)));
        var result = await CloudConnectionTester.TestR2Async(
            R2("acct", "bucket", "key", "secret"), http);
        Assert.True(result.ok);
    }

    [Fact]
    public async Task R2_HeadBucket401_Fails()
    {
        using var http = new HttpClient(new FakeHttpHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.Unauthorized)));
        var result = await CloudConnectionTester.TestR2Async(
            R2("acct", "bucket", "key", "secret"), http);
        Assert.False(result.ok);
        Assert.Contains("401", result.message);
    }

    [Fact]
    public async Task GDrive_TokenEndpointReturnsToken_Succeeds()
    {
        using var http = new HttpClient(new FakeHttpHandler(req =>
        {
            var json = """{"access_token":"ya29.x","expires_in":3599,"token_type":"Bearer"}""";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
        }));
        var result = await CloudConnectionTester.TestGDriveAsync(
            GDrive("cid", "csec", "rtok"), http);
        Assert.True(result.ok);
    }

    [Fact]
    public async Task GDrive_TokenEndpoint400_FailsWithBody()
    {
        using var http = new HttpClient(new FakeHttpHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("invalid_grant", Encoding.UTF8, "text/plain"),
            }));
        var result = await CloudConnectionTester.TestGDriveAsync(
            GDrive("cid", "csec", "bad"), http);
        Assert.False(result.ok);
        Assert.Contains("invalid_grant", result.message);
    }

    private sealed class AssertionError(string msg) : Exception(msg);

    private sealed class FakeHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(responder(request));
    }
}
