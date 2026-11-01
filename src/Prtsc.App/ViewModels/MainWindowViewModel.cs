using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Prtsc.Core.Annotation;
using Prtsc.Core.Capture;
using Prtsc.Core.Clipboard;
using Prtsc.Core.Config;
using SkiaSharp;

namespace Prtsc.App.ViewModels;

public enum MainViewMode
{
    CaptureIdle,
    Editor,
}

public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly IClipboardService _clipboard;
    private readonly IConfigStore? _configStore;
    private readonly ILogger? _logger;

    public MainWindowViewModel(IClipboardService clipboard, IConfigStore? configStore = null, ILogger? logger = null)
    {
        _clipboard = clipboard;
        _configStore = configStore;
        _logger = logger;
    }

    [ObservableProperty]
    private MainViewMode _mode = MainViewMode.CaptureIdle;

    [ObservableProperty]
    private string _statusText = "Ready. Press hotkey or use toolbar to capture.";

    /// <summary>
    /// 当前打开的编辑器（M4 起）。为 null 表示尚未进入编辑器。
    /// 生命期由主 VM 拥有；<see cref="EnterEditor"/> 会释放旧实例。
    /// </summary>
    [ObservableProperty]
    private EditorViewModel? _editor;

    public bool IsInCaptureMode => Mode == MainViewMode.CaptureIdle;
    public bool IsInEditorMode => Mode == MainViewMode.Editor;

    partial void OnModeChanged(MainViewMode value)
    {
        OnPropertyChanged(nameof(IsInCaptureMode));
        OnPropertyChanged(nameof(IsInEditorMode));
    }

    public void EnterEditor(CapturedImage image)
    {
        Editor?.Dispose();
        Editor = new EditorViewModel(image);
        Mode = MainViewMode.Editor;
        StatusText = $"Captured {image.Width} x {image.Height} @ {image.ScaleFactor:0.##}x";
    }

    public void ReturnToCapture()
    {
        Editor?.Dispose();
        Editor = null;
        Mode = MainViewMode.CaptureIdle;
        StatusText = "Ready.";
    }

    // ---- M6c: clipboard round-trip + quick save ----

    /// <summary>复制当前合成结果（含 CompositionState）到剪贴板（工具栏 Copy / Ctrl+C）。</summary>
    [RelayCommand(CanExecute = nameof(CanCopy))]
    private void Copy()
    {
        if (Editor is not { } editor) return;
        try
        {
            using var image = Prtsc.Core.Editor.Export.ImageExporter.Render(editor.Document, editor.CompositionState);
            using var bitmap = SKBitmap.FromImage(image);
            if (_clipboard.TrySetImage(bitmap))
                StatusText = $"Copied {bitmap.Width} x {bitmap.Height} to clipboard";
            else
                StatusText = "Copy failed: clipboard busy";
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Copy to clipboard failed");
            StatusText = "Copy failed: " + ex.Message;
        }
    }

    private bool CanCopy() => Editor is not null;

    /// <summary>按 config 无对话框直存（Ctrl+Q）。</summary>
    [RelayCommand(CanExecute = nameof(CanQuickSave))]
    private void QuickSave()
    {
        if (Editor is not { } editor) return;
        if (_configStore is null)
        {
            StatusText = "Quick save unavailable: no config store";
            return;
        }
        try
        {
            var path = Prtsc.Core.Clipboard.QuickSave.Save(_configStore.Current, editor.Document, editor.CompositionState);
            StatusText = "Saved " + path;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Quick save failed");
            StatusText = "Quick save failed: " + ex.Message;
        }
    }

    private bool CanQuickSave() => Editor is not null && _configStore is not null;

    /// <summary>剪贴板位图 → 编辑器（工具栏 Paste / Ctrl+V）。</summary>
    [RelayCommand]
    private void PasteFromClipboard()
    {
        // 所有权移交 CapturedImage（EnterEditor → EditorViewModel.Dispose 释放），不能 here-Dispose
        var bitmap = _clipboard.TryGetImage();
        if (bitmap is null)
        {
            StatusText = "Clipboard has no image";
            return;
        }
        var captured = new CapturedImage(bitmap, new Prtsc.Core.Common.PixelRect(0, 0, bitmap.Width, bitmap.Height),
            scaleFactor: 1.0, CaptureSource.Clipboard);
        EnterEditor(captured);
    }

    partial void OnEditorChanged(EditorViewModel? value)
    {
        CopyCommand.NotifyCanExecuteChanged();
        QuickSaveCommand.NotifyCanExecuteChanged();
    }
}
