using Novashot.Core.Autostart;

namespace Novashot.Platform.Linux.Autostart;

/// <summary>
/// XDG Autostart .desktop 文件生成（纯函数，可测）。freedesktop Autostart 规范：
/// ~/.config/autostart/&lt;name&gt;.desktop，GNOME/KDE/XFCE/Cinnamon/MATE 通用。
/// </summary>
public static class XdgAutostartDesktop
{
    public const string EntryId = "novashot";

    /// <summary>~/.config/autostart/novashot.desktop。</summary>
    public static string DesktopPath(string homeDirectory) =>
        Path.Combine(homeDirectory, ".config", "autostart", $"{EntryId}.desktop");

    /// <summary>
    /// 生成 .desktop 内容。Terminal=false；Type=Application；
    /// X-GNOME-Autostart-enabled 兼容 GNOME 老版本开关语义。
    /// </summary>
    public static string Build(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        return $"""
            [Desktop Entry]
            Type=Application
            Name=Novashot
            Comment=Novashot screenshot tool autostart
            Exec={Quote(executablePath)}
            Terminal=false
            X-GNOME-Autostart-enabled=true
            """;
    }

    /// <summary>Exec 的引号规则：空格才包双引号并转义内部引号/反斜杠（Exec 规范）。</summary>
    internal static string Quote(string path) =>
        path.Contains(' ') || path.Contains('"') || path.Contains('\\')
            ? $"\"{path.Replace("\\", "\\\\").Replace("\"", "\\\"")}\""
            : path;
}

/// <summary>desktop 文件门面（读写可 Fake）。</summary>
public interface IDesktopFileStore
{
    bool Exists(string path);
    void Write(string path, string content);
    void Delete(string path);
    string? ReadExecutable(string path);
}

public sealed class DesktopFileStore : IDesktopFileStore
{
    public bool Exists(string path) => File.Exists(path);

    public void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content + Environment.NewLine);
    }

    public void Delete(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }

    public string? ReadExecutable(string path)
    {
        if (!File.Exists(path)) return null;
        foreach (var line in File.ReadLines(path))
        {
            // Exec=...（允许行内空格；引号由 Quote 的逆规则处理——这里只取到行尾再剥引号）
            if (line.StartsWith("Exec=", StringComparison.Ordinal))
            {
                var v = line["Exec=".Length..].Trim();
                if (v.StartsWith('"') && v.EndsWith('"') && v.Length >= 2)
                    return v[1..^1].Replace("\\\"", "\"").Replace("\\\\", "\\");
                return v;
            }
        }
        return null;
    }
}

/// <summary>
/// XDG Autostart 自启实现（对齐 LaunchdAutostartService 语义）：
/// Enable 写 .desktop；Disable 删文件；IsEnabled = 文件存在；
/// SyncPath 比对 Exec 与当前 exe，变了就重写。
/// executablePathProvider 可注入（测试）；默认 Environment.ProcessPath。
/// </summary>
public sealed class XdgAutostartService : IAutostartService
{
    private readonly IDesktopFileStore _store;
    private readonly string _desktopPath;
    private readonly Func<string?> _executablePathProvider;

    public XdgAutostartService(IDesktopFileStore? store = null, string? homeDirectory = null,
        Func<string?>? executablePathProvider = null)
    {
        _store = store ?? new DesktopFileStore();
        _desktopPath = XdgAutostartDesktop.DesktopPath(
            homeDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        _executablePathProvider = executablePathProvider ?? (() => Environment.ProcessPath);
    }

    public bool IsEnabled => _store.Exists(_desktopPath);

    public void Enable()
    {
        var exe = RequireExecutablePath();
        _store.Write(_desktopPath, XdgAutostartDesktop.Build(exe));
    }

    public void Disable() => _store.Delete(_desktopPath);

    public void SyncPath()
    {
        if (!IsEnabled) return;
        var current = _executablePathProvider() ?? string.Empty;
        var registered = _store.ReadExecutable(_desktopPath);
        if (!string.Equals(registered, current, StringComparison.Ordinal))
            _store.Write(_desktopPath, XdgAutostartDesktop.Build(current));
    }

    private string RequireExecutablePath() =>
        _executablePathProvider()
        ?? throw new InvalidOperationException("Environment.ProcessPath is null; cannot configure autostart");
}
