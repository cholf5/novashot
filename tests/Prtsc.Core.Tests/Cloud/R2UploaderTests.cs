using System.Net;
using Prtsc.Core.Cloud;
using Prtsc.Core.Config;

namespace Prtsc.Core.Tests;

/// <summary>
/// R2 上传（M7a）：path-style PUT https://{AccountId}.r2.cloudflarestorage.com/{Bucket}/{Key}，
/// SigV4 签名（host;x-amz-content-sha256;x-amz-date / region=auto / service=s3）。
/// Authorization 向量由 python hmac/sha256 独立预计算锁定（与 SigV4SignerTests 同方法论）。
/// </summary>
public class R2UploaderTests
{
    private const string AccessKey = "AKIDEXAMPLE";
    private const string SecretKey = "wJalrXUtnFEMI/K7MDENG+bPxRfiCYEXAMPLEKEY";
    private static readonly DateTimeOffset FixedNow = new(2026, 11, 1, 8, 9, 10, TimeSpan.Zero);

    private const string ExpectedAuthorization =
        "AWS4-HMAC-SHA256 Credential=AKIDEXAMPLE/20261101/auto/s3/aws4_request, " +
        "SignedHeaders=host;x-amz-content-sha256;x-amz-date, " +
        "Signature=e17676d340ecb8665292be73ba2845f9f5b89249dc73dedc10ab12a4d8b5bc58";

    private static R2Config SampleConfig() => new()
    {
        AccountId = "abc123def456",
        Bucket = "prtsc-shots",
        PublicUrl = "https://img.example.com",
        Directory = "shots/2026/11",
        AccessKeyId = AccessKey,
        SecretAccessKey = SecretKey,
    };

    private static UploadRequest SampleRequest() => new(
        fileName: "prtsc 01.png",
        content: "hello prtsc"u8.ToArray(),
        contentType: "image/png");

    // ---------- BuildUploadTarget ----------

    [Fact]
    public void BuildUploadTarget_MatchesIndependentlyComputedVector()
    {
        var uploader = new R2Uploader(SampleConfig());

        var target = uploader.BuildUploadTarget(SampleRequest(), FixedNow);

        Assert.Equal(
            "https://abc123def456.r2.cloudflarestorage.com/prtsc-shots/shots/2026/11/prtsc%2001.png",
            target.RequestUri);
        Assert.Equal("shots/2026/11/prtsc 01.png", target.ObjectKey);
        Assert.Equal(SigV4Signer.HashHex("hello prtsc"u8.ToArray()), target.PayloadHash);
        Assert.Equal("20261101T080910Z", target.AmzDate);
        Assert.Equal(ExpectedAuthorization, target.Authorization);
        Assert.Equal("https://img.example.com/shots/2026/11/prtsc%2001.png", target.PublicUrl);
    }

    [Fact]
    public void BuildUploadTarget_EmptyDirectory_PutsObjectAtBucketRoot()
    {
        var config = SampleConfig();
        config.Directory = " / "; // 分隔符噪音 → 归一化为空

        var target = new R2Uploader(config).BuildUploadTarget(SampleRequest(), FixedNow);

        Assert.Equal("prtsc 01.png", target.ObjectKey);
        Assert.Equal("https://abc123def456.r2.cloudflarestorage.com/prtsc-shots/prtsc%2001.png", target.RequestUri);
        Assert.Equal("https://img.example.com/prtsc%2001.png", target.PublicUrl);
    }

    // ---------- UploadAsync（注入 FakeHandler） ----------

    [Fact]
    public async Task UploadAsync_SendsSignedPut_AndReturnsPublicUrl()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var uploader = new R2Uploader(SampleConfig(), new HttpClient(handler), clock: () => FixedNow);

        var result = await uploader.UploadAsync(SampleRequest());

        Assert.True(result.Ok, result.Error);
        Assert.Equal("https://img.example.com/shots/2026/11/prtsc%2001.png", result.Url);

        var request = handler.Requests.Single();
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.Equal("/prtsc-shots/shots/2026/11/prtsc%2001.png", request.RequestUri!.AbsolutePath);
        Assert.Equal("abc123def456.r2.cloudflarestorage.com", request.RequestUri.Host);

        Assert.True(request.Headers.TryGetValues("x-amz-date", out var dates));
        Assert.Equal(["20261101T080910Z"], dates);
        Assert.True(request.Headers.TryGetValues("x-amz-content-sha256", out var hashes));
        Assert.Equal([SigV4Signer.HashHex("hello prtsc"u8.ToArray())], hashes);
        Assert.True(request.Headers.TryGetValues("Authorization", out var auths));
        Assert.Equal([ExpectedAuthorization], auths);

        Assert.Equal("image/png", request.Content!.Headers.ContentType!.ToString());
        Assert.Equal("hello prtsc"u8.ToArray(), handler.Bodies.Single());
    }

    [Fact]
    public async Task UploadAsync_ApiError_ReturnsFailWithStatusAndBody()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("<Error><Code>AccessDenied</Code></Error>"),
        });
        var uploader = new R2Uploader(SampleConfig(), new HttpClient(handler));

        var result = await uploader.UploadAsync(SampleRequest());

        Assert.False(result.Ok);
        Assert.Null(result.Url);
        Assert.Contains("403", result.Error);
        Assert.Contains("AccessDenied", result.Error);
    }

    [Fact]
    public async Task UploadAsync_NetworkError_ReturnsFailInsteadOfThrow()
    {
        var handler = new FakeHandler(_ => throw new HttpRequestException("connection refused"));
        var uploader = new R2Uploader(SampleConfig(), new HttpClient(handler));

        var result = await uploader.UploadAsync(SampleRequest());

        Assert.False(result.Ok);
        Assert.Contains("connection refused", result.Error);
    }

    [Fact]
    public async Task UploadAsync_MissingCredentials_ReturnsReadableFail_WithoutHttpCall()
    {
        var config = SampleConfig();
        config.SecretAccessKey = "";
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var uploader = new R2Uploader(config, new HttpClient(handler));

        var result = await uploader.UploadAsync(SampleRequest());

        Assert.False(result.Ok);
        Assert.Contains("SecretAccessKey", result.Error);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task UploadAsync_EmptyPublicUrl_StillSucceedsWithoutUrl()
    {
        var config = SampleConfig();
        config.PublicUrl = "";
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var uploader = new R2Uploader(config, new HttpClient(handler));

        var result = await uploader.UploadAsync(SampleRequest());

        Assert.True(result.Ok, result.Error);
        Assert.Null(result.Url);
    }
}
