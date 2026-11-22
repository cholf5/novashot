using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Novashot.Core.Library;

namespace Novashot.App.ViewModels;

/// <summary>Library 列表条目 VM：承载缩略图与展示文案。</summary>
public sealed partial class LibraryEntryViewModel : ObservableObject
{
    private readonly Bitmap? _thumbnail;

    public LibraryEntry Entry { get; }
    public Bitmap? Thumbnail => _thumbnail;
    public string FileName => Entry.FileName;
    public bool HasRemote => !string.IsNullOrEmpty(Entry.RemoteUrl);
    public string ProviderText => HasRemote ? Localization.Loc.T("Library.Uploaded", Entry.Provider) : "";
    public string DetailsText =>
        $"{Entry.CreatedAt.LocalDateTime:yyyy-MM-dd HH:mm}  ·  {Entry.Width}×{Entry.Height}  ·  {FormatSize(Entry.SizeBytes)}";

    public LibraryEntryViewModel(LibraryEntry entry, Bitmap? thumbnail)
    {
        Entry = entry;
        _thumbnail = thumbnail;
    }

    internal static string FormatSize(long bytes) => bytes switch
    {
        >= 1_048_576 => $"{bytes / 1_048_576.0:0.#} MB",
        >= 1024 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes} B",
    };
}

/// <summary>Library 窗口 VM：加载索引 + 异步补缩略图。</summary>
public sealed partial class LibraryViewModel : ObservableObject
{
    private readonly LibraryStore _store;
    private readonly string _thumbsDirectory;
    private readonly ILogger? _logger;
    private readonly Func<string, bool>? _tryDelete;

    [ObservableProperty]
    private System.Collections.ObjectModel.ObservableCollection<LibraryEntryViewModel> _entries = [];

    [ObservableProperty]
    private LibraryEntryViewModel? _selected;

    [ObservableProperty]
    private string _countText = "";

    public LibraryViewModel(LibraryStore store, string thumbsDirectory, ILogger? logger = null,
        Func<string, bool>? tryDelete = null)
    {
        _store = store;
        _thumbsDirectory = thumbsDirectory;
        _logger = logger;
        _tryDelete = tryDelete;
    }

    public LibraryEntryViewModel? SelectedEntry => Selected;

    /// <summary>读索引（CreatedAt 倒序 = 最新在上），再逐条异步补缩略图。</summary>
    public async Task LoadAsync()
    {
        List<LibraryEntry> entries;
        try
        {
            entries = await _store.LoadAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Library load failed");
            entries = [];
        }

        Entries = new System.Collections.ObjectModel.ObservableCollection<LibraryEntryViewModel>(
            entries
                .OrderByDescending(e => e.CreatedAt)
                .Select(e => new LibraryEntryViewModel(e, LibraryThumbnails.TryLoad(e))));
        Selected = Entries.FirstOrDefault();
        CountText = FormatCount(Entries.Count);
    }

    private static string FormatCount(int count) => count switch
    {
        0 => Localization.Loc.T("Library.Empty"),
        1 => Localization.Loc.T("Library.CountOne"),
        _ => Localization.Loc.T("Library.CountMany", count),
    };

    /// <summary>B-7：删除记录；deleteFile=true 时连磁盘文件一起删（默认 false = 现状只删记录）。
    /// 文件删除失败：记录保持已删（不回滚），状态栏提示但不崩。</summary>
    public async Task<bool> DeleteAsync(LibraryEntryViewModel item, bool deleteFile = false)
    {
        var (removed, fileResult) = await _store.RemoveAsync(item.Entry.Id, deleteFile,
            tryDelete: _tryDelete);
        if (removed)
        {
            Entries.Remove(item);
            CountText = FormatCount(Entries.Count);
            if (fileResult == LibraryFileDeleteResult.FailedButRecordDeleted)
            {
                _logger?.LogWarning("Library file delete failed (record removed): {Path}", item.Entry.LocalPath);
                CountText = Localization.Loc.T("Library.FileDeleteFailed");
            }
        }
        return removed;
    }
}
