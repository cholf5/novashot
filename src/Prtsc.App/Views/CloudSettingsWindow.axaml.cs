using Avalonia.Controls;
using Avalonia.Interactivity;
using Prtsc.App.ViewModels;
using Prtsc.Core.Config;

namespace Prtsc.App.Views;

/// <summary>云上传设置窗口（M7b）：渠道下拉 + 凭据表单 + AutoUpload 开关。</summary>
public partial class CloudSettingsWindow : Window
{
    private readonly CloudSettingsViewModel _vm;

    public CloudSettingsWindow() : this(AppServices.GetRequiredService<IConfigStore>()) { }

    public CloudSettingsWindow(IConfigStore store)
    {
        _vm = new CloudSettingsViewModel(store);
        DataContext = _vm;
        InitializeComponent();
    }

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        if (_vm.Save())
            Close();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close();
}
