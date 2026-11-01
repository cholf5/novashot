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

        // G-17（winshot）：删除带确认，防误触（记录本身可直接重建，但本地文件会真删）
        var file = item.Entry.LocalPath;
        var detail = string.IsNullOrEmpty(file) ? "仅删除历史记录" : $"本地文件 {file} 也将删除";
        var dialog = new Window
        {
            Title = "确认删除",
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            ShowInTaskbar = false,
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(20),
                Spacing = 14,
                Children =
                {
                    new Avalonia.Controls.TextBlock
                    {
                        Text = $"确定删除「{item.Entry.FileName}」？\n{detail}",
                        MaxWidth = 420,
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    },
                    new StackPanel
                    {
                        Orientation = Avalonia.Layout.Orientation.Horizontal,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                        Spacing = 8,
                        Children =
                        {
                            CancelButton("取消"),
                            OkButtonForDelete(item),
                        },
                    },
                },
            },
        };
        await dialog.ShowDialog(this);
    }

    private Button CancelButton(string label)
    {
        var b = new Button { Content = label, Padding = new Avalonia.Thickness(14, 5) };
        b.Click += (_, _) => Close();
        return b;
    }

    private Button OkButtonForDelete(ViewModels.LibraryEntryViewModel item)
    {
        var b = new Button
        {
            Content = "删除",
            Padding = new Avalonia.Thickness(14, 5),
        };
        b.Click += async (_, _) =>
        {
            Close();
            if (await _vm.DeleteAsync(item))
                _logger.LogDebug("Library entry deleted: {Id}", item.Entry.Id);
        };
        return b;
    }

    /// <summary>G-17：Del / Backspace 快捷删除（同按钮，走确认框）。</summary>
    private void OnListKeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (e.Key is not (Avalonia.Input.Key.Delete or Avalonia.Input.Key.Back)) return;
        if (_vm.SelectedEntry is null) return;
        e.Handled = true;
        OnDeleteClick(this, new RoutedEventArgs());
    }

    /// <summary>G-18（winshot Copy Path）：复制本地文件路径；无本地文件时提示。</summary>
    private void OnCopyPathClick(object? sender, RoutedEventArgs e)
    {
        if (_vm.SelectedEntry is not { } item) return;
        var path = item.Entry.LocalPath;
        if (string.IsNullOrEmpty(path))
        {
            _vm.CountText = "该记录没有本地文件";
            return;
        }
        if (_clipboard.TrySetText(path))
            _vm.CountText = "路径已复制";
        else
            _logger.LogWarning("Copy path to clipboard failed");
    }

    private async void OnEntryDoubleTapped(object? sender, RoutedEventArgs e) => OnOpenLocalClick(sender, e);

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
