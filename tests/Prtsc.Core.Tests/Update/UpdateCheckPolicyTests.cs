using Prtsc.Core.Update;

namespace Prtsc.Core.Tests;

/// <summary>
/// C-1/R-1 更新检查决策纯函数：System.Version 解析语义钉死。
/// 裁决：prerelease 字符串（如 "1.3.0-beta"）System.Version 解析失败 → 不提示（宁可少提示不误弹）。
/// </summary>
public class UpdateCheckPolicyTests
{
    // —— ShouldCheckOnStartup：三条件与（isPackaged × 开关 × FeedUrl） ——

    [Theory]
    [InlineData(true, true, "https://example.com/releases", true)]
    [InlineData(false, true, "https://example.com/releases", false)] // 未打包（开发跑/源码运行）→ 永不检查
    [InlineData(true, false, "https://example.com/releases", false)] // 开关关
    [InlineData(true, true, "", false)]                              // FeedUrl 空
    [InlineData(true, true, "   ", false)]                           // FeedUrl 全空白
    [InlineData(true, true, null, false)]                            // FeedUrl null
    [InlineData(false, false, "", false)]
    public void ShouldCheckOnStartup_RequiresAllThreeConditions(
        bool isPackaged, bool checkOnStartup, string? feedUrl, bool expected)
    {
        Assert.Equal(expected, UpdateCheckPolicy.ShouldCheckOnStartup(isPackaged, checkOnStartup, feedUrl));
    }

    // —— ShouldPrompt：update > current 且 != skipped 才提示 ——

    [Fact]
    public void ShouldPrompt_HigherVersion_ReturnsTrue()
    {
        Assert.True(UpdateCheckPolicy.ShouldPrompt("0.2.0", "0.1.0", ""));
    }

    [Fact]
    public void ShouldPrompt_EqualVersion_ReturnsFalse()
    {
        Assert.False(UpdateCheckPolicy.ShouldPrompt("0.1.0", "0.1.0", ""));
    }

    [Fact]
    public void ShouldPrompt_LowerVersion_ReturnsFalse()
    {
        Assert.False(UpdateCheckPolicy.ShouldPrompt("0.0.9", "0.1.0", ""));
    }

    [Fact]
    public void ShouldPrompt_FourPartRevision_CountsAsHigher()
    {
        Assert.True(UpdateCheckPolicy.ShouldPrompt("0.1.0.1", "0.1.0", ""));
    }

    [Fact]
    public void ShouldPrompt_SkippedVersionSame_ReturnsFalse()
    {
        Assert.False(UpdateCheckPolicy.ShouldPrompt("0.2.0", "0.1.0", "0.2.0"));
    }

    [Fact]
    public void ShouldPrompt_SkippedVersionDifferent_ReturnsTrue()
    {
        // 用户跳过 0.2.0 后来了 0.3.0 → 要再提示
        Assert.True(UpdateCheckPolicy.ShouldPrompt("0.3.0", "0.1.0", "0.2.0"));
    }

    [Fact]
    public void ShouldPrompt_UnparseableSkippedVersion_IgnoresSkip()
    {
        // SkippedVersion 存了非法串（手改 config）→ 不吞提示
        Assert.True(UpdateCheckPolicy.ShouldPrompt("0.2.0", "0.1.0", "not-a-version"));
    }

    [Fact]
    public void ShouldPrompt_PrereleaseUpdateString_FailsSystemVersionParse_ReturnsFalse()
    {
        // "1.3.0-beta" 用 System.Version 解析失败 → 不提示（Velopack feed 不会推这类串给稳定渠道）
        Assert.False(UpdateCheckPolicy.ShouldPrompt("1.3.0-beta", "0.1.0", ""));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("v0.2.0")] // 'v' 前缀 System.Version 不认
    public void ShouldPrompt_UnparseableUpdateVersion_ReturnsFalse(string? update)
    {
        Assert.False(UpdateCheckPolicy.ShouldPrompt(update, "0.1.0", ""));
    }

    [Fact]
    public void ShouldPrompt_UnparseableCurrentVersion_ReturnsFalse()
    {
        Assert.False(UpdateCheckPolicy.ShouldPrompt("0.2.0", "dev", ""));
    }

    [Fact]
    public void ShouldPrompt_TristateNulls_ReturnsFalse()
    {
        Assert.False(UpdateCheckPolicy.ShouldPrompt(null, null, null));
    }
}
