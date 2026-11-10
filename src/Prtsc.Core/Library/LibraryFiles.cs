namespace Prtsc.Core.Library;

/// <summary>B-7：Library 关联磁盘文件的尽力删除（失败吞异常返 false，由决策层/UI 决定提示口径）。</summary>
public static class LibraryFiles
{
    /// <summary>尽力删文件；被占用/无权限等失败返 false 不抛。
    /// 文件不存在（含 Exists→Delete 竞窗）按「已不在磁盘」返 true——跨平台口径一致
    /// （POSIX 上 File.Delete 对 ENOENT 抛 FileNotFoundException，Windows 为 no-op）。</summary>
    public static bool TryDelete(string path)
    {
        try
        {
            File.Delete(path);
            return true;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            // 目标不存在（文件 ENOENT / 父目录已消失，含 Exists→Delete 竞窗）
            // = 验收「不报错」的静默分支；两者都是 IOException 子类，必须先于其兜底分支
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }
    }
}
