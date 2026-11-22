using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Novashot.Core.Autostart;

namespace Novashot.Platform.Windows.Autostart;

/// <summary>
/// HKCU\Software\Microsoft\Windows\CurrentVersion\Run\Novashot = "<exe>"（带引号）。
/// </summary>
public sealed class RegistryAutostartService : IAutostartService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Novashot";

    private readonly ILogger<RegistryAutostartService> _logger;

    public RegistryAutostartService(ILogger<RegistryAutostartService> logger)
    {
        _logger = logger;
    }

    public bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
            return key?.GetValue(ValueName) is string s && !string.IsNullOrEmpty(s);
        }
    }

    public void Enable()
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        key.SetValue(ValueName, "\"" + ExePath() + "\"");
        _logger.LogInformation("Autostart enabled at {Path}", ExePath());
    }

    public void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
        _logger.LogInformation("Autostart disabled");
    }

    public void SyncPath()
    {
        if (!IsEnabled) return;
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key == null) return;
        var current = key.GetValue(ValueName) as string;
        var expected = "\"" + ExePath() + "\"";
        if (!string.Equals(current, expected, StringComparison.OrdinalIgnoreCase))
        {
            key.SetValue(ValueName, expected);
            _logger.LogInformation("Autostart path synced to {Path}", ExePath());
        }
    }

    private static string ExePath()
    {
        var main = Environment.ProcessPath;
        return string.IsNullOrEmpty(main)
            ? System.Reflection.Assembly.GetEntryAssembly()?.Location ?? string.Empty
            : main;
    }
}
