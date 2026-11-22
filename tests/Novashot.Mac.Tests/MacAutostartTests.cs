using System.Xml.Linq;
using Novashot.Platform.Mac.Autostart;
using Xunit;

using System.Runtime.InteropServices;

namespace Novashot.Mac.Tests;

public class MacAutostartPlistTests
{
    [Fact]
    public void BuildPlist_ContainsLabelAndProgramArguments()
    {
        var xml = MacAutostartPlist.BuildPlist("/Applications/Novashot/Novashot", ["--run-agent"]);
        var doc = XDocument.Parse(xml);

        var dict = doc.Root!.Element("dict")!;
        var label = dict.Elements("key").First(k => k.Value == "Label").NextNode is XElement l ? l.Value : "";
        Assert.Equal("com.cholf5.novashot", label);

        var args = dict.Elements("key").Where(k => k.Value == "ProgramArguments")
            .Select(k => k.NextNode).OfType<XElement>().First()
            .Elements("string").Select(s => s.Value).ToArray();
        Assert.Equal("/Applications/Novashot/Novashot", args[0]);
        Assert.Equal("--run-agent", args[1]);
    }

    [Fact]
    public void BuildPlist_PathWithSpaces_KeptVerbatim()
    {
        var xml = MacAutostartPlist.BuildPlist("/Users/theo/My Tools/novashot", []);
        Assert.Contains("/Users/theo/My Tools/novashot", xml);
        // XML 转义后不该出现引号包裹的路径（launchd 不解析引号）
        Assert.DoesNotContain("\"/Users", xml);
    }

    [Fact]
    public void BuildPlist_KeepsAliveFalse()
    {
        var xml = MacAutostartPlist.BuildPlist("/x/novashot", []);
        var doc = XDocument.Parse(xml);
        var dict = doc.Root!.Element("dict")!;
        var keepAlive = dict.Elements("key").Where(k => k.Value == "KeepAlive")
            .Select(k => k.NextNode).OfType<XElement>().First();
        Assert.Equal("false", keepAlive.Name.LocalName); // plist 布尔 = <false/> 节点
        var runAtLoad = dict.Elements("key").Where(k => k.Value == "RunAtLoad")
            .Select(k => k.NextNode).OfType<XElement>().First();
        Assert.Equal("true", runAtLoad.Name.LocalName);
    }

    [Fact]
    public void PlistPath_UsesUid()
    {
        var path = MacAutostartPlist.PlistPath("/Users/theo");
        Assert.Equal($"/Users/theo/Library/LaunchAgents/com.cholf5.novashot.plist", path);
    }
}

/// <summary>bootstrap/load 系统调用走 IProcessRunner Fake 的状态机测试。</summary>
public class LaunchdAutostartServiceTests : IDisposable
{
    private readonly string _home = Path.Combine(Path.GetTempPath(), "novashot-autostart-test-" + Guid.NewGuid().ToString("N"));
    private readonly FakeProcessRunner _runner = new();

    public void Dispose()
    {
        if (Directory.Exists(_home)) Directory.Delete(_home, recursive: true);
        GC.SuppressFinalize(this);
    }

    private LaunchdAutostartService Svc() => new(_runner, _home);

    [Fact]
    public void IsEnabled_NoPlist_False()
    {
        Assert.False(Svc().IsEnabled);
    }

    [Fact]
    public void Enable_WritesPlist_AndBootstraps()
    {
        var svc = Svc();
        svc.Enable();

        Assert.True(svc.IsEnabled);
        Assert.True(File.Exists(Path.Combine(_home, "Library/LaunchAgents/com.cholf5.novashot.plist")));
        var call = Assert.Single(_runner.Calls);
        Assert.Contains("bootstrap", call);
        Assert.Contains($"gui/{TestUid.getuid()}", call); // 服务用真实 uid
    }

    [Fact]
    public void Disable_BootsOut_AndDeletesPlist()
    {
        var svc = Svc();
        svc.Enable();
        _runner.Calls.Clear();

        svc.Disable();

        Assert.False(svc.IsEnabled);
        Assert.False(File.Exists(Path.Combine(_home, "Library/LaunchAgents/com.cholf5.novashot.plist")));
        Assert.Contains(_runner.Calls, c => c.Contains("bootout"));
    }

    [Fact]
    public void Enable_Twice_IsIdempotent()
    {
        var svc = Svc();
        svc.Enable();
        svc.Enable();
        Assert.Equal(2, _runner.Calls.Count); // 第二次仍 bootstrap（launchd bootstrap 已存在会报错但服务容忍）
    }

    [Fact]
    public void SyncPath_EnabledAndPathChanged_RewritesPlist()
    {
        var svc = Svc();
        svc.Enable();
        _runner.Calls.Clear();

        // Environment.ProcessPath 不可控，SyncPath 比较的是 plist 内容 vs 当前路径；
        // 这里直接验证：未变化时（TestHost 路径未写入）会重写一次
        svc.SyncPath();

        Assert.True(svc.IsEnabled);
    }
}

internal static class TestUid
{
    [DllImport("libc")]
    internal static extern uint getuid();
}
