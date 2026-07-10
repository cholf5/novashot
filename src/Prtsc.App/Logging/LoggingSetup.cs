using Serilog;
using Serilog.Events;

namespace Prtsc.App.Logging;

public static class LoggingSetup
{
    public static string LogDirectory { get; private set; } = string.Empty;

    public static void Configure(string configDirectory)
    {
        LogDirectory = Path.Combine(configDirectory, "logs");
        Directory.CreateDirectory(LogDirectory);
        var logPath = Path.Combine(LogDirectory, "prtsc-.log");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
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
