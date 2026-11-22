using System.Net;
using System.Text;
using Novashot.Core.Annotation;
using Novashot.Core.Cloud;
using Novashot.Core.Config;
using Novashot.Core.Editor;
using Novashot.Core.Editor.Export;
using Novashot.Core.Library;
using SkiaSharp;

namespace Novashot.Core.Tests;

/// <summary>
/// UploadCoordinator（M7b 编排层）：渲染编码 → 上传 → 记 Library。
/// 上传成功后 Library 里有 RemoteUrl/Provider/宽高/字节量齐全的记录；
/// 上传失败不落库；Library 写失败不吞成功结果。
/// </summary>
public class UploadCoordinatorTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(
        Path.GetTempPath(), "novashot-coordinator-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
    }

    private static AnnotationDocument PlainDoc(int w = 8, int h = 6)
    {
        var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Opaque));
        bmp.Erase(SKColors.DimGray);
        return new AnnotationDocument(bmp);
    }

    private (UploadCoordinator Coordinator, JsonConfigStore Store, LibraryStore Library, FakeHandler Handler)
        Create(Action<NovashotConfig>? setup = null, HttpResponseMessage? uploadResponse = null)
    {
        Directory.CreateDirectory(_tempDir);
        var store = new JsonConfigStore(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<JsonConfigStore>.Instance,
            overrideDirectory: _tempDir);
        store.LoadAsync().GetAwaiter().GetResult();
        store.Update(c =>
        {
            c.Cloud.Provider = "r2";
            c.Cloud.R2.AccountId = "acc";
            c.Cloud.R2.Bucket = "bkt";
            c.Cloud.R2.AccessKeyId = "AK";
            c.Cloud.R2.SecretAccessKey = "SK";
            c.Cloud.R2.PublicUrl = "https://img.example.com";
            setup?.Invoke(c);
        });
        var handler = new FakeHandler(_ => uploadResponse ?? new HttpResponseMessage(HttpStatusCode.OK));
        var library = new LibraryStore(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<LibraryStore>.Instance, _tempDir);
        var coordinator = new UploadCoordinator(
            new ConfigUploadService(store, new HttpClient(handler)), store, library);
        return (coordinator, store, library, handler);
    }

    [Fact]
    public async Task UploadDocument_Success_RecordsLibraryEntryWithUrlAndProvider()
    {
        var (coordinator, _, library, handler) = Create();
        var doc = PlainDoc();
        var state = new EditorState(Padding: 2); // 合成 (8+4)x(6+4)

        var outcome = await coordinator.UploadDocumentAsync(doc, state);

        Assert.True(outcome.Ok, outcome.Error);
        Assert.Equal("https://img.example.com/" + outcome.Entry!.FileName, outcome.Url);

        var entries = await library.LoadAsync();
        var entry = Assert.Single(entries);
        Assert.Equal(outcome.Entry.Id, entry.Id);
        Assert.Equal(entry.FileName, outcome.Entry.FileName);
        Assert.StartsWith("novashot_", entry.FileName);
        Assert.EndsWith(".png", entry.FileName, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(entry.RemoteUrl, outcome.Url);
        Assert.Equal("r2", entry.Provider);
        Assert.Equal(12, entry.Width);
        Assert.Equal(10, entry.Height);
        Assert.True(entry.SizeBytes > 0);

        Assert.Equal("image/png", handler.Requests.Single().Content!.Headers.ContentType!.ToString());
    }

    [Fact]
    public async Task UploadDocument_Failure_NoLibraryRecord()
    {
        var (coordinator, _, library, _) = Create(uploadResponse: new HttpResponseMessage(HttpStatusCode.Forbidden));

        var outcome = await coordinator.UploadDocumentAsync(PlainDoc(), EditorState.Default);

        Assert.False(outcome.Ok);
        Assert.Null(outcome.Entry);
        Assert.Empty(await library.LoadAsync());
        Assert.Contains("403", outcome.Error);
    }

    [Fact]
    public async Task UploadDocument_UploadThrows_ExceptionBecomesFailOutcome()
    {
        var handler = new FakeHandler(_ => throw new HttpRequestException("boom"));
        Directory.CreateDirectory(_tempDir);
        var store = new JsonConfigStore(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<JsonConfigStore>.Instance,
            overrideDirectory: _tempDir);
        await store.LoadAsync();
        store.Update(c =>
        {
            c.Cloud.Provider = "r2";
            c.Cloud.R2.AccountId = "acc";
            c.Cloud.R2.Bucket = "bkt";
            c.Cloud.R2.AccessKeyId = "AK";
            c.Cloud.R2.SecretAccessKey = "SK";
        });
        var library = new LibraryStore(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<LibraryStore>.Instance, _tempDir);
        var coordinator = new UploadCoordinator(
            new ConfigUploadService(store, new HttpClient(handler)), store, library);

        var outcome = await coordinator.UploadDocumentAsync(PlainDoc(), EditorState.Default);

        Assert.False(outcome.Ok);
        Assert.Contains("boom", outcome.Error);
    }

    [Fact]
    public async Task UploadDocument_LibraryWriteFails_UploadStillSucceeds()
    {
        // 用只读化的 library.json 触发 Library 写失败（把文件路径顶成目录）
        Directory.CreateDirectory(_tempDir);
        var store = new JsonConfigStore(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<JsonConfigStore>.Instance,
            overrideDirectory: _tempDir);
        await store.LoadAsync();
        store.Update(c =>
        {
            c.Cloud.Provider = "r2";
            c.Cloud.R2.AccountId = "acc";
            c.Cloud.R2.Bucket = "bkt";
            c.Cloud.R2.AccessKeyId = "AK";
            c.Cloud.R2.SecretAccessKey = "SK";
        });
        Directory.CreateDirectory(Path.Combine(_tempDir, "library.json")); // 目录占位 → 保存必炸
        var library = new LibraryStore(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<LibraryStore>.Instance, _tempDir);
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var coordinator = new UploadCoordinator(
            new ConfigUploadService(store, new HttpClient(handler)), store, library);

        var outcome = await coordinator.UploadDocumentAsync(PlainDoc(), EditorState.Default);

        Assert.True(outcome.Ok, outcome.Error);
        Assert.Null(outcome.Entry);
    }

    [Fact]
    public async Task UploadDocument_WithoutEditorState_UsesDocumentSize()
    {
        var (coordinator, _, library, _) = Create();

        var outcome = await coordinator.UploadDocumentAsync(PlainDoc(20, 10), state: null);

        Assert.True(outcome.Ok, outcome.Error);
        Assert.Equal(20, (await library.LoadAsync()).Single().Width);
    }

    [Fact]
    public async Task UploadDocument_JpegFormat_SetsContentTypeAndExtension()
    {
        var (coordinator, _, _, handler) = Create(c => c.Export.DefaultFormat = "jpeg");

        var outcome = await coordinator.UploadDocumentAsync(PlainDoc(), EditorState.Default);

        Assert.True(outcome.Ok, outcome.Error);
        Assert.Equal("image/jpeg", handler.Requests.Single().Content!.Headers.ContentType!.ToString());
        Assert.EndsWith(".jpg", outcome.Entry!.FileName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UploadDocument_Success_WritesThumbnail()
    {
        var thumbsDir = Path.Combine(_tempDir, "thumbs");
        Directory.CreateDirectory(_tempDir);
        var store = new JsonConfigStore(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<JsonConfigStore>.Instance,
            overrideDirectory: _tempDir);
        await store.LoadAsync();
        store.Update(c =>
        {
            c.Cloud.Provider = "r2";
            c.Cloud.R2.AccountId = "acc";
            c.Cloud.R2.Bucket = "bkt";
            c.Cloud.R2.AccessKeyId = "AK";
            c.Cloud.R2.SecretAccessKey = "SK";
        });
        var library = new LibraryStore(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<LibraryStore>.Instance, _tempDir);
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var coordinator = new UploadCoordinator(
            new ConfigUploadService(store, new HttpClient(handler)), store, library,
            thumbsDirectory: thumbsDir);

        var outcome = await coordinator.UploadDocumentAsync(PlainDoc(), EditorState.Default);

        Assert.True(outcome.Ok, outcome.Error);
        Assert.NotNull(outcome.Entry);
        var thumbPath = Path.Combine(thumbsDir, $"thumb_{outcome.Entry.Id}.png");
        Assert.True(File.Exists(thumbPath), "缩略图应随上传入库生成");
        using var decoded = SKBitmap.Decode(thumbPath);
        Assert.NotNull(decoded);
        Assert.True(Math.Max(decoded!.Width, decoded.Height) <= 320);
    }

    [Theory]
    [InlineData("none", null)]
    [InlineData("R2", "r2")]
    [InlineData("gdrive", "gdrive")]
    [InlineData("  ", null)]
    public void NormalizeProvider_MapsAndCleans(string input, string? expected)
    {
        Assert.Equal(expected, UploadCoordinator.NormalizeProvider(input));
    }

    [Theory]
    [InlineData(ExportFormat.Png, "image/png")]
    [InlineData(ExportFormat.Jpeg, "image/jpeg")]
    [InlineData(ExportFormat.WebP, "image/webp")]
    public void ContentTypeFor_MapsFormats(ExportFormat format, string expected)
    {
        Assert.Equal(expected, UploadCoordinator.ContentTypeFor(format));
    }
}
