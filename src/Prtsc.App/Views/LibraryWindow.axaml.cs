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
        _vm = new LibraryViewModel(store, thumbsDirectory, _logger);
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
            _vm.CountText = Prtsc.App.Localization.Loc.T("Library.NoLink");
            return;
        }
        if (_clipboard.TrySetText(item.Entry.RemoteUrl))
            _vm.CountText = Prtsc.App.Localization.Loc.T("Library.LinkCopied");
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
        _vm.CountText = Prtsc.App.Localization.Loc.T("Library.FileGone");
    }

    private async void OnDeleteClick(object? sender, RoutedEventArgs e)
    {
        if (_vm.SelectedEntry is not { } item) return;

        // G-17（winshot）：删除带确认，防误触（记录本身可直接重建；本地文件不会动，仍留在磁盘上）。
        // B-7：有本地文件时提供「同时删除磁盘文件」勾选（默认不勾 = safer 现状不变）。
        var file = item.Entry.LocalPath;
        var detail = string.IsNullOrEmpty(file)
            ? Prtsc.App.Localization.Loc.T("Library.DeleteRecordOnly")
            : Prtsc.App.Localization.Loc.T("Library.DeleteFileStays");
        var alsoFileCheckBox = string.IsNullOrEmpty(file)
            ? null
            : new CheckBox
            {
                Content = Prtsc.App.Localization.Loc.T("Library.DeleteAlsoFile"),
                IsChecked = false,
            };
        var dialog = new Window
        {
            Title = Prtsc.App.Localization.Loc.T("Library.DeleteConfirmTitle"),
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            ShowInTaskbar = false,
        };
        var contentPanel = new StackPanel { Margin = new Avalonia.Thickness(20), Spacing = 14 };
        foreach (var child in BuildDeleteDialogChildren(dialog, item, detail, alsoFileCheckBox))
            contentPanel.Children.Add(child);
        dialog.Content = contentPanel;
        await dialog.ShowDialog(this);
    }

    private System.Collections.Generic.List<Avalonia.Controls.Control> BuildDeleteDialogChildren(
        Window dialog, ViewModels.LibraryEntryViewModel item, string detail, CheckBox? alsoFileCheckBox)
    {
        var children = new System.Collections.Generic.List<Avalonia.Controls.Control>
        {
            new Avalonia.Controls.TextBlock
            {
                Text = Prtsc.App.Localization.Loc.T("Library.DeleteConfirmRecord", item.Entry.FileName)
                       + "\n" + detail,
                MaxWidth = 420,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            },
        };
        if (alsoFileCheckBox is not null) children.Add(alsoFileCheckBox);
        children.Add(new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Spacing = 8,
            Children =
            {
                CancelButton(dialog, Prtsc.App.Localization.Loc.T("Common.Cancel")),
                OkButtonForDelete(dialog, item, alsoFileCheckBox),
            },
        });
        return children;
    }

    private Button CancelButton(Window dialog, string label)
    {
        var b = new Button { Content = label, Padding = new Avalonia.Thickness(14, 5) };
        // review I2：G-17 起误用 Close()（关掉的是 Library 主窗口 owner）；正确语义是关确认框
        b.Click += (_, _) => dialog.Close();
        return b;
    }

    private Button OkButtonForDelete(Window dialog, ViewModels.LibraryEntryViewModel item, CheckBox? alsoFileCheckBox)
    {
        var b = new Button
        {
            Content = Prtsc.App.Localization.Loc.T("Library.Delete"),
            Padding = new Avalonia.Thickness(14, 5),
        };
        b.Click += async (_, _) =>
        {
            // review C1：勾选状态必须在点击一刻读取（构建时采样会让勾选永远失效）
            var deleteFile = alsoFileCheckBox?.IsChecked == true;
            dialog.Close();
            if (await _vm.DeleteAsync(item, deleteFile))
                _logger.LogDebug("Library entry deleted: {Id} (file: {File})", item.Entry.Id, deleteFile);
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
            _vm.CountText = Prtsc.App.Localization.Loc.T("Library.NoLocalFile");
            return;
        }
        if (_clipboard.TrySetText(path))
            _vm.CountText = Prtsc.App.Localization.Loc.T("Library.PathCopied");
        else
            _logger.LogWarning("Copy path to clipboard failed");
    }

    private async void OnEntryDoubleTapped(object? sender, RoutedEventArgs e) => OnOpenLocalClick(sender, e);

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
