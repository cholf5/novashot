using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Novashot.App.ViewModels;
using SkiaSharp;

namespace Novashot.App.Controls;

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

    /// <summary>
    /// 背景三态 chip（Q-1）：输入走 Click（Tag 承载状态），IsChecked 只做 OneWay 回显。
    /// 曾经是 GroupName + IsChecked TwoWay——组管理器取消兄弟时会「回写 VM → VM 整表通知
    /// → 绑定重新发布 → IsChecked 再变」成环，加压即栈溢出崩进程（测试宿主 abort）。
    /// 单向绑定把环从结构上消掉：目标侧永远是纯消费者，状态只由 <see cref="CompositionPanelViewModel.Apply"/> 决定。
    /// </summary>
    private void OnBgKindClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm || sender is not RadioButton { Tag: string kind }) return;
        switch (kind)
        {
            case "None": vm.IsBgNone = true; break;
            case "Solid": vm.IsBgSolid = true; break;
            case "Gradient": vm.IsBgGradient = true; break;
        }
    }

    private void OnAutoEdgeColorClick(object? sender, RoutedEventArgs e)
    {
        Vm?.SetAutoEdgeColor();
    }

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
