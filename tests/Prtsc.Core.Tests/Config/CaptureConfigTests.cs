using System.Text.Json;
using Prtsc.Core.Config;

namespace Prtsc.Core.Tests;

/// <summary>
/// 捕获配置（S-1 / D-1）：WGC 光标开关 IncludeCursor + 延时秒数 DelaySeconds。
/// 默认值保持既有行为（光标包含、不延时）；字段名 camelCase 锁定。
/// </summary>
public class CaptureConfigTests
{
    [Fact]
    public void Defaults_IncludeCursorTrue()
    {
        Assert.True(new PrtscConfig().Capture.IncludeCursor);
    }

    [Fact]
    public void Defaults_DelaySecondsZero()
    {
        Assert.Equal(0, new PrtscConfig().Capture.DelaySeconds); // 默认不延时，既有行为不变
    }

    [Fact]
    public void JsonRoundTrip_UsesCamelCaseIncludeCursor()
    {
        var config = new PrtscConfig { Capture = new CaptureConfig { IncludeCursor = false } };

        var json = JsonSerializer.Serialize(config, PrtscConfigJsonContext.Default.PrtscConfig);
        Assert.Contains("\"includeCursor\"", json);

        var restored = JsonSerializer.Deserialize(json, PrtscConfigJsonContext.Default.PrtscConfig);
        Assert.NotNull(restored);
        Assert.False(restored!.Capture.IncludeCursor);
    }

    [Fact]
    public void JsonRoundTrip_UsesCamelCaseDelaySeconds()
    {
        var config = new PrtscConfig { Capture = new CaptureConfig { DelaySeconds = 5 } };

        var json = JsonSerializer.Serialize(config, PrtscConfigJsonContext.Default.PrtscConfig);
        Assert.Contains("\"delaySeconds\"", json);

        var restored = JsonSerializer.Deserialize(json, PrtscConfigJsonContext.Default.PrtscConfig);
        Assert.NotNull(restored);
        Assert.Equal(5, restored!.Capture.DelaySeconds);
    }

    [Fact]
    public void JsonRoundTrip_MissingDelaySeconds_FallsBackToDefault()
    {
        // 旧版 config.json 没有 delaySeconds 字段：反序列化回默认 0（未知字段/缺失字段容忍）
        const string json = """{"capture":{"includeCursor":true}}""";
        var restored = JsonSerializer.Deserialize(json, PrtscConfigJsonContext.Default.PrtscConfig);
        Assert.NotNull(restored);
        Assert.Equal(0, restored!.Capture.DelaySeconds);
    }
}
