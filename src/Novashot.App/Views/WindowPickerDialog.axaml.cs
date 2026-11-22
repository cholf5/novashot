using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Novashot.Core.Capture;
using Novashot.Core.Windows;
using SkiaSharp;

namespace Novashot.App.Views;

/// <summary>
/// 选择要截取的顶层窗口。ShowDialog 返回选中的 <see cref="WindowInfo"/> 或 null（取消）。
/// B-2：列表每项带 96×54 实时缩略图（后台逐窗捕获，失败/最小化降级为占位首字母，绝不因此崩）。
/// </summary>
public partial class WindowPickerDialog : Window
{
    private readonly IWindowService _windowService;
    private readonly ICaptureService _captureService;
    private readonly ObservableCollection<Row> _rows = new();

    /// <summary>缩略图加载代次：Refresh/关窗后旧结果按代次丢弃。</summary>
    private int _thumbGeneration;

    /// <summary>最近一轮加载任务（测试轮询用；产品代码 fire-and-forget）。</summary>
    internal Task? CurrentLoadTask { get; private set; }

    /// <summary>测试观测用行快照。</summary>
    internal IReadOnlyList<Row> Rows => _rows;

    public WindowInfo? Selected { get; private set; }

    public WindowPickerDialog()
        : this(AppServices.GetRequiredService<IWindowService>(),
               AppServices.GetRequiredService<ICaptureService>()) { }

    public WindowPickerDialog(IWindowService windowService, ICaptureService captureService)
    {
        _windowService = windowService;
        _captureService = captureService;
        InitializeComponent();

        var list = this.FindControl<ListBox>("WindowList")!;
        list.ItemsSource = _rows;

        this.FindControl<Button>("RefreshButton")!.Click += (_, _) => Reload();
        this.FindControl<Button>("CancelButton")!.Click += (_, _) => { Selected = null; Close(); };
        this.FindControl<Button>("OkButton")!.Click += (_, _) => ConfirmSelection();

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Selected = null; Close(); }
        };
        Closed += (_, _) => _thumbGeneration++; // 在途结果作废，位图不再回写

        Reload();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void Reload()
    {
        _thumbGeneration++;
        foreach (var old in _rows) old.DisposeThumbnail();

        _rows.Clear();
        var windows = _windowService.EnumerateWindows();
        foreach (var w in windows)
        {
            _rows.Add(new Row(w));
        }

        var list = this.FindControl<ListBox>("WindowList")!;
        if (_rows.Count > 0) list.SelectedIndex = 0;

        StartThumbnailLoad(_thumbGeneration);
    }

    /// <summary>后台逐窗捕获（串行，避免 N 路并发抓屏）；结果经 Post 回 UI 线程，
    /// 位图所有权随 Post 闭包移交（代次已过期时由闭包负责 Dispose，绝不回写旧行）。
    /// rowsSnapshot 由调用方在 UI 线程取好——后台枚举期间 Reload 可能清集合。</summary>
    internal async Task LoadThumbnailsAsync(int generation, IReadOnlyList<Row> rowsSnapshot, CancellationToken ct)
    {
        foreach (var row in rowsSnapshot)
        {
            ct.ThrowIfCancellationRequested();
            if (Volatile.Read(ref _thumbGeneration) != generation) return;

            var hwnd = row.Info.Handle;
            var captured = await Task.Run(() =>
            {
                try { return _captureService.CaptureWindow(hwnd); }
                catch { return null; } // 最小化/已关闭/无权限：降级占位
            }, ct).ConfigureAwait(false);

            if (Volatile.Read(ref _thumbGeneration) != generation)
            {
                captured?.Dispose();
                return;
            }

            var thumb = captured is null ? null : PickerThumbnailsFactory.TryCreate(captured);
            captured?.Dispose();

            Dispatcher.UIThread.Post(() =>
            {
                if (Volatile.Read(ref _thumbGeneration) != generation)
                {
                    thumb?.Dispose();
                    return;
                }
                row.SetThumbnail(thumb);
            });
        }
    }

    /// <summary>测试入口：模拟用户点 Refresh（公开 Reload 走同一路径）。</summary>
    internal void SimulateRefreshForTest() => Reload();

    private void StartThumbnailLoad(int generation)
    {
        CurrentLoadTask = LoadThumbnailsAsync(generation, _rows.ToArray(), CancellationToken.None);
    }

    private void OnListDoubleTapped(object? sender, TappedEventArgs e) => ConfirmSelection();

    private void ConfirmSelection()
    {
        var list = this.FindControl<ListBox>("WindowList")!;
        if (list.SelectedItem is Row r)
        {
            Selected = r.Info;
            Close();
        }
    }

    /// <summary>行 DTO：把 WindowInfo 平铺给 DataTemplate 用。公开是为了 Avalonia XAML 编译期绑定。</summary>
    public sealed class Row : INotifyPropertyChanged
    {
        public WindowInfo Info { get; }
        public string Title => string.IsNullOrEmpty(Info.Title) ? Novashot.App.Localization.Loc.T("Picker.Untitled") : Info.Title;
        public string SubText =>
            (string.IsNullOrEmpty(Info.ProcessName) ? "?" : Info.ProcessName)
            + $"   {Info.Bounds.Width}×{Info.Bounds.Height}"
            + (Info.IsMinimized ? "   " + Novashot.App.Localization.Loc.T("Picker.Minimized") : string.Empty);

        /// <summary>B-2 占位首字母：进程名首个字母数字字符（无则 "?"）。</summary>
        public string Initial
        {
            get
            {
                var ch = (Info.ProcessName ?? string.Empty)
                    .FirstOrDefault(char.IsLetterOrDigit);
                return ch == default ? "?" : char.ToUpperInvariant(ch).ToString();
            }
        }

        private Bitmap? _thumbnail;
        public Bitmap? Thumbnail
        {
            get => _thumbnail;
            private set { _thumbnail = value; OnPropertyChanged(nameof(Thumbnail)); OnPropertyChanged(nameof(HasThumbnail)); }
        }

        public bool HasThumbnail => _thumbnail is not null;

        public Row(WindowInfo info) { Info = info; }

        internal void SetThumbnail(Bitmap? thumbnail)
        {
            _thumbnail?.Dispose();
            Thumbnail = thumbnail;
        }

        /// <summary>Reload 清行时释放已持有位图（DisplayItem 生命周期归属 Row）。</summary>
        internal void DisposeThumbnail()
        {
            _thumbnail?.Dispose();
            _thumbnail = null;
        }

        private void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}

/// <summary>
/// CapturedImage → Avalonia Bitmap（96×54 等比，PNG 字节流中转）。
/// 任何失败返 null —— 缩略图是装饰，不因个别窗口拖垮列表。
/// </summary>
internal static class PickerThumbnailsFactory
{
    public static Bitmap? TryCreate(CapturedImage captured)
    {
        try
        {
            using var resized = Novashot.Core.Windows.PickerThumbnails.Resize(
                captured.Bitmap,
                Novashot.Core.Windows.PickerThumbnails.ThumbWidth,
                Novashot.Core.Windows.PickerThumbnails.ThumbHeight);
            using var image = SKImage.FromBitmap(resized);
            using var data = image.Encode(SKEncodedImageFormat.Png, 90);
            using var stream = new MemoryStream();
            data.SaveTo(stream);
            stream.Position = 0;
            return new Bitmap(stream);
        }
        catch
        {
            return null;
        }
    }
}
