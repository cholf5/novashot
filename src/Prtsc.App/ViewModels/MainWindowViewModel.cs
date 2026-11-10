using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Prtsc.App.Localization;
using Prtsc.Core;
using Prtsc.Core.Annotation;
using Prtsc.Core.Capture;
using Prtsc.Core.Clipboard;
using Prtsc.Core.Cloud;
using Prtsc.App.Notifications;
using Prtsc.Core.Config;
using Prtsc.Core.Editor.Export;
using Prtsc.Core.Library;
using Prtsc.Core.Notifications;
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

    /// <summary>N-1：桌面通知服务（Windows=WinRT Toast / 其余 Null 静默）。可为 null（DI 外旧构造）。</summary>
    private readonly INotificationService? _notifications;

    /// <summary>E-2：画布参数写盘防抖（内存回写在 UI 线程同步做，防抖的只是磁盘 IO）。</summary>
    private static readonly TimeSpan ConfigSaveDebounce = TimeSpan.FromMilliseconds(500);
    private CancellationTokenSource? _configSaveDebounceCts;
    private bool _configDirty;

    public MainWindowViewModel(IClipboardService clipboard, IConfigStore? configStore = null,
        IUploadService? upload = null, LibraryStore? library = null, ILogger? logger = null,
        Prtsc.Core.Editor.ColorHistory? colorHistory = null,
        INotificationService? notifications = null)
    {
        _clipboard = clipboard;
        _configStore = configStore;
        _upload = upload;
        _library = library;
        _logger = logger;
        _colorHistory = colorHistory;
        _notifications = notifications;
    }

    /// <summary>DI 外（测试）直接 new 时的兼容构造。</summary>
    public MainWindowViewModel(IClipboardService clipboard, IConfigStore? configStore, ILogger? logger)
        : this(clipboard, configStore, upload: null, library: null, logger: logger, colorHistory: null)
    {
    }

    /// <summary>E-1 M9：会话级取色历史（overlay 写入；面板历史行消费）。可为 null（无历史服务）。</summary>
    private readonly Prtsc.Core.Editor.ColorHistory? _colorHistory;

    /// <summary>
    /// E-1 M9：overlay 取色模式下点选的颜色。进入编辑器时经 StylePanel 既有路径写入描边色
    /// （<see cref="EnterEditor"/>），取消防捕获时清空（外层 OnAnyCancelled），不污染下一次会话。
    /// </summary>
    [ObservableProperty]
    private SKColor? _pendingStrokeColor;

    [ObservableProperty]
    private MainViewMode _mode = MainViewMode.CaptureIdle;

    [ObservableProperty]
    private string _statusText = Loc.T("Status.Ready");

    /// <summary>标题栏右侧的版本号（0.1.0）。</summary>
    public string VersionText { get; } =
        typeof(MainWindowViewModel).Assembly.GetName().Version is { } v
            ? $"{v.Major}.{v.Minor}.{v.Build}"
            : "0.0.0";

    /// <summary>主窗口空闲态的「最近 4 张」（来自 Library 索引）。</summary>
    [ObservableProperty]
    private IReadOnlyList<LibraryEntryViewModel> _recentEntries = Array.Empty<LibraryEntryViewModel>();

    public bool HasRecent => RecentEntries.Count > 0;

    /// <summary>重新读 Library 索引前 4 条（启动 / 导出后 / 回到空闲态时调用）。</summary>
    public async Task RefreshRecentAsync()
    {
        if (_library is null)
        {
            RecentEntries = Array.Empty<LibraryEntryViewModel>();
            OnPropertyChanged(nameof(HasRecent));
            return;
        }

        try
        {
            var entries = await _library.LoadAsync().ConfigureAwait(true);
            RecentEntries = entries
                .OrderByDescending(e => e.CreatedAt)
                .Take(4)
                .Select(e => new LibraryEntryViewModel(e, LibraryThumbnails.TryLoad(e)))
                .ToList();
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Recent shots load failed");
            RecentEntries = Array.Empty<LibraryEntryViewModel>();
        }

        OnPropertyChanged(nameof(HasRecent));
    }

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
        var editor = new EditorViewModel(image, _colorHistory);
        ApplyEditorDefaults(editor);
        editor.CompositionPanelViewModel.CanvasStateChanged = OnCanvasStateChanged;
        ApplyPendingStroke(editor);
        Editor = editor;
        Mode = MainViewMode.Editor;
        StatusText = Loc.T("Status.Captured", image.Width, image.Height, image.ScaleFactor.ToString("0.##"));
    }

    /// <summary>
    /// E-1 M9：把 overlay 取的挂起色经 StylePanel 既有 Stroke 路径写入（进编辑器时必无选中标注
    /// → 落在 CurrentStyle 上，后续新标注沿用该色，语义与预设色点击一致）。消费即清空。
    /// </summary>
    private void ApplyPendingStroke(EditorViewModel editor)
    {
        if (PendingStrokeColor is not { } picked) return;
        editor.StylePanel.Stroke = picked;
        PendingStrokeColor = null;
    }

    /// <summary>
    /// 新编辑器按 config 初始化（G-13/G-14）：editor 段语义为「上次使用的画布参数」（E-2），
    /// 映射下沉 Core（EditorConfigMapper.FromConfig）；IncludeBackground=false 时剥离背景板。
    /// 无 config store（单元测试）时保持 EditorState.Default。
    /// </summary>
    private void ApplyEditorDefaults(EditorViewModel editor)
    {
        if (_configStore is null) return;
        try
        {
            var cfg = _configStore.Current;
            editor.CompositionState = Prtsc.Core.Editor.EditorConfigMapper.FromConfig(cfg.Editor, cfg.Export.IncludeBackground);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "ApplyEditorDefaults failed; keeping default composition state");
        }
    }

    // ---- E-2 画布参数持久化 ----

    /// <summary>画布面板改动 → config 同步回写（内存，UI 线程）+ 防抖写盘。</summary>
    private void OnCanvasStateChanged(Prtsc.Core.Editor.EditorState state)
    {
        if (_configStore is null) return;
        // export.includeBackground=false 时编辑器状态是剥离派生态（padding/阴影/内衬/边框已清零、
        // 背景为 None），回写会把用户未触碰的外观配置一并抹掉 → 剥离模式下不持久化，config 保持权威
        if (!_configStore.Current.Export.IncludeBackground) return;
        try
        {
            _configStore.Update(c => Prtsc.Core.Editor.EditorConfigMapper.ApplyTo(state, c.Editor));
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Canvas state persistence failed");
            return;
        }
        ScheduleConfigSave();
    }

    /// <summary>
    /// 标记 config 有未落盘改动并安排 500ms 防抖写盘。画布回写与关窗尺寸回写（G-16）共用：
    /// Update 只改内存，落盘统一走这里；进程退出前由 <see cref="FlushConfigSaveSync"/> 兜底。
    /// </summary>
    public void ScheduleConfigSave()
    {
        if (_configStore is null) return;
        _configDirty = true;
        CancelConfigDebounce();
        _configSaveDebounceCts = new CancellationTokenSource();
        _ = SaveAfterDebounceAsync(_configSaveDebounceCts);
    }

    private async Task SaveAfterDebounceAsync(CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(ConfigSaveDebounce, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return; // 新改动接管写盘 / flush 已落盘
        }
        if (_configStore is null) return;
        try
        {
            await _configStore.SaveAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Debounced config save failed");
            return;
        }
        // 期间有新改动 / flush 接管（CTS 已被换掉）：脏标归接管者管，过期 save 不清
        if (!ReferenceEquals(_configSaveDebounceCts, cts)) return;
        _configDirty = false;
    }

    /// <summary>离开编辑器时 flush：取消防抖立即落盘（异步），保证「改完后」已持久化。</summary>
    private void FlushConfigSave()
    {
        CancelConfigDebounce();
        if (!TakeConfigDirty()) return;
        _ = SaveFlushAsync();
    }

    private async Task SaveFlushAsync()
    {
        // 调用链上 TakeConfigDirty 已保证非 null（_configDirty 只在 _configStore 非 null 时置位）；
        // 这里显式收敛可空性，避免 await 期间字段被换掉的假想路径。
        var store = _configStore;
        if (store is null) return;
        try
        {
            await store.SaveAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _configDirty = true; // 落盘失败：恢复脏标，留待下次 flush / 退出重试
            _logger?.LogWarning(ex, "Config flush save failed");
        }
    }

    /// <summary>
    /// 进程退出（desktop.Exit）前同步落盘：取消防抖、阻塞等待写完，优雅退出不丢最后 500ms 内的改动。
    /// 与 <see cref="FlushConfigSave"/> 的差别是阻塞——进程即将退出，fire-and-forget 会被杀；
    /// 失败只记日志，不阻塞退出。
    /// </summary>
    public void FlushConfigSaveSync()
    {
        CancelConfigDebounce();
        var store = _configStore;
        if (!TakeConfigDirty() || store is null) return;
        try
        {
            store.SaveAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Config save on exit failed");
        }
    }

    private void CancelConfigDebounce()
    {
        _configSaveDebounceCts?.Cancel();
        _configSaveDebounceCts?.Dispose();
        _configSaveDebounceCts = null;
    }

    private bool TakeConfigDirty()
    {
        if (!_configDirty || _configStore is null) return false;
        _configDirty = false;
        return true;
    }

    public void ReturnToCapture()
    {
        FlushConfigSave();
        Editor?.Dispose();
        Editor = null;
        Mode = MainViewMode.CaptureIdle;
        StatusText = Loc.T("Status.Ready");
        _ = RefreshRecentAsync();
    }

    // ---- M6c: clipboard round-trip + quick save ----

    /// <summary>复制当前合成结果（含 CompositionState）到剪贴板（工具栏 Copy / Ctrl+C）。手动触发 → 通知。</summary>
    [RelayCommand(CanExecute = nameof(CanCopy))]
    private void Copy() => CopyToClipboard(notifyCopied: true);

    /// <summary>
    /// N-1 review I-1：导出管线内的自动复制入口——<b>不弹复制通知</b>（导出 toast 已带路径，
    /// 自动复制的 toast 零新信息，默认配置下确定性双弹是噪音）；手动复制（Copy 命令/Ctrl+C）仍通知。
    /// 抑制点在调用侧而非 NotificationPolicy（全局关 Copied 会误伤手动复制的合法反馈）。
    /// </summary>
    public void CopySilently() => CopyToClipboard(notifyCopied: false);

    private void CopyToClipboard(bool notifyCopied)
    {
        if (Editor is not { } editor) return;
        try
        {
            using var image = Prtsc.Core.Editor.Export.ImageExporter.Render(editor.Document, editor.CompositionState);
            using var bitmap = SKBitmap.FromImage(image);
            if (_clipboard.TrySetImage(bitmap))
            {
                StatusText = Loc.T("Status.Copied", bitmap.Width, bitmap.Height);
                if (notifyCopied)
                    NotifyCopied(bitmap.Width, bitmap.Height); // N-1：手动复制（自动复制走 CopySilently）
            }
            else
                StatusText = Loc.T("Status.CopyBusy");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Copy to clipboard failed");
            StatusText = Loc.T("Status.CopyFailed", ex.Message);
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
            StatusText = Loc.T("Status.QuickSaveUnavailable");
            return;
        }
        try
        {
            var path = Prtsc.Core.Clipboard.QuickSave.Save(_configStore.Current, editor.Document, editor.CompositionState);
            // G-9（winshot）：QuickSave 成功后自动复制文件路径（导出/粘贴后可直接粘进聊天）
            var copiedPath = _clipboard.TrySetText(path);
            StatusText = copiedPath ? Loc.T("Status.QuickSavedPath", path) : Loc.T("Status.QuickSaved", path);
            NotifyQuickSaved(path); // N-1：落盘成功即通知（托盘常驻场景无焦点窗口也能看到）
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Quick save failed");
            StatusText = Loc.T("Status.QuickSaveFailed", ex.Message);
        }
    }

    private bool CanQuickSave() => Editor is not null && _configStore is not null;

    // ---- N-1: desktop notifications（四处触发点的统一出口：SuccessNotifier 内判 policy + 自吞异常） ----

    /// <summary>导出成功通知（MainWindow 导出落盘后调用；QuickSave/复制/上传在 VM 内自调）。</summary>
    public void NotifyExported(string path) =>
        SuccessNotifier.Notify(_notifications, _configStore, NotificationEvent.Exported,
            Loc.T("Notify.Exported.Title"), Loc.T("Notify.Exported.Message", path), path, _logger);

    private void NotifyQuickSaved(string path) =>
        SuccessNotifier.Notify(_notifications, _configStore, NotificationEvent.QuickSaved,
            Loc.T("Notify.QuickSaved.Title"), Loc.T("Notify.QuickSaved.Message", path), path, _logger);

    private void NotifyCopied(int width, int height) =>
        SuccessNotifier.Notify(_notifications, _configStore, NotificationEvent.Copied,
            Loc.T("Notify.Copied.Title"), Loc.T("Notify.Copied.Message", width, height), filePath: null, _logger);

    /// <summary><paramref name="detail"/>：有 URL 带 URL，无 URL 退文件名。</summary>
    private void NotifyUploaded(string detail) =>
        SuccessNotifier.Notify(_notifications, _configStore, NotificationEvent.Uploaded,
            Loc.T("Notify.Uploaded.Title"), Loc.T("Notify.Uploaded.Message", detail), filePath: null, _logger);

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
                StatusText = outcome.Error ?? Loc.T("Status.UploadFailed", "");
                return;
            }

            if (outcome.Url is { } url && _clipboard.TrySetText(url))
            {
                StatusText = Loc.T("Status.UploadedUrl", outcome.Entry?.FileName, url);
                NotifyUploaded(url); // N-1：正文带 URL
            }
            else
            {
                StatusText = Loc.T("Status.Uploaded", outcome.Entry?.FileName);
                NotifyUploaded(outcome.Entry?.FileName ?? string.Empty);
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = Loc.T("Status.UploadCanceled");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Upload failed");
            StatusText = Loc.T("Status.UploadFailed", ex.Message);
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
                StatusText = Loc.T("Status.ClipboardEmpty");
                return;
            }
            if (!ImportImageFile(filePath, out var failure))
            {
                StatusText = failure ?? Loc.T("Status.ImportFailed", "");
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
                failure = Loc.T("Status.FileNotFound", path);
                return false;
            }
            var bitmap = SKBitmap.Decode(path);
            if (bitmap is null)
            {
                failure = Loc.T("Status.UnsupportedFormat", Path.GetExtension(path));
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
            failure = Loc.T("Status.ImportFailed", ex.Message);
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
                StatusText = Loc.T("Status.PinFailed", "");
                return;
            }
            PinnedImageRequested?.Invoke(this, new PinnedImageRequestedEventArgs(bitmap.Copy()));
            StatusText = Loc.T("Status.Pinned", bitmap.Width, bitmap.Height);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Pin failed");
            StatusText = Loc.T("Status.PinFailed", ex.Message);
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
