using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Novashot.App.Assets;
using Novashot.App.Localization;
using Novashot.App.Controls;
using Novashot.App.ViewModels;
using Novashot.Core.Annotation.Model;
using Novashot.Core.Annotation.Tools;
using Novashot.Core.Capture;
using Novashot.Core.Common;
using Novashot.Core.Clipboard;
using Novashot.Core.Config;
using Novashot.Core.Editor.Export;
using Novashot.Core.Windows;

namespace Novashot.App.Views;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _vm;
    private readonly ICaptureService _capture;
    private readonly IWindowService _windowService;
    private readonly ILogger<MainWindow> _logger;
    private readonly IConfigStore _configStore;
    private readonly Novashot.Core.Library.LibraryStore _libraryStore;

    private SkiaCanvas? _canvas;
    private TextBox? _textEditBox;
    private EditorViewModel? _subscribedEditor;

    public MainWindow() : this(
        AppServices.GetRequiredService<MainWindowViewModel>(),
        AppServices.GetRequiredService<ICaptureService>(),
        AppServices.GetRequiredService<IWindowService>(),
        AppServices.GetRequiredService<IConfigStore>(),
        AppServices.GetRequiredService<Novashot.Core.Library.LibraryStore>(),
        AppServices.GetRequiredService<ILogger<MainWindow>>())
    { }

    public MainWindow(MainWindowViewModel vm, ICaptureService capture, IWindowService windowService,
        IConfigStore configStore, Novashot.Core.Library.LibraryStore libraryStore, ILogger<MainWindow> logger)
    {
        _vm = vm;
        _capture = capture;
        _windowService = windowService;
        _configStore = configStore;
        _libraryStore = libraryStore;
        _logger = logger;
        DataContext = vm;

        InitializeComponent();

        Icon = AppIconFactory.CreateWindowIcon();

        _canvas = this.FindControl<SkiaCanvas>("Canvas");
        _textEditBox = this.FindControl<TextBox>("TextEditBox");
        if (_canvas is not null) _canvas.ViewportInvalidated += OnCanvasViewportInvalidated;
        _vm.PropertyChanged += OnVmPropertyChanged;
        _vm.PinnedImageRequested += OnPinnedImageRequested;
        SubscribeToEditor(_vm.Editor);

        // G-16：恢复上次窗口尺寸（配置有效值才覆盖默认 1200×800）
        ApplyPersistedWindowSize();

        // B-6：工具坞按 Editor.VisibleTools 过滤；设置页保存（任何 Update）后重算
        ApplyToolVisibility();
        _configStore.Changed += OnConfigChangedForToolDock;

        // 标题 + 主窗口「最近 4 张」
        ApplyLocalizedTitle();
        _ = _vm.RefreshRecentAsync();

        // G-15：CloseToTray —— ✕ 隐藏到托盘而非退出；G-16：关闭时持久化尺寸
        Closing += OnMainWindowClosing;
    }

    /// <summary>config.Window 的有效值（>0）才覆盖 AXAML 默认尺寸。</summary>
    private void ApplyPersistedWindowSize()
    {
        try
        {
            var window = _configStore.Current.Window;
            if (window.Width >= MinWidth) Width = window.Width;
            if (window.Height >= MinHeight) Height = window.Height;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ApplyPersistedWindowSize failed; keeping AXAML default");
        }
    }

    /// <summary>
    /// B-6 工具坞可见性：按 <c>Editor.VisibleTools</c>（Core <see cref="ToolVisibility"/> 决策表）
    /// 设置各 RadioButton/分隔线 IsVisible。编辑器打开且当前工具被隐藏时回落 Select（默认工具有落点）。
    /// </summary>
    private void ApplyToolVisibility()
    {
        var dock = this.FindControl<StackPanel>("ToolDock");
        if (dock is null) return;

        List<(bool IsSeparator, string? Tag)> children;
        try
        {
            children = dock.Children.Select(c => (c is not RadioButton,
                (c as RadioButton)?.Tag as string)).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ApplyToolVisibility: dock children read failed");
            return;
        }

        bool[] visible;
        try
        {
            visible = ToolVisibility.ComputeDockChildVisibility(
                children, ToolVisibility.Resolve(_configStore.Current.Editor.VisibleTools));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ApplyToolVisibility: resolve failed; keeping current state");
            return;
        }

        for (var i = 0; i < dock.Children.Count && i < visible.Length; i++)
            dock.Children[i].IsVisible = visible[i];

        // 当前工具被隐藏 → 回落 Select（Resolve 保证 Select 恒可见）
        if (_vm.Editor is { } editor
            && dock.Children.OfType<RadioButton>()
                .Where(rb => rb.IsVisible)
                .Select(rb => rb.Tag as string)
                .All(t => t != editor.CurrentTool.ToString()))
        {
            editor.CurrentTool = ToolKind.Select;
        }
    }

    private void OnConfigChangedForToolDock(object? sender, NovashotConfig cfg) => ApplyToolVisibility();


    private void OnMainWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        // G-16：把当前尺寸写回 config（夹到最小值内）；失败不影响关闭流程
        try
        {
            var w = Math.Max((int)MinWidth, (int)Math.Round(Width));
            var h = Math.Max((int)MinHeight, (int)Math.Round(Height));
            _configStore.Update(c =>
            {
                c.Window.Width = w;
                c.Window.Height = h;
            });
            // E-2 跟进：Update 只改内存——尺寸回写此前从未真正落盘（除非碰巧有别处 save），补上防抖写盘
            _vm.ScheduleConfigSave();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Persist window size failed");
        }

        // G-15：CloseToTray=true（默认）时 ✕ 隐藏到托盘；真退出只能走托盘 Quit
        bool closeToTray;
        try { closeToTray = _configStore.Current.Startup.CloseToTray; }
        catch { closeToTray = true; }
        if (closeToTray)
        {
            e.Cancel = true;
            Hide();
        }
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.Editor))
            SubscribeToEditor(_vm.Editor);
    }

    private void SubscribeToEditor(EditorViewModel? editor)
    {
        if (_subscribedEditor is not null)
            _subscribedEditor.PropertyChanged -= OnEditorPropertyChanged;
        _subscribedEditor = editor;
        if (_subscribedEditor is not null)
            _subscribedEditor.PropertyChanged += OnEditorPropertyChanged;
    }

    private void OnEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EditorViewModel.EditingText))
        {
            // 进入编辑：把焦点交给 TextBox，选中全部文字方便替换
            if (_subscribedEditor?.EditingText is not null && _textEditBox is not null)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    _textEditBox.Focus();
                    _textEditBox.SelectAll();
                }, DispatcherPriority.Background);
            }
        }
    }

    private void OnCanvasViewportInvalidated(object? sender, EventArgs e)
    {
        var editor = _subscribedEditor;
        if (editor?.EditingText is not TextAnnotation ta) return;
        if (_canvas is null) return;
        if (!_canvas.TryMapImageRectToDip(ta.BoundingBox, out var dipRect, out var scale)) return;
        var w = Math.Max(dipRect.Width, 60);
        var h = Math.Max(dipRect.Height, 24);
        editor.UpdateEditingTextBounds(
            new Avalonia.Rect(dipRect.X - 2, dipRect.Y - 2, w + 12, h + 4),
            ta.FontSize * scale);
    }

    private void OnTextEditLostFocus(object? sender, RoutedEventArgs e)
    {
        // 有些切换过程 EditingText 已经被程序清成 null（Enter/Escape 命令刚跑）
        var editor = _subscribedEditor;
        if (editor?.EditingText is null) return;
        editor.CommitTextEditCommand.Execute(null);
    }

    private async void OnCaptureFullscreenClick(object? sender, RoutedEventArgs e)
    {
        await CaptureFullscreenAsync();
    }

    private async void OnCaptureRegionClick(object? sender, RoutedEventArgs e)
    {
        await CaptureRegionAsync();
    }

    private void OnCaptureWindowClick(object? sender, RoutedEventArgs e)
    {
        _ = CaptureWindowAsync();
    }

    private void OnMinimizeToTrayClick(object? sender, RoutedEventArgs e) => Hide();

    private void OnEditorBackClick(object? sender, RoutedEventArgs e) => _vm.ReturnToCapture();

    /// <summary>窗口标题跟随语言（Title 是普通 string，不跟 DynamicResource）。</summary>
    internal void ApplyLocalizedTitle() => Title = Loc.T("App.Title");

    /// <summary>点主窗口「最近」里的缩略图 → 重新载入那张图。</summary>
    private void OnRecentShotTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Control { DataContext: LibraryEntryViewModel entry }) return;
        if (string.IsNullOrWhiteSpace(entry.Entry.LocalPath)) return;
        if (_vm.ImportImageFile(entry.Entry.LocalPath, out var failure)) return;
        if (!string.IsNullOrEmpty(failure)) _vm.StatusText = failure;
    }

    // ---- I-1: Ctrl+O 导入图片文件（⋯ 菜单入口；解码/所有权/失败提示全在 VM.ImportImageFile） ----

    private void OnImportClick(object? sender, RoutedEventArgs e) => _ = ImportImageFromPickerAsync();

    /// <summary>
    /// I-1 Ctrl+O / ⋯ 菜单导入：<see cref="TopLevel.StorageProvider"/> 单选文件
    /// （常用图片扩展名过滤 + All files——任意扩展不在此拦截，解不动走 UnsupportedFormat 既有提示）。
    /// 用户取消（空结果）静默返回，无副作用。
    /// </summary>
    internal async Task ImportImageFromPickerAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Loc.T("Editor.ImportDialogTitle"),
            AllowMultiple = false,
            FileTypeFilter = new List<FilePickerFileType>
            {
                new(Loc.T("Editor.ImportTypeImages"))
                {
                    Patterns = Novashot.Core.Utils.ImageFileTypes.Patterns,
                },
                new(Loc.T("Editor.ImportTypeAll")) { Patterns = new[] { "*.*" } },
            },
        });
        ImportPickedFiles(files);
    }

    /// <summary>选择器结果落地：取消/空列表静默返回；非空取第一个本地路径交给 VM（失败走既有 StatusText）。</summary>
    private void ImportPickedFiles(IReadOnlyList<IStorageFile> files)
    {
        if (files.Count == 0) return; // 用户取消：静默返回，无副作用
        var path = files[0].TryGetLocalPath();
        if (string.IsNullOrEmpty(path))
        {
            _logger.LogWarning("Import picked file has no local path; skipped");
            _vm.StatusText = Loc.T("Status.ImportFailed", "");
            return;
        }
        if (_vm.ImportImageFile(path, out var failure)) return;
        if (!string.IsNullOrEmpty(failure)) _vm.StatusText = failure;
    }

    private void OnExportClick(object? sender, RoutedEventArgs e) => _ = ExportCurrentAsync();

    // ---- M7b: cloud upload / library / pin / cloud settings ----

    private void OnLibraryClick(object? sender, RoutedEventArgs e) => ShowLibrary();

    /// <summary>打开 Library 历史窗口（重复调用激活已有窗口）。</summary>
    internal void ShowLibrary()
    {
        if (_libraryWindow is { } existing)
        {
            existing.Activate();
            return;
        }
        _libraryWindow = new LibraryWindow(_libraryStore, Novashot.Core.AppDataPaths.Thumbs,
            AppServices.GetRequiredService<Novashot.Core.Clipboard.IClipboardService>());
        _libraryWindow.Closed += (_, _) => _libraryWindow = null;
        _libraryWindow.Show();
    }

    private LibraryWindow? _libraryWindow;

    private void OnCloudSettingsClick(object? sender, RoutedEventArgs e)
    {
        var dialog = new CloudSettingsWindow(_configStore);
        dialog.Show(this);
    }

    /// <summary>S-1：设置中心入口。</summary>
    private void OnSettingsClick(object? sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow(_configStore);
        dialog.Show(this);
    }

    /// <summary>Pin 事件 → 开贴图窗（位图所有权移交 PinnedWindow）。</summary>
    private void OnPinnedImageRequested(object? sender, PinnedImageRequestedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var pin = new PinnedWindow(e.Bitmap);
            pin.Show();
        });
    }

    /// <summary>
    /// M6a 导出：保存对话框（PNG/JPEG/WebP，格式由所选扩展名决定）→
    /// <see cref="ImageExporter"/>（内部走 <see cref="Composition"/>，与画布渲染同源）→ 落盘。
    /// 建议文件名沿用 FilenamePatterns 的 timestamp 模式。
    /// </summary>
    private async Task ExportCurrentAsync()
    {
        if (_vm.Editor is not { } editor) return;

        var suggested = Novashot.Core.Utils.FilenamePatterns.Generate(
            Novashot.Core.Utils.FilenamePatterns.Timestamp, folder: string.Empty,
            extension: ImageExporter.ExtensionFor(ExportFormat.Png));

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Loc.T("Editor.ExportDialogTitle"),
            SuggestedFileName = suggested,
            FileTypeChoices = new List<Avalonia.Platform.Storage.FilePickerFileType>
            {
                new(Loc.T("Editor.ExportTypePng"))  { Patterns = new[] { "*.png" } },
                new(Loc.T("Editor.ExportTypeJpeg")) { Patterns = new[] { "*.jpg", "*.jpeg" } },
                new(Loc.T("Editor.ExportTypeWebp")) { Patterns = new[] { "*.webp" } },
            },
        });
        if (file is null) return;

        var path = file.TryGetLocalPath();
        if (string.IsNullOrEmpty(path))
        {
            _vm.StatusText = Loc.T("Status.ExportFailedNoPath");
            return;
        }

        try
        {
            var format = ImageExporter.FormatFromExtension(path);
            ImageExporter.Save(editor.Document, path, format, state: editor.CompositionState);
            _logger.LogInformation("Exported {Path} ({Format}, {Width}x{Height})",
                path, format, editor.ContentWidth, editor.ContentHeight);
            _vm.StatusText = Loc.T("Status.Exported", path);
            _vm.NotifyExported(path); // N-1：手动导出成功即通知（QuickSave 路径的通知在 VM 内）

            await RecordLibraryAfterExportAsync(path, editor);

            // M6c: Auto-copy —— 导出成功后按 config 自动复制到剪贴板
            // N-1 review I-1：管线内自动复制不弹复制 toast（CopySilently），导出 toast 即本次操作唯一反馈
            if (IsAutoCopyEnabled())
            {
                _vm.CopySilently();
                _vm.StatusText = Loc.T("Status.ExportedCopied", path);
            }

            // M7b: Auto-upload —— 导出成功后按 config 自动上传
            if (_vm.IsAutoUploadEnabled)
                await _vm.UploadCurrentAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Export to {Path} failed", path);
            _vm.StatusText = Loc.T("Status.ExportFailed", ex.Message);
        }
    }

    /// <summary>config.Export.AutoCopyToClipboard，默认 true；无 config store 时保持旧行为（不复制）。</summary>
    private bool IsAutoCopyEnabled()
    {
        try
        {
            return AppServices.GetRequiredService<IConfigStore>().Current.Export.AutoCopyToClipboard;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read AutoCopyToClipboard config; skipping auto-copy");
            return false;
        }
    }

    /// <summary>
    /// M7b：导出成功后记入 Library 历史（LocalPath = 保存位置；上传信息留空）。
    /// 记录失败只警告，不影响导出流程。
    /// </summary>
    private async Task RecordLibraryAfterExportAsync(string path, EditorViewModel editor)
    {
        try
        {
            var info = new FileInfo(path);
            var size = Novashot.Core.Editor.Composition.Measure(editor.Document, editor.CompositionState);
            var entry = await _libraryStore.AddAsync(new Novashot.Core.Library.LibraryEntry
            {
                CreatedAt = DateTimeOffset.Now,
                FileName = Path.GetFileName(path),
                LocalPath = path,
                Width = size.Width,
                Height = size.Height,
                SizeBytes = info.Length,
            });

            // 缩略图失败不影响导出流程
            try
            {
                using var bitmap = SkiaSharp.SKBitmap.Decode(path);
                if (bitmap is not null)
                    Novashot.Core.Library.ThumbnailWriter.Write(
                        Novashot.Core.AppDataPaths.Thumbs, $"thumb_{entry.Id}.png", bitmap);
            }
            catch (Exception tex)
            {
                _logger.LogWarning(tex, "Thumbnail write failed for {Path}", path);
            }

            // 主窗口「最近 N 张」立即刷新（回到空闲态就能看见）
            await _vm.RefreshRecentAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Library record failed for {Path}", path);
        }
    }

    /// <summary>
    /// 工具栏的 RadioButton Click 事件：Tag 是 <see cref="ToolKind"/> 的字符串名，解析后设 <c>Editor.CurrentTool</c>。
    /// </summary>
    private void OnToolChanged(object? sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton rb || rb.IsChecked != true) return;
        if (rb.Tag is not string tag) return;
        if (!Enum.TryParse<ToolKind>(tag, out var kind)) return;
        if (_vm.Editor is { } editor) editor.CurrentTool = kind;
    }

    /// <summary>
    /// M4d 工具热键：<b>V R E O A L T N S + M5 的 P H M B</b>（无修饰键）。
    /// 编辑 Text 时或 TextBox 拿焦时禁用，避免抢用户输入。
    /// 走 <see cref="OnKeyDown"/> 覆盖而非 <c>Window.KeyBindings</c> —— 后者不看焦点，字母键会被吞掉。
    /// M6a：Ctrl+S 触发导出（编辑文本中不导出——导出会跳过编辑中的 Text，先提交再存）。
    /// I-1：Ctrl+O 导入图片文件——空闲/编辑器两态都可用（空闲态是「无图进编辑器」的主路径，
    /// 对齐 winshot），故分发在 editor guard 之前；编辑文本中不接管（同款防抢输入）。
    /// </summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled) return;
        // I-1：Ctrl+O 导入图片文件（两态；编辑文本中不接管）
        var textInputActive = _vm.Editor?.EditingText is not null || _textEditBox is { IsFocused: true };
        if (e.Key == Key.O && e.KeyModifiers == KeyModifiers.Control && !textInputActive)
        {
            e.Handled = true;
            _ = ImportImageFromPickerAsync();
            return;
        }
        if (_vm.Editor is not { } editor) return;
        if (editor.EditingText is not null) return;
        if (_textEditBox is { IsFocused: true }) return;
        if (e.Key == Key.S && e.KeyModifiers == KeyModifiers.Control)
        {
            e.Handled = true;
            _ = ExportCurrentAsync();
            return;
        }
        // M6c: Ctrl+C 复制 / Ctrl+Q 快存（编辑器内；粘贴在捕获态由 TextBox/系统处理）
        if (e.Key == Key.C && e.KeyModifiers == KeyModifiers.Control)
        {
            e.Handled = true;
            _vm.CopyCommand.Execute(null);
            return;
        }
        if (e.Key == Key.Q && e.KeyModifiers == KeyModifiers.Control)
        {
            e.Handled = true;
            _vm.QuickSaveCommand.Execute(null);
            return;
        }
        if (e.Key == Key.V && e.KeyModifiers == KeyModifiers.Control)
        {
            e.Handled = true;
            _vm.PasteFromClipboardCommand.Execute(null);
            return;
        }
        // M7b: Ctrl+U 上传 / Ctrl+L 历史 / Ctrl+P Pin
        if (e.Key == Key.U && e.KeyModifiers == KeyModifiers.Control)
        {
            e.Handled = true;
            _ = _vm.UploadCurrentAsync();
            return;
        }
        if (e.Key == Key.L && e.KeyModifiers == KeyModifiers.Control)
        {
            e.Handled = true;
            ShowLibrary();
            return;
        }
        if (e.Key == Key.P && e.KeyModifiers == KeyModifiers.Control)
        {
            e.Handled = true;
            _vm.PinCommand.Execute(null);
            return;
        }
        // 视口缩放键：+/−（多数布局需 Shift，放行任意修饰，Ctrl+± 顺带兼容浏览器习惯）、
        // 0 = 回 fit、1 = 100% 物理像素。滚轮缩放在 SkiaCanvas 内部处理。
        // 空格 = 临时平移模式（按住 + 左键）。画布有焦点时画布自己吃掉（Handled 到不了这）；
        // 焦点在窗口/其他控件时在此转发，滚轮缩放后直接空格拖拽也能用。
        // 编辑文本的 guard 在上面已返回；工具 chip 聚焦时 Avalonia 按钮自吃空格（键盘激活语义优先）。
        if (_canvas is not null && e.Key == Key.Space)
        {
            e.Handled = true;
            _canvas.SetSpacePan(true);
            return;
        }
        if (_canvas is not null && e.Key is Key.OemPlus or Key.Add or Key.OemMinus or Key.Subtract)
        {
            e.Handled = true;
            if (e.Key is Key.OemPlus or Key.Add) _canvas.ZoomInCenter();
            else _canvas.ZoomOutCenter();
            return;
        }
        if (_canvas is not null && e.KeyModifiers == KeyModifiers.None)
        {
            if (e.Key == Key.D0)
            {
                e.Handled = true;
                _canvas.ZoomToFit();
                return;
            }
            if (e.Key == Key.D1)
            {
                e.Handled = true;
                _canvas.ZoomToPhysicalPixels();
                return;
            }
        }
        if (e.KeyModifiers != KeyModifiers.None) return;
        var kind = MapKeyToTool(e.Key);
        if (kind is null) return;
        editor.CurrentTool = kind.Value;
        e.Handled = true;
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        // 与 OnKeyDown 的空格转发配对；画布侧 OnKeyUp/OnLostFocus 也会清，三处幂等
        if (e.Key == Key.Space) _canvas?.SetSpacePan(false);
    }

    private static ToolKind? MapKeyToTool(Key key) => key switch
    {
        Key.V => ToolKind.Select,
        Key.R => ToolKind.Rectangle,
        Key.E => ToolKind.Ellipse,
        Key.O => ToolKind.ArrowCurved,
        Key.A => ToolKind.ArrowStraight,
        Key.L => ToolKind.Line,
        Key.T => ToolKind.Text,
        Key.N => ToolKind.Number,
        Key.S => ToolKind.Spotlight,
        // M5：Flameshot 补齐工具
        Key.P => ToolKind.Pencil,
        Key.H => ToolKind.Highlighter,
        Key.M => ToolKind.Mosaic,
        Key.B => ToolKind.Blur,
        Key.I => ToolKind.Invert,
        // M6d：非破坏裁剪
        Key.X => ToolKind.Crop,
        _ => null,
    };

    /// <summary>
    /// Public API：托盘/热键触发全屏抓取。主窗口先隐藏，等 200ms 让 shadow 消失，再抓帧。
    /// </summary>
    public async Task CaptureFullscreenAsync()
    {
        _logger.LogInformation("Capture Fullscreen requested");
        var wasVisible = IsVisible;
        if (wasVisible)
        {
            Hide();
            await Task.Delay(200);
        }
        try
        {
            var img = _capture.CaptureFullscreen();
            _vm.EnterEditor(img);
            Show();
            Activate();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fullscreen capture failed");
            _vm.StatusText = Loc.T("Status.CaptureFailed", ex.Message);
            if (wasVisible) Show();
        }
    }

    /// <summary>
    /// Region 捕获：每个物理显示器一个 <see cref="OverlayWindow"/>；
    /// 先抓帧再显示 overlay，避免 overlay 自身被拍进画面。
    /// 首个 Confirmed/Cancelled 决定整会话结果，其余 overlay 一并关闭。
    /// M2-3a：每屏一个 overlay；B-3（M2-3b）：共享 <see cref="SelectionSession"/> 权威选框，
    /// 选框可跨屏拖拽，确认的物理 region 由 CaptureRegion 跨屏拼接。
    /// </summary>
    public async Task CaptureRegionAsync()
    {
        _logger.LogInformation("Capture Region requested");
        var wasVisible = IsVisible;
        if (wasVisible)
        {
            Hide();
            await Task.Delay(200);
        }

        var overlays = new List<OverlayWindow>();
        var displayShots = new List<CapturedImage>();

        // E-1 M9 review I-2：抓帧位图的释放再推迟一跳（Background 优先级）——overlay 关闭时
        // compositor 可能还有引用放大镜/背景位图的在途帧，本 hop 先让渲染管线回收完，
        // 下一 hop 才 Dispose，堵住「在途帧 × 提前 Dispose」的 SIGSEGV 窗口（毫秒级延迟换确定性）。
        void DisposeShotsAfterRenderSettles()
        {
            Dispatcher.UIThread.Post(
                () => { foreach (var s in displayShots) s.Dispose(); },
                DispatcherPriority.Background);
        }

        try
        {
            var displays = _capture.EnumerateDisplays();
            _logger.LogInformation("Opening region overlays across {N} display(s)", displays.Count);

            // 先抓全部显示器（overlay 尚未显示，不会被拍到）
            foreach (var d in displays)
                displayShots.Add(_capture.CaptureDisplay(d.Index));

            var completed = false;

            void CloseAll()
            {
                foreach (var o in overlays)
                {
                    try { o.Close(); } catch { /* already closed */ }
                }
            }

            void OnAnyConfirmed(PixelRect region)
            {
                if (completed) return;
                completed = true;
                Dispatcher.UIThread.Post(() =>
                {
                    CloseAll();
                    try
                    {
                        var img = _capture.CaptureRegion(region);
                        _vm.EnterEditor(img);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Region crop failed");
                        _vm.StatusText = Loc.T("Status.RegionFailed", ex.Message);
                    }
                    finally
                    {
                        DisposeShotsAfterRenderSettles(); // 显示器抓帧位图释放（延迟一跳）
                    }
                    Show();
                    Activate();
                });
            }

            void OnAnyCancelled()
            {
                if (completed) return;
                completed = true;
                Dispatcher.UIThread.Post(() =>
                {
                    CloseAll();
                    DisposeShotsAfterRenderSettles();
                    _vm.PendingStrokeColor = null; // E-1 M9：取色后取消捕获，挂起色不泄漏进下一次会话
                    if (wasVisible) { Show(); Activate(); }
                });
            }

            // E-1 M9：取色模式服务（剪贴板 + 会话历史）；取色点击挂起为编辑器描边色
            IClipboardService clipboard;
            try { clipboard = AppServices.GetRequiredService<IClipboardService>(); }
            catch { clipboard = new FakeClipboardService(); }
            var colorHistory = AppServices.Services.GetService<Novashot.Core.Editor.ColorHistory>();

            // B-3：一次捕获会话一个跨屏选框协调器（按枚举快照构造，逐 overlay 注入）
            var selectionSession = new SelectionSession(displays);

            for (int i = 0; i < displays.Count; i++)
            {
                var display = displays[i];
                var shot = displayShots[i];
                var overlay = new OverlayWindow(shot.Bitmap, display, clipboard, colorHistory, selectionSession);
                overlay.Confirmed += (_, region) => OnAnyConfirmed(region);
                overlay.Cancelled += (_, _) => OnAnyCancelled();
                overlay.StrokePicked += (_, color) => _vm.PendingStrokeColor = color;
                overlays.Add(overlay);
            }

            foreach (var o in overlays) o.Show();
            // 焦点给主屏那个（默认第一个 primary），保证 Enter/Esc 有地方接
            var primaryIdx = displays.ToList().FindIndex(d => d.IsPrimary);
            if (primaryIdx < 0) primaryIdx = 0;
            overlays[primaryIdx].Activate();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Region capture setup failed");
            foreach (var o in overlays) { try { o.Close(); } catch { } }
            DisposeShotsAfterRenderSettles(); // 同 I-2：部分 overlay 可能已显示，位图释放同样延一跳
            if (wasVisible) Show();
        }
    }

    /// <summary>
    /// D-1/G-26：截取指定矩形（Capture Launcher 预置区域的路径）。
    /// 物理像素、虚拟桌面坐标；调用方（App）已做钳界。主窗口隐藏 → 等残影消失 → 抓帧 → 编辑器。
    /// </summary>
    public async Task CapturePresetRegionAsync(PixelRect region)
    {
        _logger.LogInformation("Capture preset region {Rect} requested", region);
        if (region.IsEmpty) return;

        var wasVisible = IsVisible;
        if (wasVisible)
        {
            Hide();
            await Task.Delay(200);
        }
        try
        {
            var img = _capture.CaptureRegion(region);
            _vm.EnterEditor(img);
            Show();
            Activate();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Preset region capture failed for {Rect}", region);
            _vm.StatusText = Loc.T("Status.RegionFailed", ex.Message);
            if (wasVisible) Show();
        }
    }

    /// <summary>D-1：跨窗口状态提示（App 层在倒计时被取消时调用）。</summary>
    public void ShowStatus(string text) => _vm.StatusText = text;

    /// <summary>
    /// Window 捕获：弹出 <see cref="WindowPickerDialog"/> 让用户挑一个顶层窗口，
    /// 主窗口隐藏 → 等 250ms 让候选窗口浮到前台 → <see cref="ICaptureService.CaptureWindow"/> →
    /// 进入编辑器。用户取消 picker 则复原。
    /// </summary>
    public async Task CaptureWindowAsync()
    {
        _logger.LogInformation("Capture Window requested");

        var picker = new WindowPickerDialog(_windowService, _capture);
        WindowInfo? target = null;
        if (IsVisible)
        {
            await picker.ShowDialog(this);
            target = picker.Selected;
        }
        else
        {
            // 从托盘/热键触发，无 owner，直接 Show + 等关闭
            var tcs = new TaskCompletionSource();
            picker.Closed += (_, _) => tcs.TrySetResult();
            picker.Show();
            picker.Activate();
            await tcs.Task;
            target = picker.Selected;
        }

        if (target is null)
        {
            _logger.LogDebug("Window picker cancelled");
            return;
        }

        var wasVisible = IsVisible;
        if (wasVisible)
        {
            Hide();
        }

        try
        {
            // 让目标窗口浮到前台并稍等其重绘（尤其 UWP/DirectComposition 需要 tick 一下 DWM）
            _windowService.TryBringToForeground(target.Handle);
            await Task.Delay(250);

            var img = _capture.CaptureWindow(target.Handle);
            _vm.EnterEditor(img);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Window capture failed");
            _vm.StatusText = Loc.T("Status.WindowFailed", ex.Message);
        }
        finally
        {
            if (wasVisible) { Show(); Activate(); }
        }
    }
}
