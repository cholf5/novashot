using System.Text;
using System.Xml.Linq;
using Prtsc.Core.Autostart;

namespace Prtsc.Platform.Mac.Autostart;

/// <summary>
/// launchd LaunchAgent plist 生成（纯函数，可测）。
/// </summary>
public static class MacAutostartPlist
{
    public const string Label = "com.cholf5.prtsc";

    /// <summary>~/Library/LaunchAgents/com.cholf5.prtsc.plist。</summary>
    public static string PlistPath(string homeDirectory) =>
        Path.Combine(homeDirectory, "Library", "LaunchAgents", $"{Label}.plist");

    /// <summary>
    /// RunAtLoad=true + KeepAlive=false：登录时启动、崩溃不自动拉起（用户手动开）。
    /// ProgramArguments 逐段写 string 节点——launchd 不解析 shell 引号，路径含空格天然安全。
    /// </summary>
    public static string BuildPlist(string executablePath, IReadOnlyList<string> extraArguments)
    {
        var args = new List<string> { executablePath };
        args.AddRange(extraArguments);

        var dict = new XElement("dict");
        void AddString(string key, string value)
        {
            dict.Add(new XElement("key", key));
            dict.Add(new XElement("string", value));
        }
        void AddBool(string key, bool value)
        {
            dict.Add(new XElement("key", key));
            dict.Add(new XElement(value ? "true" : "false"));
        }

        AddString("Label", Label);
        dict.Add(new XElement("key", "ProgramArguments"));
        var array = new XElement("array");
        foreach (var a in args) array.Add(new XElement("string", a));
        dict.Add(array);
        AddBool("RunAtLoad", true);
        AddBool("KeepAlive", false);
        AddString("ProcessDescription", "Prtsc screenshot tool autostart");

        var doc = new XDocument(new XDeclaration("1.0", "UTF-8", null), new XElement("plist", dict) { });
        doc.Root!.SetAttributeValue("version", "1.0");
        return doc.Declaration + Environment.NewLine + doc.Root;
    }
}

/// <summary>launchctl 系统调用门面（可 Fake）。</summary>
public interface IProcessRunner
{
    bool Run(string fileName, string arguments);
}

/// <summary>
/// launchd LaunchAgent 自启实现。
/// SMAppService 只针对打包 .app；本项目是 dotnet 免打包程序，LaunchAgent 是正确通道。
/// Enable: 写 plist → `launchctl bootstrap gui/$UID <plist>`（失败回退 `load -w`）。
/// Disable: `launchctl bootout gui/$UID/<label>` → 删 plist。
/// </summary>
public sealed class LaunchdAutostartService : IAutostartService
{
    private readonly IProcessRunner _runner;
    private readonly string _plistPath;

    public LaunchdAutostartService(IProcessRunner runner, string? homeDirectory = null)
    {
        _runner = runner;
        _plistPath = MacAutostartPlist.PlistPath(
            homeDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
    }

    public bool IsEnabled => File.Exists(_plistPath);

    public void Enable()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_plistPath)!);
        var currentExe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(currentExe))
        {
            throw new InvalidOperationException("Environment.ProcessPath is null; cannot configure autostart");
        }
        File.WriteAllText(_plistPath, MacAutostartPlist.BuildPlist(currentExe, []), Encoding.UTF8);
        Bootstrap();
    }

    public void Disable()
    {
        if (!IsEnabled) return;
        Bootout();
        File.Delete(_plistPath);
    }

    /// <summary>已启用但 exe 路径变化 → 重写 plist 并重新 bootstrap。</summary>
    public void SyncPath()
    {
        if (!IsEnabled) return;
        var currentExe = Environment.ProcessPath ?? string.Empty;
        var doc = XDocument.Load(_plistPath);
        var args = doc.Root?.Element("dict")?.Elements("key")
            .Where(k => k.Value == "ProgramArguments")
            .Select(k => k.NextNode).OfType<XElement>().FirstOrDefault()
            ?.Elements("string").Select(s => s.Value).ToArray() ?? [];

        if (args.Length == 0 || args[0] != currentExe)
        {
            File.WriteAllText(_plistPath, MacAutostartPlist.BuildPlist(currentExe, []), Encoding.UTF8);
            Bootout();
            Bootstrap();
        }
    }

    private void Bootstrap()
    {
        var uid = GetUid();
        // 现代接口；失败回退老接口 load -w
        if (!_runner.Run("/bin/launchctl", $"bootstrap gui/{uid} \"{_plistPath}\""))
        {
            _runner.Run("/bin/launchctl", $"load -w \"{_plistPath}\"");
        }
    }

    private void Bootout()
    {
        var uid = GetUid();
        _runner.Run("/bin/launchctl", $"bootout gui/{uid}/{MacAutostartPlist.Label}");
    }

    private static uint GetUid() => unchecked((uint)getuid());

    [System.Runtime.InteropServices.DllImport("libc")]
    private static extern int getuid();
}
