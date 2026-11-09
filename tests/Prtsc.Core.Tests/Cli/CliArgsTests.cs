using Prtsc.Core.Cli;
using Prtsc.Core.Common;
using Prtsc.Core.Editor.Export;

namespace Prtsc.Core.Tests;

/// <summary>
/// L-1 CLI 参数解析纯函数：合法/非法/钳制/geometry/默认导出目标/帮助位。
/// 解析失败必须带原因（调用方 usage error + 退出码 2），不允许静默回退。
/// </summary>
public class CliArgsTests
{
    // ---------- 模式 ----------

    [Fact]
    public void Parse_EmptyArgs_DefaultsToGui()
    {
        var r = CliArgs.Parse([]);
        Assert.True(r.Success);
        Assert.Equal(CliCaptureMode.Gui, r.Command!.Mode);
        Assert.Equal(0, r.Command.DelaySeconds);
        Assert.False(r.Command.Clipboard);
        Assert.Equal(ExportFormat.Png, r.Command.Format);
        Assert.Null(r.Command.Geometry);
        Assert.Null(r.Command.Path);
        Assert.False(r.Command.Help);
    }

    [Theory]
    [InlineData("gui", CliCaptureMode.Gui)]
    [InlineData("full", CliCaptureMode.Full)]
    [InlineData("region", CliCaptureMode.Region)]
    [InlineData("window", CliCaptureMode.Window)]
    [InlineData("FULL", CliCaptureMode.Full)]
    [InlineData("Region", CliCaptureMode.Region)]
    public void Parse_ModeNames_CaseInsensitive(string mode, CliCaptureMode expected)
    {
        var r = CliArgs.Parse([mode]);
        Assert.True(r.Success);
        Assert.Equal(expected, r.Command!.Mode);
    }

    [Fact]
    public void Parse_UnknownMode_FailsWithReason()
    {
        var r = CliArgs.Parse(["shot"]);
        Assert.False(r.Success);
        Assert.Null(r.Command);
        Assert.Contains("Unknown mode", r.Error);
        Assert.Contains("shot", r.Error);
    }

    // ---------- --help ----------

    [Fact]
    public void Parse_HelpAlone_GuiWithHelpFlag()
    {
        var r = CliArgs.Parse(["--help"]);
        Assert.True(r.Success);
        Assert.True(r.Command!.Help);
        Assert.Equal(CliCaptureMode.Gui, r.Command.Mode);
    }

    [Fact]
    public void Parse_ShortHelp_AfterMode_IsRecorded()
    {
        var r = CliArgs.Parse(["full", "-h"]);
        Assert.True(r.Success);
        Assert.True(r.Command!.Help);
        Assert.Equal(CliCaptureMode.Full, r.Command.Mode);
    }

    [Fact]
    public void HelpText_MentionsAllModesAndOptions()
    {
        foreach (var token in new[] { "gui", "full", "region", "window", "--delay", "--path",
                     "--clipboard", "--format", "--geometry", "--help", "Exit codes" })
        {
            Assert.Contains(token, CliArgs.HelpText);
        }
    }

    // ---------- --delay（0-60 钳制） ----------

    [Theory]
    [InlineData("0", 0)]
    [InlineData("5", 5)]
    [InlineData("60", 60)]
    [InlineData("90", 60)]   // 超上限钳制
    [InlineData("-3", 0)]    // 负数钳到 0
    public void Parse_Delay_ClampedToZeroThroughSixty(string raw, int expected)
    {
        var r = CliArgs.Parse(["full", "--delay", raw]);
        Assert.True(r.Success);
        Assert.Equal(expected, r.Command!.DelaySeconds);
    }

    [Fact]
    public void Parse_Delay_ShortFlag_Works()
    {
        var r = CliArgs.Parse(["full", "-d", "3"]);
        Assert.True(r.Success);
        Assert.Equal(3, r.Command!.DelaySeconds);
    }

    [Fact]
    public void Parse_Delay_NonInteger_Fails()
    {
        var r = CliArgs.Parse(["full", "--delay", "abc"]);
        Assert.False(r.Success);
        Assert.Contains("--delay", r.Error);
    }

