namespace Prtsc.Core.Cli;

/// <summary>
/// 管道命令的处理结果（服务端 → 客户端）。ExitCode 遵循 CLI 约定：0 成功 / 1 失败 / 2 用法错误。
/// </summary>
public sealed record CliPipeResponse(bool Ok, int ExitCode, string Message)
{
    public static CliPipeResponse Success(string message) => new(true, 0, message);

    public static CliPipeResponse Fail(int exitCode, string message) => new(false, exitCode, message);

    public static CliPipeResponse Usage(string message) => new(false, CliExitCodes.Usage, message);
}

/// <summary>CLI 进程退出码约定（写进 handoff）：0 成功、1 一般失败、2 用法错误。</summary>
public static class CliExitCodes
{
    public const int Success = 0;
    public const int Failure = 1;
    public const int Usage = 2;
}
