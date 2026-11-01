using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Prtsc.App.ViewModels;
using SkiaSharp;

namespace Prtsc.App.Controls;

/// <summary>
/// 合成外观面板（M6b）：色板/渐变格子的 Click 走 code-behind（Tag 承载值），
/// 滑杆与开关直接绑定 <see cref="CompositionPanelViewModel"/>。
/// </summary>
public partial class CompositionPanel : UserControl
{
    public CompositionPanel()
    {
        InitializeComponent();
    }

    private CompositionPanelViewModel? Vm => DataContext as CompositionPanelViewModel;

    private void OnSolidColorClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is { } vm && sender is ToggleButton { Tag: SKColor color })
            vm.SetSolidColor(color);
    }

    private void OnGradientClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is { } vm && sender is ToggleButton { Tag: string css })
            vm.SetGradient(css);
    }

    private void OnBorderColorClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is { } vm && sender is ToggleButton { Tag: SKColor color })
        {
            if (!vm.BorderOn) vm.BorderOn = true;
            vm.SetBorderColor(color);
        }
    }

    private void OnInsetColorClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is { } vm && sender is ToggleButton { Tag: SKColor color })
        {
            if (!vm.InsetOn) vm.InsetOn = true;
            vm.SetInsetColor(color);
        }
    }
}
