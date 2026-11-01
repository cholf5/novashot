using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Microsoft.Extensions.Logging;
using Prtsc.App.ViewModels;
using Prtsc.Core.Clipboard;
using Prtsc.Core.Library;

namespace Prtsc.App.Views;

/// <summary>
/// Library 历史窗口（M7b）：列出最近截图（缩略图 + 文件名 + 时间），支持复制链接 / 打开图片 / 删除记录。
/// </summary>
public partial class LibraryWindow : Window
{
    private readonly LibraryViewModel _vm;
    private readonly IClipboardService _clipboard;
    private readonly ILogger<LibraryWindow> _logger;

    public LibraryWindow()
        : this(new LibraryStore(
                Microsoft.Extensions.Logging.Abstractions.NullLogger<LibraryStore>.Instance),
            Prtsc.Core.AppDataPaths.Thumbs,
            AppServices.GetRequiredService<IClipboardService>())
    { }

    public LibraryWindow(LibraryStore store, string thumbsDirectory, IClipboardService clipboard,
        ILogger<LibraryWindow>? logger = null)
    {
        _clipboard = clipboard;
        _logger = logger ?? AppServices.GetRequiredService<ILogger<LibraryWindow>>();
        _vm = new LibraryViewModel(store, thumbsDirectory);
        DataContext = _vm;
        InitializeComponent();
        _ = _vm.LoadAsync();
    }

    private void OnRefreshClick(object? sender, RoutedEventArgs e) => _ = _vm.LoadAsync();

    private void OnCopyUrlClick(object? sender, RoutedEventArgs e)
    {
        if (_vm.SelectedEntry is not { } item) return;
        if (string.IsNullOrEmpty(item.Entry.RemoteUrl))
        {
            _vm.CountText = "该记录没有上传链接";
            return;
        }
        if (_clipboard.TrySetText(item.Entry.RemoteUrl))
            _vm.CountText = "链接已复制";
        else
            _logger.LogWarning("Copy URL to clipboard failed");
    }

    private async void OnOpenLocalClick(object? sender, RoutedEventArgs e)
    {
        if (_vm.SelectedEntry is not { } item) return;
        var entry = item.Entry;

        // 优先本地文件；没有则用远程 URL（上传记录）
        if (!string.IsNullOrEmpty(entry.LocalPath) && File.Exists(entry.LocalPath))
        {
            await TopLevel.GetTopLevel(this)!.Launcher.LaunchFileInfoAsync(new FileInfo(entry.LocalPath));
            return;
        }
        if (!string.IsNullOrEmpty(entry.RemoteUrl))
        {
            await TopLevel.GetTopLevel(this)!.Launcher.LaunchUriAsync(new Uri(entry.RemoteUrl));
            return;
        }
        _vm.CountText = "本地文件不存在且无远程链接";
    }

    private async void OnDeleteClick(object? sender, RoutedEventArgs e)
    {
        if (_vm.SelectedEntry is not { } item) return;
        if (await _vm.DeleteAsync(item))
            _logger.LogDebug("Library entry deleted: {Id}", item.Entry.Id);
    }

    private async void OnEntryDoubleTapped(object? sender, RoutedEventArgs e) => OnOpenLocalClick(sender, e);

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
