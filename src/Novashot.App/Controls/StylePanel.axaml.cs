using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Novashot.App.ViewModels;
using SkiaSharp;

namespace Novashot.App.Controls;

public partial class StylePanel : UserControl
{
    public StylePanel()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => RefreshSwatchStates();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        RefreshSwatchStates();
        if (DataContext is StylePanelViewModel vm)
            vm.PropertyChanged += (_, _) => RefreshSwatchStates();
    }

    private void OnSwatchClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton tb) return;
        if (tb.Tag is not SKColor color) return;
        if (DataContext is not StylePanelViewModel vm) return;
        vm.Stroke = color;
        RefreshSwatchStates();
    }

    /// <summary>把当前选中颜色反映到 ToggleButton.IsChecked 上（视觉高亮）。预设行与历史行都刷。</summary>
    private void RefreshSwatchStates()
    {
        if (DataContext is not StylePanelViewModel vm) return;
        // Avalonia ItemsControl 的容器：遍历 LogicalChildren 拿到每个 ContentPresenter → 子 ToggleButton
        foreach (var name in new[] { "ColorSwatches", "HistorySwatches" })
        {
            var items = this.FindControl<ItemsControl>(name);
            if (items is null) continue;
            foreach (var container in items.GetLogicalChildren())
            {
                if (container is not Control ctrl) continue;
                var toggle = ctrl.GetVisualDescendants()
                                 .OfType<ToggleButton>()
                                 .FirstOrDefault();
                if (toggle is null) continue;
                if (toggle.Tag is SKColor color)
                    toggle.IsChecked = color == vm.Stroke;
            }
        }
    }
}
