using System.Net;
using System.Text;
using Novashot.Core.Cloud;
using Novashot.Core.Config;

namespace Novashot.Core.Tests;

/// <summary>
/// ConfigUploadService（M7a）：每次上传读 IConfigStore.Current.Cloud.Provider 分发
/// （none → Fail / r2 / gdrive，大小写不敏感，未知值可读报错）；config 改动即时生效。
/// </summary>
public class ConfigUploadServiceTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(
        Path.GetTempPath(), "novashot-configupload-tests", Guid.NewGuid().ToString("N"));

    private FakeHandler? _handler;
    private ConfigUploadService? _service;
    private JsonConfigStore? _store;

    public void Dispose()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
    }

    private ConfigUploadService CreateService(Action<NovashotConfig>? setup = null)
    {
        Directory.CreateDirectory(_tempDir);
        _store = new JsonConfigStore(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<JsonConfigStore>.Instance,
            overrideDirectory: _tempDir);
        _store.LoadAsync().GetAwaiter().GetResult();
        setup?.Invoke(_store.Current);
        _handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"id":"file-1"}""", Encoding.UTF8, "application/json"),
        });
        _service = new ConfigUploadService(_store, new HttpClient(_handler));
        return _service;
    }

    private static UploadRequest Sample() => new(
        fileName: "a.png", content: [1, 2, 3], contentType: "image/png");

    [Fact]
    public async Task UploadAsync_ProviderNone_ReturnsFailWithReadableMessage()
    {
        var service = CreateService();

        var result = await service.UploadAsync(Sample());

        Assert.False(result.Ok);
        Assert.Contains("未配置", result.Error);
    }

    [Fact]
    public async Task UploadAsync_ProviderR2_DispatchesToR2Uploader()
    {
        var service = CreateService(c =>
        {
            c.Cloud.Provider = "r2";
            c.Cloud.R2.AccountId = "acc";
            c.Cloud.R2.Bucket = "bkt";
            c.Cloud.R2.AccessKeyId = "AK";
            c.Cloud.R2.SecretAccessKey = "SK";
            c.Cloud.R2.PublicUrl = "https://img.example.com";
        });

        var result = await service.UploadAsync(Sample());

        Assert.True(result.Ok, result.Error);
        Assert.Equal("https://img.example.com/a.png", result.Url);
        var request = _handler!.Requests.Single();
        Assert.Equal("acc.r2.cloudflarestorage.com", request.RequestUri!.Host);
    }

    [Fact]
    public async Task UploadAsync_ProviderGDrive_DispatchesToGDriveUploader()
    {
        var service = CreateService(c =>
        {
            c.Cloud.Provider = "gdrive";
            c.Cloud.GDrive.ClientId = "cid";
            c.Cloud.GDrive.ClientSecret = "csecret";
            c.Cloud.GDrive.RefreshToken = "rtok";
        });
        var queue = new Queue<HttpResponseMessage>([
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"access_token":"at","expires_in":3600}""", Encoding.UTF8, "application/json"),
            },
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"id":"f1"}""", Encoding.UTF8, "application/json"),
            },
        ]);
        _handler = new FakeHandler(_ => queue.Dequeue());
        var fresh = new ConfigUploadService(_store!, new HttpClient(_handler));

        var result = await fresh.UploadAsync(Sample());

        Assert.True(result.Ok, result.Error);
        Assert.Equal(2, _handler.Requests.Count); // token + multipart 上传
        Assert.Contains("googleapis.com", _handler.Requests[1].RequestUri!.Host);
    }

    [Fact]
    public async Task UploadAsync_UnknownProvider_ReturnsReadableFail()
    {
        var service = CreateService(c => c.Cloud.Provider = "onedrive");

        var result = await service.UploadAsync(Sample());

        Assert.False(result.Ok);
        Assert.Contains("onedrive", result.Error);
    }

    [Fact]
    public async Task UploadAsync_ConfigChangeBetweenCalls_TakesEffectImmediately()
    {
        var service = CreateService(); // 默认 none

        var first = await service.UploadAsync(Sample());
        Assert.False(first.Ok);

        _store!.Update(c =>
        {
            c.Cloud.Provider = "r2";
            c.Cloud.R2.AccountId = "acc";
            c.Cloud.R2.Bucket = "bkt";
            c.Cloud.R2.AccessKeyId = "AK";
            c.Cloud.R2.SecretAccessKey = "SK";
        });
        var second = await service.UploadAsync(Sample());

        Assert.True(second.Ok, second.Error);
        Assert.Contains("r2.cloudflarestorage.com", _handler!.Requests.Single().RequestUri!.Host);
    }
}
