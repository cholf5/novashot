using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Prtsc.Core;
using Prtsc.Core.Annotation;
using Prtsc.Core.Capture;
using Prtsc.Core.Clipboard;
using Prtsc.Core.Cloud;
using Prtsc.Core.Config;
using Prtsc.Core.Editor.Export;
using Prtsc.Core.Library;
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
    private readonly IUploadService? _upload;
    private readonly LibraryStore? _library;
    private readonly ILogger? _logger;

    public MainWindowViewModel(IClipboardService clipboard, IConfigStore? configStore = null,
        IUploadService? upload = null, LibraryStore? library = null, ILogger? logger = null)
    {
        _clipboard = clipboard;
        _configStore = configStore;
        _upload = upload;
        _library = library;
        _logger = logger;
    }

    /// <summary>DI 外（测试）直接 new 时的兼容构造。</summary>
    public MainWindowViewModel(IClipboardService clipboard, IConfigStore? configStore, ILogger? logger)
        : this(clipboard, configStore, upload: null, library: null, logger: logger)
    {
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
        var editor = new EditorViewModel(image);
        ApplyEditorDefaults(editor);
        Editor = editor;
        Mode = MainViewMode.Editor;
        StatusText = $"Captured {image.Width} x {image.Height} @ {image.ScaleFactor:0.##}x";
    }

    /// <summary>
    /// 新编辑器按 config 初始化（G-13/G-14）：EditorConfig 的背景/padding/圆角/阴影/内衬/边框
    /// 写入初始合成状态；IncludeBackground=false 时直接用剥离背景的派生状态。
    /// 无 config store（单元测试）时保持 EditorState.Default。
    /// </summary>
    private void ApplyEditorDefaults(EditorViewModel editor)
    {
        if (_configStore is null) return;
        try
        {
            var cfg = _configStore.Current;
            var e = cfg.Editor;
            var state = new Prtsc.Core.Editor.EditorState
            {
                Background = e.BackgroundColor.StartsWith("linear-gradient", StringComparison.OrdinalIgnoreCase)
                    ? Prtsc.Core.Editor.BackgroundKind.Gradient
                    : Prtsc.Core.Editor.BackgroundKind.SolidColor,
                BackgroundColor = e.BackgroundColor.StartsWith("linear-gradient", StringComparison.OrdinalIgnoreCase)
                    ? default
                    : Prtsc.Core.Editor.GradientParser.ParseColor(e.BackgroundColor) ?? default,
                BackgroundGradient = e.BackgroundColor.StartsWith("linear-gradient", StringComparison.OrdinalIgnoreCase)
                    ? e.BackgroundColor
                    : null,
                Padding = e.Padding,
                CornerRadius = e.CornerRadius,
                ShadowSize = e.ShadowSize,
                InsetSize = e.Inset,
                InsetColor = string.IsNullOrWhiteSpace(e.InsetBackgroundColor)
                    ? null
                    : Prtsc.Core.Editor.GradientParser.ParseColor(e.InsetBackgroundColor),
                BorderWidth = e.BorderEnabled ? e.BorderWeight : 0f,
                BorderColor = Prtsc.Core.Editor.GradientParser.ParseColor(e.BorderColor),
                Border = e.BorderType switch
                {
                    "outside" => Prtsc.Core.Editor.BorderStyle.Outside,
                    "inside" => Prtsc.Core.Editor.BorderStyle.Inside,
                    _ => Prtsc.Core.Editor.BorderStyle.Center,
                },
            };
            editor.CompositionState = Prtsc.Core.Editor.EditorStateStripping.StripBackground(
                state, includeBackground: cfg.Export.IncludeBackground).Clamped();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "ApplyEditorDefaults failed; keeping default composition state");
        }
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
            // G-9（winshot）：QuickSave 成功后自动复制文件路径（导出/粘贴后可直接粘进聊天）
            var copiedPath = _clipboard.TrySetText(path);
            StatusText = copiedPath ? "Saved " + path + " (path copied)" : "Saved " + path;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Quick save failed");
            StatusText = "Quick save failed: " + ex.Message;
        }
    }

    private bool CanQuickSave() => Editor is not null && _configStore is not null;

    // ---- M7b: cloud upload + library + pin ----

    [ObservableProperty]
    private bool _isUploading;

    /// <summary>上传当前合成结果；成功后 URL 复制到剪贴板并记入 Library。</summary>
    [RelayCommand(CanExecute = nameof(CanUpload))]
    private async Task UploadAsync(CancellationToken ct)
    {
        if (Editor is not { } editor) return;
        if (_upload is null || _configStore is null || _library is null)
        {
            StatusText = UploadCoordinator.NotConfiguredHint;
            return;
        }

        IsUploading = true;
        try
        {
            var coordinator = new UploadCoordinator(_upload, _configStore, _library, _logger);
            var outcome = await coordinator.UploadDocumentAsync(
                editor.Document, editor.CompositionState, ct: ct);

            if (!outcome.Ok)
            {
                StatusText = outcome.Error ?? "上传失败";
                return;
            }

            if (outcome.Url is { } url && _clipboard.TrySetText(url))
                StatusText = $"Uploaded {outcome.Entry?.FileName} — URL copied to clipboard";
            else
                StatusText = $"Uploaded {outcome.Entry?.FileName}";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Upload canceled";
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Upload failed");
            StatusText = "Upload failed: " + ex.Message;
        }
        finally
        {
            IsUploading = false;
        }
    }

    private bool CanUpload() => Editor is not null && !IsUploading &&
        (_upload is null || _configStore?.Current.Cloud.Provider is not (null or "none"));

    partial void OnIsUploadingChanged(bool value) => UploadCommand.NotifyCanExecuteChanged();

    /// <summary>Cloud.AutoUpload 开启且配置了渠道时，导出/复制后自动上传（入口由 View 层调）。</summary>
    public async Task UploadCurrentAsync(CancellationToken ct = default)
    {
        if (UploadCommand.CanExecute(null))
            await UploadAsync(ct);
    }

    /// <summary>Cloud.AutoUpload 配置值（View 层导出后判断用）。</summary>
    public bool IsAutoUploadEnabled =>
        _configStore is not null && _configStore.Current.Cloud.AutoUpload;

    /// <summary>Library 索引存储（View 层开 Library 窗口 / Pin 缩略图用；DI 外可能为 null）。</summary>
    public LibraryStore? Library => _library;

    /// <summary>剪贴板位图 → 编辑器（工具栏 Paste / Ctrl+V）。位图为空时回退 CF_HDROP 文件路径（G-11）。</summary>
    [RelayCommand]
    private void PasteFromClipboard()
    {
        // 所有权移交 CapturedImage（EnterEditor → EditorViewModel.Dispose 释放），不能 here-Dispose
        var bitmap = _clipboard.TryGetImage();
        if (bitmap is null)
        {
            // G-11（winshot）：资源管理器复制的文件列表——取第一个图片文件解码进编辑器
            var filePath = _clipboard.TryGetImageFilePath();
            if (filePath is null)
            {
                StatusText = "Clipboard has no image";
                return;
            }
            if (!ImportImageFile(filePath, out var failure))
            {
                StatusText = failure ?? "Clipboard file could not be opened";
                return;
            }
            return;
        }
        var captured = new CapturedImage(bitmap, new Prtsc.Core.Common.PixelRect(0, 0, bitmap.Width, bitmap.Height),
            scaleFactor: 1.0, CaptureSource.Clipboard);
        EnterEditor(captured);
    }

    /// <summary>
    /// 文件 → SKBitmap → 编辑器（I-1 导入与 G-11 HDROP 粘贴共用）。成功返回 true；
    /// 失败写 <paramref name="failure"/>。位图所有权随 CapturedImage 移交（M6c 交接坑：不 here-Dispose）。
    /// </summary>
    public bool ImportImageFile(string path, out string? failure)
    {
        failure = null;
        try
        {
            if (!File.Exists(path))
            {
                failure = "File not found: " + path;
                return false;
            }
            var bitmap = SKBitmap.Decode(path);
            if (bitmap is null)
            {
                failure = "Unsupported image format: " + Path.GetExtension(path);
                return false;
            }
            var captured = new CapturedImage(bitmap,
                new Prtsc.Core.Common.PixelRect(0, 0, bitmap.Width, bitmap.Height),
                scaleFactor: 1.0, CaptureSource.File);
            EnterEditor(captured);
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Import image file failed: {Path}", path);
            failure = "Import failed: " + ex.Message;
            return false;
        }
    }

    /// <summary>Pin 贴图参数（合成结果；View 层收到变更后开置顶窗）。</summary>
    public event EventHandler<PinnedImageRequestedEventArgs>? PinnedImageRequested;

    /// <summary>Pin 当前合成结果（工具栏 Pin / Ctrl+P）：弹置顶无边框小窗。</summary>
    [RelayCommand(CanExecute = nameof(CanCopy))]
    private void Pin()
    {
        if (Editor is not { } editor) return;
        try
        {
            using var image = ImageExporter.Render(editor.Document, editor.CompositionState);
            using var bitmap = SKBitmap.FromImage(image);
            if (bitmap.Width <= 0 || bitmap.Height <= 0)
            {
                StatusText = "Pin failed: empty image";
                return;
            }
            PinnedImageRequested?.Invoke(this, new PinnedImageRequestedEventArgs(bitmap.Copy()));
            StatusText = $"Pinned {bitmap.Width} x {bitmap.Height}";
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Pin failed");
            StatusText = "Pin failed: " + ex.Message;
        }
    }

    partial void OnEditorChanged(EditorViewModel? value)
    {
        CopyCommand.NotifyCanExecuteChanged();
        QuickSaveCommand.NotifyCanExecuteChanged();
        UploadCommand.NotifyCanExecuteChanged();
        PinCommand.NotifyCanExecuteChanged();
    }
}

/// <summary>Pin 请求事件参数：位图所有权移交订阅者（不收到则丢弃）。</summary>
public sealed class PinnedImageRequestedEventArgs : EventArgs
{
    public SKBitmap Bitmap { get; }

    public PinnedImageRequestedEventArgs(SKBitmap bitmap) => Bitmap = bitmap;
}
