using Avalonia.Media.Imaging;
using Novashot.Core.Library;

namespace Novashot.App.ViewModels;

/// <summary>
/// Library 缩略图加载（LibraryWindow 列表与主窗口「最近」共用）。
/// 读不到就返回 null —— 缩略图只是装饰，绝不因为缺图让列表挂掉。
/// </summary>
internal static class LibraryThumbnails
{
    public static Bitmap? TryLoad(LibraryEntry entry)
    {
        try
        {
            var path = System.IO.Path.Combine(Novashot.Core.AppDataPaths.Thumbs, $"thumb_{entry.Id}.png");
            return System.IO.File.Exists(path) ? new Bitmap(path) : null;
        }
        catch
        {
            return null;
        }
    }
}
