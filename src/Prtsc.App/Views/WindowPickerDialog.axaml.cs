using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Prtsc.Core.Windows;

namespace Prtsc.App.Views;

/// <summary>
/// 选择要截取的顶层窗口。ShowDialog 返回选中的 <see cref="WindowInfo"/> 或 null（取消）。
/// </summary>
public partial class WindowPickerDialog : Window
{
    private readonly IWindowService _windowService;
    private readonly ObservableCollection<Row> _rows = new();

    public WindowInfo? Selected { get; private set; }

    public WindowPickerDialog() : this(AppServices.GetRequiredService<IWindowService>()) { }

    public WindowPickerDialog(IWindowService windowService)
    {
        _windowService = windowService;
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

        Reload();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void Reload()
    {
        _rows.Clear();
        var windows = _windowService.EnumerateWindows();
        foreach (var w in windows)
        {
            _rows.Add(new Row(w));
        }

        var list = this.FindControl<ListBox>("WindowList")!;
        if (_rows.Count > 0) list.SelectedIndex = 0;
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
    public sealed class Row
    {
        public WindowInfo Info { get; }
        public string Title => string.IsNullOrEmpty(Info.Title) ? "(untitled)" : Info.Title;
        public string SubText =>
            (string.IsNullOrEmpty(Info.ProcessName) ? "?" : Info.ProcessName)
            + $"   {Info.Bounds.Width}×{Info.Bounds.Height}"
            + (Info.IsMinimized ? "   [最小化]" : string.Empty);

        public Row(WindowInfo info) { Info = info; }
    }
}
