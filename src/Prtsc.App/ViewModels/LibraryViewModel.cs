using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Prtsc.Core.Library;

namespace Prtsc.App.ViewModels;

/// <summary>Library 列表条目 VM：承载缩略图与展示文案。</summary>
public sealed partial class LibraryEntryViewModel : ObservableObject
{
    private readonly Bitmap? _thumbnail;

    public LibraryEntry Entry { get; }
    public Bitmap? Thumbnail => _thumbnail;
    public string FileName => Entry.FileName;
    public bool HasRemote => !string.IsNullOrEmpty(Entry.RemoteUrl);
    public string ProviderText => HasRemote ? $"[{Entry.Provider}] 已上传" : "";
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

    [ObservableProperty]
    private System.Collections.ObjectModel.ObservableCollection<LibraryEntryViewModel> _entries = [];

    [ObservableProperty]
    private LibraryEntryViewModel? _selected;

    [ObservableProperty]
    private string _countText = "";

    public LibraryViewModel(LibraryStore store, string thumbsDirectory, ILogger? logger = null)
    {
        _store = store;
        _thumbsDirectory = thumbsDirectory;
        _logger = logger;
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
                .Select(e => new LibraryEntryViewModel(e, TryLoadThumbnail(e))));
        Selected = Entries.FirstOrDefault();
        CountText = Entries.Count == 0 ? "暂无记录" : $"{Entries.Count} 条记录";
    }

    private Bitmap? TryLoadThumbnail(LibraryEntry entry)
    {
        try
        {
            var path = System.IO.Path.Combine(_thumbsDirectory, $"thumb_{entry.Id}.png");
            return System.IO.File.Exists(path) ? new Bitmap(path) : null;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Thumbnail load failed for {Id}", entry.Id);
            return null;
        }
    }

    public async Task<bool> DeleteAsync(LibraryEntryViewModel item)
    {
        var ok = await _store.RemoveAsync(item.Entry.Id);
        if (ok)
        {
            Entries.Remove(item);
            CountText = Entries.Count == 0 ? "暂无记录" : $"{Entries.Count} 条记录";
        }
        return ok;
    }
}