    [Fact]
    public void Parse_Delay_MissingValue_Fails()
    {
        var r = CliArgs.Parse(["full", "--delay"]);
        Assert.False(r.Success);
        Assert.Contains("Missing value", r.Error);
    }

    [Fact]
    public void Parse_Delay_ValueLooksLikeOption_Fails()
    {
        var r = CliArgs.Parse(["full", "--delay", "--clipboard"]);
        Assert.False(r.Success);
        Assert.Contains("Missing value", r.Error);
    }

    // ---------- --clipboard / --path ----------

    [Fact]
    public void Parse_Clipboard_Flag()
    {
        var r = CliArgs.Parse(["full", "-c"]);
        Assert.True(r.Success);
        Assert.True(r.Command!.Clipboard);
        Assert.Null(r.Command.Path);
    }

    [Fact]
    public void Parse_Path_ValueWithSpaces_Preserved()
    {
        var r = CliArgs.Parse(["full", "-p", "/tmp/my shots"]);
        Assert.True(r.Success);
        Assert.Equal("/tmp/my shots", r.Command!.Path);
    }

    [Fact]
    public void Parse_Path_EmptyValue_Fails()
    {
        var r = CliArgs.Parse(["full", "--path", ""]);
        Assert.False(r.Success);
        Assert.Contains("--path", r.Error);
    }

    [Fact]
    public void Parse_Path_MissingValue_Fails()
    {
        var r = CliArgs.Parse(["full", "--path"]);
        Assert.False(r.Success);
        Assert.Contains("Missing value", r.Error);
    }

    [Fact]
    public void Parse_Path_ValueLooksLikeOption_Fails()
    {
        var r = CliArgs.Parse(["full", "--path", "--format"]);
        Assert.False(r.Success);
        Assert.Contains("Missing value", r.Error);
    }

    // ---------- 默认导出目标（对齐 flameshot：无 --path 无 --clipboard → clipboard） ----------

    [Fact]
    public void Parse_NoExportTarget_DefaultsToClipboard()
    {
        var r = CliArgs.Parse(["full"]);
        Assert.True(r.Success);
        Assert.True(r.Command!.Clipboard);
        Assert.Null(r.Command.Path);
    }

    [Fact]
    public void Parse_PathOnly_DoesNotForceClipboard()
    {
        var r = CliArgs.Parse(["full", "-p", "/tmp"]);
        Assert.True(r.Success);
        Assert.False(r.Command!.Clipboard);
        Assert.Equal("/tmp", r.Command.Path);
    }

    [Fact]
    public void Parse_PathAndClipboard_BothKept()
    {
        var r = CliArgs.Parse(["full", "-p", "/tmp", "--clipboard"]);
        Assert.True(r.Success);
        Assert.True(r.Command!.Clipboard);
        Assert.Equal("/tmp", r.Command.Path);
    }

    // ---------- --format ----------

    [Theory]
    [InlineData("png", ExportFormat.Png)]
    [InlineData("jpeg", ExportFormat.Jpeg)]
    [InlineData("webp", ExportFormat.WebP)]
    [InlineData("WEBP", ExportFormat.WebP)]
    public void Parse_Format_ValidValues(string raw, ExportFormat expected)
    {
        var r = CliArgs.Parse(["full", "--format", raw]);
        Assert.True(r.Success);
        Assert.Equal(expected, r.Command!.Format);
    }

    [Fact]
    public void Parse_Format_InvalidValue_Fails()
    {
        var r = CliArgs.Parse(["full", "--format", "gif"]);
        Assert.False(r.Success);
        Assert.Contains("png | jpeg | webp", r.Error);
    }

    [Fact]
    public void Parse_Format_MissingValue_Fails()
    {
        var r = CliArgs.Parse(["full", "--format"]);
        Assert.False(r.Success);
        Assert.Contains("Missing value", r.Error);
    }

    // ---------- --geometry ----------

    [Fact]
    public void Parse_Geometry_Valid()
    {
        var r = CliArgs.Parse(["region", "--geometry", "10,20,300,400"]);
        Assert.True(r.Success);
        Assert.Equal(new PixelRect(10, 20, 300, 400), r.Command!.Geometry);
        Assert.True(r.Command.IsHeadlessExport);
    }

