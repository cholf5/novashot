using System.Net;
using System.Text;
using Novashot.Core.Cloud;
using Novashot.Core.Config;

namespace Novashot.Core.Tests;

/// <summary>
/// GDrive multipart 上传（M7a）：POST upload endpoint，multipart/related
/// （part1 = metadata JSON {name,parents}，part2 = 字节）；可选 MakePublic（POST permissions）拿 webViewLink。
/// multipart 字节由 FakeHandler 记录，逐段解析断言。
/// </summary>
public class GDriveUploaderTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 11, 1, 8, 9, 10, TimeSpan.Zero);

    private static GDriveConfig SampleConfig(bool withFolder = true) => new()
    {
        FolderId = withFolder ? "folder-123" : "",
        ClientId = "client-id",
        ClientSecret = "client-secret",
        RefreshToken = "refresh-token",
    };

    private static UploadRequest SampleRequest() => new(
        fileName: "novashot 01.png",
        content: "hello novashot"u8.ToArray(),
        contentType: "image/png");

    /// <summary>第一跳发 token，第二跳发 multipart，第三跳（可选）发 permissions。</summary>
    private static FakeHandler ChainHandler(params HttpResponseMessage[] responses)
    {
        var queue = new Queue<HttpResponseMessage>(responses);
        return new FakeHandler(_ => queue.Dequeue());
    }

    private static HttpResponseMessage TokenResponse() => new(HttpStatusCode.OK)
    {
        Content = new StringContent(
            """{"access_token":"at-123","expires_in":3600,"token_type":"Bearer"}""",
            Encoding.UTF8, "application/json"),
    };

    private static HttpResponseMessage UploadResponse(string fileId = "file-abc") => new(HttpStatusCode.OK)
    {
        Content = new StringContent(
            $$"""{"id":"{{fileId}}","name":"novashot 01.png","mimeType":"image/png"}""",
            Encoding.UTF8, "application/json"),
    };

    private static HttpResponseMessage PermissionResponse() => new(HttpStatusCode.OK)
    {
        Content = new StringContent("""{"id":"perm-1","type":"anyone","role":"reader"}""", Encoding.UTF8, "application/json"),
    };

    // ---------- BuildMetadata ----------

    [Fact]
    public void BuildMetadata_WithFolder_HasNameAndParents()
    {
        var json = GDriveUploader.BuildMetadata(SampleConfig(), "novashot 01.png");

        Assert.Equal("""{"name":"novashot 01.png","parents":["folder-123"]}""", json);
    }

    [Fact]
    public void BuildMetadata_WithoutFolder_NameOnly()
    {
        var json = GDriveUploader.BuildMetadata(SampleConfig(withFolder: false), "a.png");

        Assert.Equal("""{"name":"a.png"}""", json);
    }

    // ---------- UploadAsync ----------

    [Fact]
    public async Task UploadAsync_SendsMultipartRelated_WithBearer()
    {
        var handler = ChainHandler(TokenResponse(), UploadResponse());
        var uploader = new GDriveUploader(SampleConfig(), new HttpClient(handler), clock: () => FixedNow);

        var result = await uploader.UploadAsync(SampleRequest());

        Assert.True(result.Ok, result.Error);
        Assert.Null(result.Url); // 未开 MakePublic → 无公开链接

        var uploadRequest = handler.Requests[1];
        Assert.Equal(HttpMethod.Post, uploadRequest.Method);
        Assert.Equal(
            "https://www.googleapis.com/upload/drive/v3/files?uploadType=multipart",
            uploadRequest.RequestUri!.ToString());
        Assert.True(uploadRequest.Headers.TryGetValues("Authorization", out var auths));
        Assert.Equal(["Bearer at-123"], auths);

        Assert.Equal("multipart/related", uploadRequest.Content!.Headers.ContentType!.MediaType);
        var boundary = uploadRequest.Content.Headers.ContentType.Parameters
            .Single(p => p.Name == "boundary").Value!.Trim('"');

        var body = Encoding.UTF8.GetString(handler.Bodies[1]);
        var parts = body.Split($"--{boundary}");
        Assert.Equal(4, parts.Length); // 空前缀 + 两个 part + 尾随 "--"
        Assert.Contains("Content-Type: application/json", parts[1]);
        Assert.Contains("""{"name":"novashot 01.png","parents":["folder-123"]}""", parts[1]);
        Assert.Contains("Content-Type: image/png", parts[2]);
        Assert.Contains("hello novashot", parts[2]);
        Assert.StartsWith("--", parts[3].Trim());
    }

    [Fact]
    public async Task UploadAsync_WithoutFolder_NoParentsKey()
    {
        var handler = ChainHandler(TokenResponse(), UploadResponse());
        var uploader = new GDriveUploader(SampleConfig(withFolder: false), new HttpClient(handler));

        var result = await uploader.UploadAsync(SampleRequest());

        Assert.True(result.Ok, result.Error);
        var body = Encoding.UTF8.GetString(handler.Bodies[1]);
        Assert.DoesNotContain("\"parents\"", body);
    }

    [Fact]
    public async Task UploadAsync_MakePublic_PermissionCallThenReturnsWebViewLink()
    {
        var handler = ChainHandler(TokenResponse(), UploadResponse(), PermissionResponse());
        var config = SampleConfig();
        config.FolderId = "folder-123";
        var uploader = new GDriveUploader(config, new HttpClient(handler), makePublic: true);

        var result = await uploader.UploadAsync(SampleRequest());

        Assert.True(result.Ok, result.Error);
        Assert.Equal("https://drive.google.com/file/d/file-abc/view", result.Url);

        var permissionRequest = handler.Requests[2];
        Assert.Equal(HttpMethod.Post, permissionRequest.Method);
        Assert.Equal("https://www.googleapis.com/drive/v3/files/file-abc/permissions", permissionRequest.RequestUri!.ToString());
        Assert.Contains("anyone", Encoding.UTF8.GetString(handler.Bodies[2]));
    }

    [Fact]
    public async Task UploadAsync_MakePublic_PermissionFails_StillSucceedsWithoutUrl()
    {
        var handler = ChainHandler(TokenResponse(), UploadResponse(), new HttpResponseMessage(HttpStatusCode.Forbidden));
        var uploader = new GDriveUploader(SampleConfig(), new HttpClient(handler), makePublic: true);

        var result = await uploader.UploadAsync(SampleRequest());

        Assert.True(result.Ok, result.Error);
        Assert.Null(result.Url);
    }

    [Fact]
    public async Task UploadAsync_TokenError_ReturnsFail()
    {
        var handler = ChainHandler(new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("""{"error":"invalid_grant"}""", Encoding.UTF8, "application/json"),
        });
        var uploader = new GDriveUploader(SampleConfig(), new HttpClient(handler));

        var result = await uploader.UploadAsync(SampleRequest());

        Assert.False(result.Ok);
        Assert.Contains("invalid_grant", result.Error);
        Assert.Single(handler.Requests); // 不再发上传请求
    }

    [Fact]
    public async Task UploadAsync_UploadError_ReturnsFailWithStatusAndBody()
    {
        var handler = ChainHandler(TokenResponse(), new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("backend error"),
        });
        var uploader = new GDriveUploader(SampleConfig(), new HttpClient(handler));

        var result = await uploader.UploadAsync(SampleRequest());

        Assert.False(result.Ok);
        Assert.Contains("500", result.Error);
        Assert.Contains("backend error", result.Error);
    }

    [Fact]
    public async Task UploadAsync_MissingCredentials_ReturnsReadableFail_WithoutHttpCall()
    {
        var config = SampleConfig();
        config.RefreshToken = "";
        var handler = ChainHandler(TokenResponse(), UploadResponse());
        var uploader = new GDriveUploader(config, new HttpClient(handler));

        var result = await uploader.UploadAsync(SampleRequest());

        Assert.False(result.Ok);
        Assert.Contains("RefreshToken", result.Error);
        Assert.Empty(handler.Requests);
    }
}
