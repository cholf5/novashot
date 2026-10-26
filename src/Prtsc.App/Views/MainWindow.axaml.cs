using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using Prtsc.App.Assets;
using Prtsc.App.Controls;
using Prtsc.App.ViewModels;
using Prtsc.Core.Annotation.Model;
using Prtsc.Core.Annotation.Tools;
using Prtsc.Core.Capture;
using Prtsc.Core.Common;
using Prtsc.Core.Editor.Export;
using Prtsc.Core.Windows;

namespace Prtsc.App.Views;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _vm;
    private readonly ICaptureService _capture;
    private readonly IWindowService _windowService;
    private readonly ILogger<MainWindow> _logger;

    private SkiaCanvas? _canvas;
    private TextBox? _textEditBox;
    private EditorViewModel? _subscribedEditor;

    public MainWindow() : this(
        AppServices.GetRequiredService<MainWindowViewModel>(),
        AppServices.GetRequiredService<ICaptureService>(),
        AppServices.GetRequiredService<IWindowService>(),
        AppServices.GetRequiredService<ILogger<MainWindow>>())
    { }

    public MainWindow(MainWindowViewModel vm, ICaptureService capture, IWindowService windowService, ILogger<MainWindow> logger)
    {
        _vm = vm;
        _capture = capture;
        _windowService = windowService;
        _logger = logger;
        DataContext = vm;

        InitializeComponent();

        Icon = AppIconFactory.CreatePlaceholderWindowIcon();

        _canvas = this.FindControl<SkiaCanvas>("Canvas");
        _textEditBox = this.FindControl<TextBox>("TextEditBox");
        if (_canvas is not null) _canvas.ViewportInvalidated += OnCanvasViewportInvalidated;
        _vm.PropertyChanged += OnVmPropertyChanged;
        SubscribeToEditor(_vm.Editor);
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

    private void OnExportClick(object? sender, RoutedEventArgs e) => _ = ExportCurrentAsync();

    /// <summary>
    /// M6a 导出：保存对话框（PNG/JPEG/WebP，格式由所选扩展名决定）→
    /// <see cref="ImageExporter"/>（内部走 <see cref="Composition"/>，与画布渲染同源）→ 落盘。
    /// 建议文件名沿用 FilenamePatterns 的 timestamp 模式。
    /// </summary>
    private async Task ExportCurrentAsync()
    {
        if (_vm.Editor is not { } editor) return;

        var suggested = Prtsc.Core.Utils.FilenamePatterns.Generate(
            Prtsc.Core.Utils.FilenamePatterns.Timestamp, folder: string.Empty,
            extension: ImageExporter.ExtensionFor(ExportFormat.Png));

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export image",
            SuggestedFileName = suggested,
            FileTypeChoices = new List<Avalonia.Platform.Storage.FilePickerFileType>
            {
                new("PNG image")  { Patterns = new[] { "*.png" } },
                new("JPEG image") { Patterns = new[] { "*.jpg", "*.jpeg" } },
                new("WebP image") { Patterns = new[] { "*.webp" } },
            },
        });
        if (file is null) return;

        var path = file.TryGetLocalPath();
        if (string.IsNullOrEmpty(path))
        {
            _vm.StatusText = "Export failed: no local file path.";
            return;
        }

        try
        {
            var format = ImageExporter.FormatFromExtension(path);
            ImageExporter.Save(editor.Document, path, format);
            _logger.LogInformation("Exported {Path} ({Format}, {Width}x{Height})",
                path, format, editor.ImageWidth, editor.ImageHeight);
            _vm.StatusText = $"Saved {path}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Export to {Path} failed", path);
            _vm.StatusText = "Export failed: " + ex.Message;
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
    /// </summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled) return;
        if (_vm.Editor is not { } editor) return;
        if (editor.EditingText is not null) return;
        if (_textEditBox is { IsFocused: true }) return;
        if (e.Key == Key.S && e.KeyModifiers == KeyModifiers.Control)
        {
            e.Handled = true;
            _ = ExportCurrentAsync();
            return;
        }
        if (e.KeyModifiers != KeyModifiers.None) return;
        var kind = MapKeyToTool(e.Key);
        if (kind is null) return;
        editor.CurrentTool = kind.Value;
        e.Handled = true;
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
            _vm.StatusText = "Capture failed: " + ex.Message;
            if (wasVisible) Show();
        }
    }

    /// <summary>
    /// Region 捕获：每个物理显示器一个 <see cref="OverlayWindow"/>；
    /// 先抓帧再显示 overlay，避免 overlay 自身被拍进画面。
    /// 首个 Confirmed/Cancelled 决定整会话结果，其余 overlay 一并关闭。
    /// M2-3a：每个 overlay 只处理本屏内的选框；跨屏选择留待 M2-3b（共享坐标器）。
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
                        _vm.StatusText = "Region capture failed: " + ex.Message;
                    }
                    finally
                    {
                        // 显示器抓帧位图释放
                        foreach (var s in displayShots) s.Dispose();
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
                    foreach (var s in displayShots) s.Dispose();
                    if (wasVisible) { Show(); Activate(); }
                });
            }

            for (int i = 0; i < displays.Count; i++)
            {
                var display = displays[i];
                var shot = displayShots[i];
                var overlay = new OverlayWindow(shot.Bitmap, display);
                overlay.Confirmed += (_, region) => OnAnyConfirmed(region);
                overlay.Cancelled += (_, _) => OnAnyCancelled();
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
            foreach (var s in displayShots) s.Dispose();
            if (wasVisible) Show();
        }
    }

    /// <summary>
    /// Window 捕获：弹出 <see cref="WindowPickerDialog"/> 让用户挑一个顶层窗口，
    /// 主窗口隐藏 → 等 250ms 让候选窗口浮到前台 → <see cref="ICaptureService.CaptureWindow"/> →
    /// 进入编辑器。用户取消 picker 则复原。
    /// </summary>
    public async Task CaptureWindowAsync()
    {
        _logger.LogInformation("Capture Window requested");

        var picker = new WindowPickerDialog(_windowService);
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
            _vm.StatusText = "Window capture failed: " + ex.Message;
        }
        finally
        {
            if (wasVisible) { Show(); Activate(); }
        }
    }
}
