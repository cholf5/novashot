using Prtsc.Core.Cloud;

namespace Prtsc.Core.Tests;

/// <summary>
/// 上传抽象与 NullUploadService（M7a）：未配置渠道时 Upload 返回 Fail 且消息可读、不抛异常。
/// </summary>
public class NullUploadServiceTests
{
    [Fact]
    public async Task UploadAsync_ReturnsFailWithReadableMessage()
    {
        var service = new NullUploadService();

        var result = await service.UploadAsync(new UploadRequest(
            fileName: "prtsc.png",
            content: [1, 2, 3],
            contentType: "image/png"));

        Assert.False(result.Ok);
        Assert.Null(result.Url);
        Assert.Contains("未配置", result.Error);
    }

    [Fact]
    public async Task UploadAsync_NullOrEmptyFileName_Throws()
    {
        var service = new NullUploadService();

        await Assert.ThrowsAsync<ArgumentException>(() => service.UploadAsync(new UploadRequest(
            fileName: "  ",
            content: [1],
            contentType: "image/png")));
    }

    [Fact]
    public async Task UploadAsync_EmptyContent_Throws()
    {
        var service = new NullUploadService();

        await Assert.ThrowsAsync<ArgumentException>(() => service.UploadAsync(new UploadRequest(
            fileName: "a.png",
            content: [],
            contentType: "image/png")));
    }
}