    [Fact]
    public void Parse_Geometry_NegativeOrigin_Allowed()
    {
        var r = CliArgs.Parse(["region", "--geometry", "-10,-20,300,400"]);
        Assert.True(r.Success);
        Assert.Equal(new PixelRect(-10, -20, 300, 400), r.Command!.Geometry);
    }

    [Fact]
    public void Parse_Geometry_SpacesAfterCommas_Tolerated()
    {
        var r = CliArgs.Parse(["region", "--geometry", "0, 0, 1920, 1080"]);
        Assert.True(r.Success);
        Assert.Equal(new PixelRect(0, 0, 1920, 1080), r.Command!.Geometry);
    }

    [Theory]
    [InlineData("10,20,300")]
    [InlineData("10,20,300,400,1")]
    [InlineData("a,b,c,d")]
    [InlineData("0,0,0,100")]
    [InlineData("0,0,-5,100")]
    public void Parse_Geometry_Invalid_FailsWithReason(string raw)
    {
        var r = CliArgs.Parse(["region", "--geometry", raw]);
        Assert.False(r.Success);
        Assert.Contains("--geometry", r.Error);
    }

    [Fact]
    public void Parse_Geometry_WithFullMode_IllegalCombination()
    {
        var r = CliArgs.Parse(["full", "--geometry", "0,0,100,100"]);
        Assert.False(r.Success);
        Assert.Contains("region", r.Error);
    }

    [Fact]
    public void Parse_RegionWithoutGeometry_IsInteractiveNotHeadless()
    {
        var r = CliArgs.Parse(["region"]);
        Assert.True(r.Success);
        Assert.Null(r.Command!.Geometry);
        Assert.True(r.Command.IsInteractive);
        Assert.False(r.Command.IsHeadlessExport);
    }

    [Fact]
    public void Parse_Window_IsInteractive()
    {
        var r = CliArgs.Parse(["window"]);
        Assert.True(r.Success);
        Assert.True(r.Command!.IsInteractive);
        Assert.False(r.Command.IsHeadlessExport);
    }

    // ---------- 未知选项 ----------

    [Fact]
    public void Parse_UnknownOption_FailsWithHint()
    {
        var r = CliArgs.Parse(["full", "--recursive"]);
        Assert.False(r.Success);
        Assert.Contains("Unknown option '--recursive'", r.Error);
        Assert.Contains("--help", r.Error);
    }

    [Fact]
    public void Parse_CaptureOptionWithoutMode_Fails()
    {
        var r = CliArgs.Parse(["--clipboard"]);
        Assert.False(r.Success);
        Assert.Contains("Missing capture mode", r.Error);
    }

    // ---------- 组合 ----------

    [Fact]
    public void Parse_FullCombinedCommand()
    {
        var r = CliArgs.Parse(["region", "-d", "5", "-p", "/tmp/shots", "-c", "-f", "webp",
                               "--geometry", "100,200,800,600"]);
        Assert.True(r.Success);
        var cmd = r.Command!;
        Assert.Equal(CliCaptureMode.Region, cmd.Mode);
        Assert.Equal(5, cmd.DelaySeconds);
        Assert.Equal("/tmp/shots", cmd.Path);
        Assert.True(cmd.Clipboard);
        Assert.Equal(ExportFormat.WebP, cmd.Format);
        Assert.Equal(new PixelRect(100, 200, 800, 600), cmd.Geometry);
        Assert.True(cmd.IsHeadlessExport);
    }

    [Fact]
    public void Parse_NullArgs_TreatedAsEmpty()
    {
        var r = CliArgs.Parse(null);
        Assert.True(r.Success);
        Assert.Equal(CliCaptureMode.Gui, r.Command!.Mode);
    }

    [Fact]
    public void Parse_RepeatedOptions_LastWins()
    {
        var r = CliArgs.Parse(["full", "-d", "3", "-d", "7", "-f", "jpeg", "-f", "png"]);
        Assert.True(r.Success);
        Assert.Equal(7, r.Command!.DelaySeconds);
        Assert.Equal(ExportFormat.Png, r.Command.Format);
    }
}
