using Avalonia.Controls;
using Avalonia.Interactivity;
using Novashot.App.ViewModels;
using Novashot.Core.Config;

namespace Novashot.App.Views;

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

    /// <summary>
    /// G-25（winshot）：用当前表单凭据做连接测试。R2 = HeadBucket，GDrive = 换 token。
    /// 结果写 SaveHint；测试不落盘（保存仍需点“保存”）。
    /// </summary>
    private async void OnTestConnectionClick(object? sender, RoutedEventArgs e)
    {
        TestConnectionButton.IsEnabled = false;
        _vm.SaveHint = Novashot.App.Localization.Loc.T("Cloud.Testing");
        try
        {
            var (ok, message) = _vm.ProviderIndex switch
            {
                1 => await Novashot.Core.Cloud.CloudConnectionTester.TestR2Async(_vm.ToR2Config()),
                2 => await Novashot.Core.Cloud.CloudConnectionTester.TestGDriveAsync(_vm.ToGDriveConfig()),
                _ => (false, Novashot.App.Localization.Loc.T("Cloud.PickProviderFirst")),
            };
            _vm.SaveHint = (ok ? "✓ " : "✗ ") + message;
        }
        catch (Exception ex)
        {
            _vm.SaveHint = "✗ " + Novashot.App.Localization.Loc.T("Cloud.TestError", ex.Message);
        }
        finally
        {
            TestConnectionButton.IsEnabled = true;
        }
    }
}
