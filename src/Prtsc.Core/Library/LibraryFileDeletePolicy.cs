namespace Prtsc.Core.Library;

/// <summary>「连文件一起删」联动结果（B-7）。记录删除与文件删除是两个独立事实。</summary>
public enum LibraryFileDeleteResult
{
    /// <summary>未勾选（或记录不存在）：与现状一致，只删记录。</summary>
    NotRequested,

    /// <summary>记录无本地文件（纯上传记录）：静默只删记录，不报错。</summary>
    NoLocalFile,

    /// <summary>文件已不在磁盘（被用户/外部移走）：静默只删记录，不报错。</summary>
    FileAlreadyGone,

    /// <summary>记录与磁盘文件都已删除。</summary>
    Deleted,

    /// <summary>文件删除失败（占用/无权限）：记录保持已删状态（不回滚），UI 提示。</summary>
    FailedButRecordDeleted,
}

/// <summary>B-7 删除决策表（纯逻辑，IO 由调用方注入探测）。</summary>
public static class LibraryFileDeletePolicy
{
    /// <summary>
    /// deleteFile=false → NotRequested（safer 默认不变）；
    /// 勾选后：无本地路径/文件已不在 → 静默只删记录；文件在且删成功 → Deleted；删失败 → FailedButRecordDeleted。
    /// </summary>
    public static LibraryFileDeleteResult Decide(bool deleteFile, string? localPath, bool fileExists, bool deleteSucceeded)
    {
        if (!deleteFile) return LibraryFileDeleteResult.NotRequested;
        if (string.IsNullOrEmpty(localPath)) return LibraryFileDeleteResult.NoLocalFile;
        if (!fileExists) return LibraryFileDeleteResult.FileAlreadyGone;
        return deleteSucceeded
            ? LibraryFileDeleteResult.Deleted
            : LibraryFileDeleteResult.FailedButRecordDeleted;
    }
}
