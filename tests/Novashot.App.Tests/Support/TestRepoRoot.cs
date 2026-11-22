namespace Novashot.App.Tests;

/// <summary>
/// 测试内定位仓库根：从测试输出目录（AppContext.BaseDirectory）向上找 <c>Novashot.slnx</c>。
/// 原 UiSnapshotTests.RepoRoot 的实现提升为公共 helper，供需要读仓库内文件（源码/资源）的测试共用。
/// </summary>
internal static class TestRepoRoot
{
    public static string Path { get; } = Find();

    private static string Find()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(System.IO.Path.Combine(dir.FullName, "Novashot.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? AppContext.BaseDirectory;
    }
}
