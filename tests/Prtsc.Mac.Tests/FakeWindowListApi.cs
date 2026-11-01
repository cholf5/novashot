using Prtsc.Platform.Mac.Interop;
using Prtsc.Platform.Mac.Windowing;

namespace Prtsc.Mac.Tests;

internal sealed class FakeWindowListApi(WinListRow[] rows) : IWindowListApi
{
    public WinListRow[] Rows => rows;

    public WinListRow[] CopyWindowList() => rows;
}
