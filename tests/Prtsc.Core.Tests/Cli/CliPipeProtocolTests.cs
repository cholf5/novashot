using System.Text;
using Prtsc.Core.Cli;

namespace Prtsc.Core.Tests;

/// <summary>
/// L-1 管道协议纯函数：请求/响应编解码 round-trip + 异常输入容错（非法 JSON、非数组、
/// 非字符串项、嵌裸换行、超限、缺字段）。wire = 单行 JSON + '\n'。
/// </summary>
public class CliPipeProtocolTests
{
    // ---------- 请求 round-trip ----------

    [Fact]
    public void Request_RoundTrip_Basic()
    {
        var frame = CliPipeProtocol.EncodeRequest(["full", "--clipboard"]);
        Assert.Equal((byte)'\n', frame[^1]);

        Assert.True(CliPipeProtocol.TryDecodeRequest(frame, out var args, out var error));
        Assert.Null(error);
        Assert.Equal(["full", "--clipboard"], args);
    }

    [Fact]
    public void Request_RoundTrip_EmptyArgs()
    {
        var frame = CliPipeProtocol.EncodeRequest([]);
        Assert.True(CliPipeProtocol.TryDecodeRequest(frame, out var args, out _));
        Assert.Empty(args!);
    }

    [Fact]
    public void Request_RoundTrip_ValuesWithSpacesQuotesUnicode()
    {
        var original = new[] { "region", "-p", "/tmp/my \"shots\" 牌", "-d", "3" };
        var frame = CliPipeProtocol.EncodeRequest(original);
        Assert.True(CliPipeProtocol.TryDecodeRequest(frame, out var args, out _));
        Assert.Equal(original, args);
    }

    [Fact]
    public void Request_RoundTrip_NewlineInValue_EscapedByJson()
    {
        // 值里的换行被 JSON 转义，wire 上不出现裸 '\n'（行式帧不被破坏）
        var frame = CliPipeProtocol.EncodeRequest(["-p", "line1\nline2"]);
        var wireText = Encoding.UTF8.GetString(frame);
        Assert.Equal(1, wireText.Count(c => c == '\n')); // 仅帧终止符
        Assert.True(CliPipeProtocol.TryDecodeRequest(frame, out var args, out _));
        Assert.Equal("-p", args![0]);
        Assert.Equal("line1\nline2", args[1]);
    }

    [Fact]
    public void Request_MissingTrailingNewline_Tolerated()
    {
        var frame = CliPipeProtocol.EncodeRequest(["full", "-c"]);
        var withoutTerminator = frame[..^1];
        Assert.True(CliPipeProtocol.TryDecodeRequest(withoutTerminator, out var args, out _));
        Assert.Equal(["full", "-c"], args);
    }

    // ---------- 请求容错 ----------

    [Theory]
    [InlineData(new byte[] { })]
    [InlineData(new byte[] { 0x0A })]
    public void Request_EmptyOrBareNewline_Fails(byte[] payload)
    {
        Assert.False(CliPipeProtocol.TryDecodeRequest(payload, out var args, out var error));
        Assert.Null(args);
        Assert.NotNull(error);
    }

    [Fact]
    public void Request_InvalidJson_FailsWithReason()
    {
        var payload = Encoding.UTF8.GetBytes("not json\n");
        Assert.False(CliPipeProtocol.TryDecodeRequest(payload, out _, out var error));
        Assert.Contains("invalid JSON", error);
    }

    [Fact]
    public void Request_NonObjectJson_Fails()
    {
        var payload = Encoding.UTF8.GetBytes("[1,2]\n");
        Assert.False(CliPipeProtocol.TryDecodeRequest(payload, out _, out var error));
        Assert.Contains("args", error);
    }

    [Fact]
    public void Request_NonStringArgsItem_Fails()
    {
        var payload = Encoding.UTF8.GetBytes("{\"v\":1,\"args\":[\"full\",42]}\n");
        Assert.False(CliPipeProtocol.TryDecodeRequest(payload, out _, out var error));
        Assert.Contains("array of strings", error);
    }

