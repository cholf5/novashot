using System.Text.Json;
using Novashot.Core.Config;

namespace Novashot.Core.Tests;

/// <summary>
/// C-1 Updater 配置：FeedUrl 字段 JSON 往返 + 未知字段容忍（旧 config / 未来版本 config 兼容）。
/// </summary>
public class UpdateConfigTests
{
    [Fact]
    public void Defaults_CheckOnStartupOn_SkippedAndFeedUrlEmpty()
    {
        var config = new NovashotConfig();

        Assert.True(config.Update.CheckOnStartup);
        Assert.Equal(string.Empty, config.Update.SkippedVersion);
        Assert.Equal(string.Empty, config.Update.FeedUrl);
    }

    [Fact]
    public void RoundTrip_PreservesFeedUrlAndSkippedVersion()
    {
        var original = new NovashotConfig
        {
            Update = new UpdateConfig
            {
                CheckOnStartup = false,
                SkippedVersion = "0.2.0",
                FeedUrl = "https://github.com/cholf5/novashot/releases",
            },
        };

        var json = JsonSerializer.Serialize(original, NovashotConfigJsonContext.Default.NovashotConfig);
        var parsed = JsonSerializer.Deserialize(json, NovashotConfigJsonContext.Default.NovashotConfig);

        Assert.NotNull(parsed);
        Assert.False(parsed!.Update.CheckOnStartup);
        Assert.Equal("0.2.0", parsed.Update.SkippedVersion);
        Assert.Equal("https://github.com/cholf5/novashot/releases", parsed.Update.FeedUrl);
    }

    [Fact]
    public void Deserialize_OldConfigWithoutUpdateNode_UsesDefaults()
    {
        const string json = """{"language":"zh-CN"}""";

        var parsed = JsonSerializer.Deserialize(json, NovashotConfigJsonContext.Default.NovashotConfig);

        Assert.NotNull(parsed);
        Assert.True(parsed!.Update.CheckOnStartup);
        Assert.Equal(string.Empty, parsed.Update.SkippedVersion);
        Assert.Equal(string.Empty, parsed.Update.FeedUrl);
    }

    [Fact]
    public void Deserialize_UnknownFieldsInsideUpdateNode_Tolerated()
    {
        // 未来版本加的字段（或手改的垃圾字段）不能让读取炸掉
        const string json = """
            {"update":{"checkOnStartup":false,"feedUrl":"https://example.com/feed.json","someFutureField":42}}
            """;

        var parsed = JsonSerializer.Deserialize(json, NovashotConfigJsonContext.Default.NovashotConfig);

        Assert.NotNull(parsed);
        Assert.False(parsed!.Update.CheckOnStartup);
        Assert.Equal("https://example.com/feed.json", parsed.Update.FeedUrl);
    }
}
