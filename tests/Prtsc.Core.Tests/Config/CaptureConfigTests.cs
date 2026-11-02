using System.Text.Json;
using Prtsc.Core.Config;

namespace Prtsc.Core.Tests;

/// <summary>
/// 捕获配置（S-1）：WGC 光标开关 IncludeCursor。
/// 默认 true 保持既有行为；字段名 camelCase 锁定。
/// </summary>
public class CaptureConfigTests
{
    [Fact]
    public void Defaults_IncludeCursorTrue()
    {
        Assert.True(new PrtscConfig().Capture.IncludeCursor);
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
}
