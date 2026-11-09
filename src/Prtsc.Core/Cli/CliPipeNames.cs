using System.Security.Cryptography;
using System.Text;

namespace Prtsc.Core.Cli;

/// <summary>
/// L-1 单实例命名管道名。名字含每用户隔离段（用户名 SHA256 前 8 hex）：
/// - Windows：默认命名空间是机器级，快速用户切换/多会话会撞名，靠 tag 隔离；
/// - Unix：dotnet 落到 Path.GetTempPath()（macOS $TMPDIR 天然按用户隔离，但 Linux /tmp 共享），同样靠 tag 隔离。
/// 两个平台都用同一函数派生，客户端/服务端天然一致。
/// </summary>
public static class CliPipeNames
{
    public const string BaseName = "prtsc-cli";

    public static string ForCurrentUser()
    {
        var user = Environment.UserName;
        if (string.IsNullOrWhiteSpace(user))
            user = "unknown";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(user.Trim().ToLowerInvariant()));
        var tag = Convert.ToHexString(bytes.AsSpan(0, 4)).ToLowerInvariant();
        return $"{BaseName}-{tag}";
    }
}
