using System.Buffers;
using System.Text.Json;

namespace Novashot.Core.Cli;

/// <summary>
/// L-1 单实例转发协议（行式 JSON + '\n' 帧）。纯函数编解码，round-trip 有单测钉死。
///
/// 请求 wire 格式：{"v":1,"args":["full","--clipboard"]}\n
/// 响应 wire 格式：{"v":1,"ok":true,"exitCode":0,"message":"Saved to ..."}\n
///
/// 转发的是整条原始 argv（JSON 数组），接收方用 <see cref="CliArgs.Parse"/> 重新解析——
/// 含空格的 --path 值等由 JSON 转义保证无损。编码手写 Utf8JsonWriter / JsonDocument，
/// 不走反射序列化（trim 安全，Core 亦无反射 JSON 依赖）。一连接一命令，处理完即关。
/// </summary>
public static class CliPipeProtocol
{
    /// <summary>单帧上限。argv 走不到这个量级；超限视为恶意/坏帧直接拒绝。</summary>
    public const int MaxMessageBytes = 64 * 1024;

    public const byte FrameTerminator = (byte)'\n';

    private const int ProtocolVersion = 1;

    // ---------- 请求 ----------

    /// <summary>编码请求帧。args 总量超 <see cref="MaxMessageBytes"/> 抛 ArgumentException（调用方 argv 不可能这么大）。</summary>
    public static byte[] EncodeRequest(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var buffer = new ArrayBufferWriter<byte>(256);
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("v"u8, ProtocolVersion);
            writer.WriteStartArray("args"u8);
            foreach (var arg in args)
                writer.WriteStringValue(arg);
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        var payload = buffer.WrittenSpan.ToArray();
        if (payload.Length + 1 > MaxMessageBytes)
            throw new ArgumentException($"Request exceeds {MaxMessageBytes} bytes.", nameof(args));
        return [.. payload, FrameTerminator];
    }

    /// <summary>解码请求帧。容忍缺尾随 '\n'；非法 JSON / 非数组 args / 非字符串项 / 超限 → false + 原因。</summary>
    public static bool TryDecodeRequest(byte[] payload, out string[]? args, out string? error)
    {
        args = null;
        error = null;

        if (payload is null || payload.Length == 0)
        {
            error = "empty payload.";
            return false;
        }
        if (payload.Length > MaxMessageBytes)
        {
            error = $"payload exceeds {MaxMessageBytes} bytes.";
            return false;
        }
        // 帧内不允许裸换行（行式协议的硬约束；JSON 转义的 \n 不受影响）
        var terminatorIndex = Array.IndexOf(payload, FrameTerminator);
        if (terminatorIndex >= 0 && terminatorIndex != payload.Length - 1)
        {
            error = "payload contains an embedded newline.";
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(payload, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                MaxDepth = 4,
            });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                error = "expected a JSON object with an 'args' array.";
                return false;
            }
            // review M-2：版本字段必须存在且 == 1（闸门，不是摆设）；不符拒帧，未来演进时旧端可明确报因
            if (!root.TryGetProperty("v", out var vEl) || vEl.ValueKind != JsonValueKind.Number || vEl.GetInt32() != ProtocolVersion)
            {
                error = $"unsupported protocol version (expected {ProtocolVersion}).";
                return false;
            }
            if (!root.TryGetProperty("args", out var argsEl))
            {
                error = "expected a JSON object with an 'args' array.";
                return false;
            }
            if (argsEl.ValueKind != JsonValueKind.Array)
            {
                error = "'args' must be an array of strings.";
                return false;
            }

            var list = new List<string>(argsEl.GetArrayLength());
            foreach (var item in argsEl.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String)
                {
                    error = "'args' must be an array of strings.";
                    return false;
                }
                list.Add(item.GetString()!);
            }
            args = [.. list];
            return true;
        }
        catch (JsonException ex)
        {
            error = $"invalid JSON: {ex.Message}";
            return false;
        }
    }

    // ---------- 响应 ----------

    public static byte[] EncodeResponse(CliPipeResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        var buffer = new ArrayBufferWriter<byte>(256);
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("v"u8, ProtocolVersion);
            writer.WriteBoolean("ok"u8, response.Ok);
            writer.WriteNumber("exitCode"u8, response.ExitCode);
            writer.WriteString("message"u8, response.Message ?? string.Empty);
            writer.WriteEndObject();
        }

        var payload = buffer.WrittenSpan.ToArray();
        if (payload.Length + 1 > MaxMessageBytes)
            throw new ArgumentException($"Response exceeds {MaxMessageBytes} bytes.", nameof(response));
        return [.. payload, FrameTerminator];
    }

    /// <summary>解码响应帧。语义同 <see cref="TryDecodeRequest"/>；缺字段（ok/exitCode/message）→ false。</summary>
    public static bool TryDecodeResponse(byte[] payload, out CliPipeResponse? response, out string? error)
    {
        response = null;
        error = null;

        if (payload is null || payload.Length == 0)
        {
            error = "empty payload.";
            return false;
        }
        if (payload.Length > MaxMessageBytes)
        {
            error = $"payload exceeds {MaxMessageBytes} bytes.";
            return false;
        }
        var terminatorIndex = Array.IndexOf(payload, FrameTerminator);
        if (terminatorIndex >= 0 && terminatorIndex != payload.Length - 1)
        {
            error = "payload contains an embedded newline.";
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(payload, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                MaxDepth = 4,
            });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                error = "expected a JSON object.";
                return false;
            }
            // review M-2：版本闸门同请求侧
            if (!root.TryGetProperty("v", out var vEl) || vEl.ValueKind != JsonValueKind.Number || vEl.GetInt32() != ProtocolVersion)
            {
                error = $"unsupported protocol version (expected {ProtocolVersion}).";
                return false;
            }
            if (!root.TryGetProperty("ok", out var okEl) || okEl.ValueKind != JsonValueKind.True && okEl.ValueKind != JsonValueKind.False
                || !root.TryGetProperty("exitCode", out var codeEl) || codeEl.ValueKind != JsonValueKind.Number
                || !root.TryGetProperty("message", out var msgEl) || msgEl.ValueKind != JsonValueKind.String)
            {
                error = "response requires boolean 'ok', numeric 'exitCode' and string 'message'.";
                return false;
            }

            response = new CliPipeResponse(
                okEl.GetBoolean(),
                codeEl.GetInt32(),
                msgEl.GetString() ?? string.Empty);
            return true;
        }
        catch (JsonException ex)
        {
            error = $"invalid JSON: {ex.Message}";
            return false;
        }
    }
}
