using Novashot.Platform.Linux.Autostart;

namespace Novashot.Linux.Tests;

/// <summary>XDG Autostart：desktop 文件生成（引号规则）、读写往返、Enable/Disable/SyncPath 状态机。</summary>
public class XdgAutostartTests
{
    private sealed class MemStore : IDesktopFileStore
    {
        public Dictionary<string, string> Files { get; } = [];
        public bool Exists(string path) => Files.ContainsKey(path);
        public void Write(string path, string content) => Files[path] = content;
        public void Delete(string path) => Files.Remove(path);
        public string? ReadExecutable(string path) =>
            Files.TryGetValue(path, out var content)
                ? content.Split('\n').FirstOrDefault(l => l.StartsWith("Exec=", StringComparison.Ordinal))?["Exec=".Length..]
                : null;
        public string? this[string key] => Files.TryGetValue(key, out var v) ? v : null;
    }

    [Fact]
    public void Build_SimplePath_NoQuotes()
    {
        var content = XdgAutostartDesktop.Build("/usr/local/bin/novashot");
        Assert.Contains("Exec=/usr/local/bin/novashot\n", content.Replace("\r\n", "\n"));
        Assert.Contains("Type=Application", content);
        Assert.Contains("Terminal=false", content);
    }

    [Fact]
    public void Build_PathWithSpaces_Quotes()
    {
        var content = XdgAutostartDesktop.Build("/home/user/My Apps/novashot");
        Assert.Contains("Exec=\"/home/user/My Apps/novashot\"", content);
    }

    [Fact]
    public void Build_PathWithQuotes_Escaped()
    {
        var content = XdgAutostartDesktop.Build("/opt/a\"b/novashot");
        Assert.Contains("Exec=\"/opt/a\\\"b/novashot\"", content);
    }

    [Fact]
    public void DesktopPath_FollowsXdgLayout()
    {
        Assert.Equal("/home/u/.config/autostart/novashot.desktop", XdgAutostartDesktop.DesktopPath("/home/u"));
    }

    [Fact]
    public void EnableDisable_Roundtrip()
    {
        var store = new MemStore();
        var home = "/home/u";
        var svc = new XdgAutostartService(store, home, executablePathProvider: () => "/opt/novashot/novashot");
        Assert.False(svc.IsEnabled);

        svc.Enable();
        Assert.True(svc.IsEnabled);
        var content = store[XdgAutostartDesktop.DesktopPath(home)]!;
        Assert.Contains("Exec=/opt/novashot/novashot", content);

        svc.Disable();
        Assert.False(svc.IsEnabled);
        Assert.False(store.Files.ContainsKey(XdgAutostartDesktop.DesktopPath(home)));
    }

    [Fact]
    public void SyncPath_RewritesWhenExeMoved()
    {
        var store = new MemStore();
        var home = "/home/u";
        var exePath = "/old/path/novashot";
        var svc = new XdgAutostartService(store, home, executablePathProvider: () => exePath);
        svc.Enable();
        Assert.Contains("/old/path/novashot", store[XdgAutostartDesktop.DesktopPath(home)]!);

        exePath = "/new/path/novashot";
        svc.SyncPath();
        Assert.Contains("/new/path/novashot", store[XdgAutostartDesktop.DesktopPath(home)]!);

        // 幂等：再 Sync 不变
        svc.SyncPath();
        Assert.Contains("/new/path/novashot", store[XdgAutostartDesktop.DesktopPath(home)]!);
    }

    [Fact]
    public void DesktopFileStore_ReadExecutable_StripsQuotes()
    {
        var store = new DesktopFileStore();
        var path = Path.Combine(Path.GetTempPath(), $"novashot-test-{Guid.NewGuid():N}", "novashot.desktop");
        try
        {
            store.Write(path, XdgAutostartDesktop.Build("/my dir/prog"));
            Assert.Equal("/my dir/prog", store.ReadExecutable(path));
        }
        finally
        {
            if (Directory.Exists(Path.GetDirectoryName(path)))
                Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }
}
