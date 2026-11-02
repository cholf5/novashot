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

    /// <summary>
    /// G-25（winshot）：用当前表单凭据做连接测试。R2 = HeadBucket，GDrive = 换 token。
    /// 结果写 SaveHint；测试不落盘（保存仍需点“保存”）。
    /// </summary>
    private async void OnTestConnectionClick(object? sender, RoutedEventArgs e)
    {
        TestConnectionButton.IsEnabled = false;
        _vm.SaveHint = Prtsc.App.Localization.Loc.T("Cloud.Testing");
        try
        {
            var (ok, message) = _vm.ProviderIndex switch
            {
                1 => await Prtsc.Core.Cloud.CloudConnectionTester.TestR2Async(_vm.ToR2Config()),
                2 => await Prtsc.Core.Cloud.CloudConnectionTester.TestGDriveAsync(_vm.ToGDriveConfig()),
                _ => (false, "先选择 R2 或 Google Drive 渠道再测试。"),
            };
            _vm.SaveHint = (ok ? "✓ " : "✗ ") + message;
        }
        catch (Exception ex)
        {
            _vm.SaveHint = "✗ " + Prtsc.App.Localization.Loc.T("Cloud.TestError", ex.Message);
        }
        finally
        {
            TestConnectionButton.IsEnabled = true;
        }
    }
}
