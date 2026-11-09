using Avalonia.Controls;
using Avalonia.Interactivity;
using Microsoft.Extensions.Logging;
using Prtsc.App.Localization;
using Prtsc.App.Update;
using Prtsc.Core.Config;
using Velopack;

namespace Prtsc.App.Views;

/// <summary>
/// C-1 更新提示对话框（非阻塞 Show）：新版本号 + 下载并安装 / 跳过此版本 / 以后再说。
/// 「跳过此版本」把目标版本写 config.Update.SkippedVersion 并存盘（同版本不再提示，更高版本会再提示）；
/// 「下载并安装」→ DownloadUpdatesAsync（进度回 UI）→ ApplyUpdatesAndRestart（该方法会立即退出进程）。
/// 所有 Velopack 调用的异常都落在本窗口内部：失败只改 Hint 文案，绝不崩溃。
/// </summary>
public partial class UpdatePromptWindow : Window
{
    private readonly UpdatePromptViewModel _vm = null!;
    private readonly UpdateManager _manager = null!;
    private readonly IConfigStore _store = null!;
    private readonly ILogger _logger = null!;
    private readonly string _targetVersion = "";

    public UpdatePromptWindow()
    {
        InitializeComponent();
    }

    public UpdatePromptWindow(IConfigStore store, UpdateManager manager, UpdateInfo info,
        string targetVersion, string currentVersion, ILogger logger)
    {
        _store = store;
        _manager = manager;
        _targetVersion = targetVersion;
        _logger = logger;
        _vm = new UpdatePromptViewModel(targetVersion, currentVersion);
        DataContext = _vm;
        InitializeComponent();
    }

    private void OnInstallClick(object? sender, RoutedEventArgs e) => _ = InstallAsync();

    private async Task InstallAsync()
    {
        try
        {
            _vm.Choose(UpdatePromptChoice.Install);
            _vm.BeginDownload();
            var info = await _manager.CheckForUpdatesAsync();
            if (info is null)
            {
                _vm.EndDownloadFailed(Loc.T("Update.Prompt.NoUpdateOnRetry"));
                return;
            }

            await _manager.DownloadUpdatesAsync(info, p =>
                Avalonia.Threading.Dispatcher.UIThread.Post(() => _vm.ReportProgress(p)));

            _vm.BusyText = Loc.T("Update.Prompt.Restarting");
            // Velopack 接管：应用更新并立即重启进程（后续代码不会执行）
            _manager.ApplyUpdatesAndRestart(info.TargetFullRelease);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Update download/apply failed");
            if (_vm.IsBusy) _vm.EndDownloadFailed(ex.Message);
        }
    }

    private void OnSkipClick(object? sender, RoutedEventArgs e)
    {
        _vm.Choose(UpdatePromptChoice.Skip);
        _store.Update(c => c.Update.SkippedVersion = _targetVersion);
        _ = _store.SaveAsync();
        Close();
    }

    private void OnLaterClick(object? sender, RoutedEventArgs e)
    {
        _vm.Choose(UpdatePromptChoice.Later);
        Close();
    }
}