    [Fact]
    public void Request_EmbeddedRawNewline_Fails()
    {
        var payload = Encoding.UTF8.GetBytes("{\"v\":1,\"args\":[\"full\"]}\n{\"v\":2}\n");
        Assert.False(CliPipeProtocol.TryDecodeRequest(payload, out _, out var error));
        Assert.Contains("embedded newline", error);
    }

    [Fact]
    public void Request_Oversize_Fails()
    {
        var payload = new byte[CliPipeProtocol.MaxMessageBytes + 1];
        payload[0] = (byte)'{';
        Assert.False(CliPipeProtocol.TryDecodeRequest(payload, out _, out var error));
        Assert.Contains("exceeds", error);
    }

    [Fact]
    public void EncodeRequest_OversizeArgs_Throws()
    {
        Assert.Throws<ArgumentException>(() => CliPipeProtocol.EncodeRequest(["-p", new string('x', CliPipeProtocol.MaxMessageBytes)]));
    }

    // ---------- 响应 round-trip ----------

    [Fact]
    public void Response_RoundTrip_Success()
    {
        var response = CliPipeResponse.Success("Saved to /tmp/prtsc_1.png (123 bytes)");
        var frame = CliPipeProtocol.EncodeResponse(response);
        Assert.Equal((byte)'\n', frame[^1]);

        Assert.True(CliPipeProtocol.TryDecodeResponse(frame, out var decoded, out var error));
        Assert.Null(error);
        Assert.True(decoded!.Ok);
        Assert.Equal(0, decoded.ExitCode);
        Assert.Equal(response.Message, decoded.Message);
    }

    [Fact]
    public void Response_RoundTrip_UsageError()
    {
        var response = CliPipeResponse.Usage("Unknown mode 'x'.");
        var frame = CliPipeProtocol.EncodeResponse(response);
        Assert.True(CliPipeProtocol.TryDecodeResponse(frame, out var decoded, out _));
        Assert.False(decoded!.Ok);
        Assert.Equal(2, decoded.ExitCode);
        Assert.Equal("Unknown mode 'x'.", decoded.Message);
    }

    [Fact]
    public void Response_RoundTrip_UnicodeMessage()
    {
        var response = CliPipeResponse.Fail(1, "抓帧失败：权限未授予");
        var frame = CliPipeProtocol.EncodeResponse(response);
        Assert.True(CliPipeProtocol.TryDecodeResponse(frame, out var decoded, out _));
        Assert.Equal("抓帧失败：权限未授予", decoded!.Message);
    }

    [Fact]
    public void Response_MissingTrailingNewline_Tolerated()
    {
        var frame = CliPipeProtocol.EncodeResponse(CliPipeResponse.Success("ok"));
        Assert.True(CliPipeProtocol.TryDecodeResponse(frame[..^1], out var decoded, out _));
        Assert.NotNull(decoded);
    }

    // ---------- 响应容错 ----------

    [Theory]
    [InlineData("{}\n")]                                    // 缺 ok/exitCode/message
    [InlineData("{\"v\":1}\n")]                             // 缺字段
    [InlineData("{\"ok\":true,\"exitCode\":0}\n")]          // 缺 message
    [InlineData("{\"ok\":true,\"exitCode\":\"0\",\"message\":\"m\"}\n")] // exitCode 非数字
    [InlineData("[1]\n")]                                   // 非对象
    [InlineData("garbage\n")]                               // 非法 JSON
    public void Response_Malformed_Fails(string wire)
    {
        var payload = Encoding.UTF8.GetBytes(wire);
        Assert.False(CliPipeProtocol.TryDecodeResponse(payload, out var response, out var error));
        Assert.Null(response);
        Assert.NotNull(error);
    }

    [Fact]
    public void Response_EmbeddedRawNewline_Fails()
    {
        var payload = Encoding.UTF8.GetBytes("{\"ok\":true,\"exitCode\":0,\"message\":\"a\nb\"}\n");
        Assert.False(CliPipeProtocol.TryDecodeResponse(payload, out _, out var error));
        Assert.Contains("embedded newline", error);
    }

    [Fact]
    public void Response_EmptyPayload_Fails()
    {
        Assert.False(CliPipeProtocol.TryDecodeResponse([], out _, out var error));
        Assert.Contains("empty", error);
    }
}
