using CommunityToolkit.Mvvm.ComponentModel;
using Novashot.App.Localization;

namespace Novashot.App.Update;

/// <summary>用户在更新提示框的三选一（Undecided = 窗口被直接关掉，视同「以后再说」）。</summary>
public enum UpdatePromptChoice { Undecided, Install, Skip, Later }

/// <summary>
/// C-1 更新提示对话框 VM：不碰 Velopack/网络，只承载版本文案、下载进度与用户选择
/// （决策入参合法性已在 Core UpdateCheckPolicy 钉死，这里只管对话态；App.Tests 可测）。
/// </summary>
public sealed partial class UpdatePromptViewModel : ObservableObject
{
    [ObservableProperty] private string _newVersion = "";
    [ObservableProperty] private string _currentVersion = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _busyText = "";
    [ObservableProperty] private string _hint = "";

    /// <summary>完整提示文案（Loc.T("Update.Prompt.Lede", new, current)，构造时定死）。</summary>
    public string Lede { get; }

    /// <summary>
    /// 用户的选择（对话态记录）。注意：Skip 的 SkippedVersion 落盘与窗口关闭的副作用
    /// 由 UpdatePromptWindow 事件直接处理，启动链路不读本属性——当前读者只有测试（review M8）。
    /// </summary>
    public UpdatePromptChoice Choice { get; private set; } = UpdatePromptChoice.Undecided;

    /// <summary>下载期间隐藏动作按钮（避免重复触发 ApplyUpdatesAndRestart）。</summary>
    public bool IsActionVisible => !IsBusy;

    public UpdatePromptViewModel(string newVersion, string currentVersion)
    {
        NewVersion = newVersion;
        CurrentVersion = currentVersion;
        Lede = Loc.T("Update.Prompt.Lede", newVersion, currentVersion);
    }

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(IsActionVisible));

    public void Choose(UpdatePromptChoice choice)
    {
        if (IsBusy) return; // 下载中不允许再选
        Choice = choice;
    }

    public void BeginDownload()
    {
        IsBusy = true;
        Hint = "";
        BusyText = Loc.T("Update.Prompt.Downloading", 0);
    }

    public void ReportProgress(int percent)
        => BusyText = Loc.T("Update.Prompt.Downloading", Math.Clamp(percent, 0, 100));

    public void EndDownloadFailed(string detail)
    {
        IsBusy = false;
        BusyText = "";
        Hint = Loc.T("Update.Prompt.DownloadFailed", detail);
    }
}
