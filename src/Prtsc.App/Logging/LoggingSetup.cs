using Serilog;
using Serilog.Events;

namespace Prtsc.App.Logging;

public static class LoggingSetup
{
    public static string LogDirectory { get; private set; } = string.Empty;

    /// <summary>
    /// 配置日志。consoleSink=false 用于 L-1 CLI 进程：stdout/stderr 是脚本契约（结果/错误），
    /// Serilog 只写文件，避免日志行混入 CLI 输出；GUI（开发跑）保持 console sink 便于调试。
    /// 重复调用安全：替换前 Dispose 旧 logger（防双 sink / 缓冲丢失）。
    /// </summary>
    public static void Configure(string configDirectory, bool consoleSink = true)
    {
        LogDirectory = Path.Combine(configDirectory, "logs");
        Directory.CreateDirectory(LogDirectory);
        var logPath = Path.Combine(LogDirectory, "prtsc-.log");
        (Log.Logger as IDisposable)?.Dispose();

        var builder = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .Enrich.FromLogContext();
        if (consoleSink)
        {
            builder = builder.WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}");
        }
        Log.Logger = builder
            .WriteTo.File(
                path: logPath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        Log.Information("Prtsc starting. Config dir: {Dir}", configDirectory);
        Log.Information("Log dir: {Dir}", LogDirectory);
    }

    public static void Close()
    {
        Log.Information("Prtsc shutting down");
        Log.CloseAndFlush();
    }
}
