using System.Buffers;

namespace Novashot.Core.Cli;

/// <summary>
/// 行式帧读取（请求/响应共用）：读到 '\n' 为止返回该行字节（不含 '\n'）；流干净关闭且无数据 → null；
/// 超过 <see cref="CliPipeProtocol.MaxMessageBytes"/> 抛 IOException。异常输入容错交给协议层 decode。
/// </summary>
internal static class CliPipeFrame
{
    public static async Task<byte[]?> ReadAsync(Stream stream, CancellationToken ct)
    {
        var buffer = new ArrayBufferWriter<byte>(256);
        var chunk = new byte[1024];

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var read = await stream.ReadAsync(chunk, ct).ConfigureAwait(false);
            if (read == 0)
            {
                // EOF：有过数据但没等到 '\n' → 按已有数据交付（decode 决定合法性）；彻底无数据 → null
                return buffer.WrittenCount > 0 ? buffer.WrittenSpan.ToArray() : null;
            }

            for (var i = 0; i < read; i++)
            {
                if (chunk[i] == CliPipeProtocol.FrameTerminator)
                {
                    buffer.Write(chunk.AsSpan(0, i));
                    return buffer.WrittenSpan.ToArray();
                }
            }
            buffer.Write(chunk.AsSpan(0, read));

            if (buffer.WrittenCount > CliPipeProtocol.MaxMessageBytes)
                throw new IOException($"Pipe frame exceeds {CliPipeProtocol.MaxMessageBytes} bytes.");
        }
    }
}
