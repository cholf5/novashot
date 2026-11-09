using Prtsc.Core.Utils;

namespace Prtsc.Core.Tests;

/// <summary>
/// I-1 Ctrl+O 导入图片：扩展名过滤判定（winshot 对齐面）。
/// 只锁三件事：Extensions 集合内容（HDROP G-11 与文件选择器共用的真值）、
/// Patterns 与 Extensions 一一对应（文件选择器过滤面）、
/// IsSupported 大小写不敏感 + 未知/空拒绝（解码动不动仍由 Skia 说了算，判定只圈「常用图片」）。
/// </summary>
public class ImageFileTypesTests
{
    [Fact]
    public void Extensions_MatchesWinshotImageSet()
    {
        Assert.Equal(
            new[] { ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif" },
            ImageFileTypes.Extensions);
    }

    [Fact]
    public void Patterns_AreStarPrefixedExtensions_OneToOne()
    {
        Assert.Equal(ImageFileTypes.Extensions.Count, ImageFileTypes.Patterns.Count);
        for (var i = 0; i < ImageFileTypes.Extensions.Count; i++)
            Assert.Equal("*" + ImageFileTypes.Extensions[i], ImageFileTypes.Patterns[i]);
    }

    [Theory]
    [InlineData(".png", true)]
    [InlineData(".PNG", true)]
    [InlineData(".Jpg", true)]
    [InlineData(".JPEG", true)]
    [InlineData(".webp", true)]
    [InlineData(".bmp", true)]
    [InlineData(".gif", true)]
    [InlineData(".webp2", false)]
    [InlineData(".txt", false)]
    [InlineData(".webm", false)]
    [InlineData("", false)]
    public void IsSupported_IsCaseInsensitive_AndRejectsUnknown(string extension, bool expected)
    {
        Assert.Equal(expected, ImageFileTypes.IsSupported(extension));
    }

    [Fact]
    public void IsSupported_NullExtension_IsRejected()
    {
        Assert.False(ImageFileTypes.IsSupported(null));
    }
}
