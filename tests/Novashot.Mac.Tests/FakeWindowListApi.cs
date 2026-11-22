using Novashot.Platform.Mac.Interop;
using Novashot.Platform.Mac.Windowing;

namespace Novashot.Mac.Tests;

internal sealed class FakeWindowListApi(WinListRow[] rows) : IWindowListApi
{
    public WinListRow[] Rows => rows;

    public WinListRow[] CopyWindowList() => rows;
}
