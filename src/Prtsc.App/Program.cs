using Avalonia;
using Prtsc.App.Logging;
using Prtsc.Core.Config;
using Serilog;

namespace Prtsc.App;

internal static class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static int Main(string[] args)
    {
        var configDir = JsonConfigStore.DefaultConfigDirectory();
        Directory.CreateDirectory(configDir);

        LoggingSetup.Configure(configDir);

        try
        {
            AppServices.Initialize(configDir);
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application terminated unexpectedly");
            return 1;
        }
        finally
        {
            LoggingSetup.Close();
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
