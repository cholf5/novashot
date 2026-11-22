namespace Novashot.Core;

/// <summary>
/// %AppData%\Novashot 数据文件/目录的统一路径。overrideRoot 供测试与便携模式注入。
/// </summary>
public static class AppDataPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Novashot");

    /// <summary>Library 缩略图目录（thumb_{id}.png）。</summary>
    public static string Thumbs => Path.Combine(Root, "Thumbs");

    public static string LibraryFile => Path.Combine(Root, "library.json");
}
