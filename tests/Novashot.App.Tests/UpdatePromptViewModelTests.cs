using Novashot.App.Update;

namespace Novashot.App.Tests;

/// <summary>
/// C-1 更新提示对话框 VM：对话态与用户选择的纯逻辑（不碰 Velopack/网络）。
/// Lede 文案经 Loc（静态状态，默认 en）——只断言含两个版本号，不锁语言。
/// </summary>
public class UpdatePromptViewModelTests
{
    [Fact]
    public void Lede_ContainsBothVersions()
    {
        var vm = new UpdatePromptViewModel("0.2.0", "0.1.0");

        Assert.Contains("0.2.0", vm.Lede);
        Assert.Contains("0.1.0", vm.Lede);
        Assert.Equal("0.2.0", vm.NewVersion);
        Assert.Equal("0.1.0", vm.CurrentVersion);
    }

    [Fact]
    public void Choice_DefaultsToUndecided()
    {
        var vm = new UpdatePromptViewModel("0.2.0", "0.1.0");

        Assert.Equal(UpdatePromptChoice.Undecided, vm.Choice);
    }

    [Fact]
    public void Choose_RecordsEachAction()
    {
        var vm = new UpdatePromptViewModel("0.2.0", "0.1.0");
        vm.Choose(UpdatePromptChoice.Later);
        Assert.Equal(UpdatePromptChoice.Later, vm.Choice);

        var vm2 = new UpdatePromptViewModel("0.2.0", "0.1.0");
        vm2.Choose(UpdatePromptChoice.Skip);
        Assert.Equal(UpdatePromptChoice.Skip, vm2.Choice);
    }

    [Fact]
    public void BeginDownload_HidesActionsAndShowsPercent()
    {
        var vm = new UpdatePromptViewModel("0.2.0", "0.1.0");

        vm.BeginDownload();
        Assert.True(vm.IsBusy);
        Assert.False(vm.IsActionVisible); // 动作按钮隐藏，防重复触发

        vm.ReportProgress(42);
        Assert.Contains("42", vm.BusyText);
        Assert.True(vm.IsBusy);

        vm.EndDownloadFailed("boom");
        Assert.False(vm.IsBusy);
        Assert.True(vm.IsActionVisible);
        Assert.Contains("boom", vm.Hint);
    }

    [Fact]
    public void Choose_DuringBusy_IsIgnored()
    {
        var vm = new UpdatePromptViewModel("0.2.0", "0.1.0");
        vm.BeginDownload();

        vm.Choose(UpdatePromptChoice.Install);

        Assert.Equal(UpdatePromptChoice.Undecided, vm.Choice); // 下载中的点击不生效
    }

    [Fact]
    public void ReportProgress_ClampsToHundred()
    {
        var vm = new UpdatePromptViewModel("0.2.0", "0.1.0");
        vm.BeginDownload();

        vm.ReportProgress(150);

        Assert.Contains("100", vm.BusyText);
    }
}
