using Novashot.Platform.Mac.Autostart;

namespace Novashot.Mac.Tests;

/// <summary>launchctl 调用 Fake：记录命令行，不真跑。</summary>
internal sealed class FakeProcessRunner : IProcessRunner
{
    public List<string> Calls { get; } = [];

    public int CurrentUid { get; init; } = 501;

    public bool Run(string fileName, string arguments)
    {
        Calls.Add($"{fileName} {arguments}");
        return true;
    }
}
