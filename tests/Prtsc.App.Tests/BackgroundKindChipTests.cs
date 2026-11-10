using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Prtsc.App.Controls;
using Prtsc.App.ViewModels;
using Prtsc.Core.Capture;
using Prtsc.Core.Common;
using Prtsc.Core.Editor;
using Xunit;

namespace Prtsc.App.Tests;

/// <summary>
/// Q-1 回归：背景三态 chip 的 IsChecked 曾经是 GroupName + TwoWay 绑定。
/// RadioButtonGroupManager 取消兄弟 → 兄弟的 IsChecked 回写 VM → VM 整表通知 → 绑定重新发布
/// → 再次进入 IsCheckedChanged，形成环，加压时栈溢出直接崩进程（测试宿主 abort，真机同样会崩）。
/// 修法：输入走 Click（Tag 承载状态），IsChecked 只做 OneWay 回显——目标侧变更永不写回 VM，
/// 环在结构上不成立，而不是靠「通知时机碰巧错开」。
/// </summary>
[Collection("HeadlessUi")]
public class BackgroundKindChipTests
{
    /// <summary>
    /// 注意 <see cref="CapturedImage"/> 必须活到窗口关闭之后：编辑器/画布在布局与渲染阶段还会读源位图，
    /// 提前 Dispose 会让 Skia 直接 abort 宿主进程（无异常、无堆栈，排查成本极高）。
    /// </summary>
    private static (Window Window, CompositionPanel Panel, CompositionPanelViewModel Vm,
                    EditorViewModel Editor, CapturedImage Image) Build()
    {
        var image = new CapturedImage(
            TestBitmaps.MakeSolid(2, 2), new PixelRect(0, 0, 2, 2), 1.0, CaptureSource.Fullscreen);
        var editor = new EditorViewModel(image);
        var vm = new CompositionPanelViewModel(editor);
        var panel = new CompositionPanel { DataContext = vm };
        var window = new Window { Width = 800, Height = 1200, Content = panel };
        window.Show();
        window.UpdateLayout();
        return (window, panel, vm, editor, image);
    }

    private static RadioButton Chip(CompositionPanel panel, string name)
        => panel.FindControl<RadioButton>(name) ?? throw new Xunit.Sdk.XunitException($"{name} 不存在");

    private static void Click(RadioButton chip)
    {
        // 真实点击：RadioButton.OnClick 先置 IsChecked，再抛 Click；这里直接抛事件 = 走行为侧入口
        chip.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    public async Task ChipsAreDisplayOnly_TargetChange_DoesNotWriteBackToVm()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestAppHeadlessBootstrap));
        await session.Dispatch(() =>
        {
            var (window, panel, _, editor, image) = Build();
            var before = editor.CompositionState.Background;

            // 直接改目标侧（组管理器取消兄弟时做的就是这件事）：OneWay 下不得回写 VM。
            // 修复前这里会沿「回写 → 通知 → 重新发布」递归，直到栈溢出把测试宿主打死。
            Chip(panel, "BgSolidChip").IsChecked = true;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(before, editor.CompositionState.Background);
            window.Close();
            image.Dispose();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Click_SwitchesBackground_KeepsSingleSelection_AndAppliesOnce()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestAppHeadlessBootstrap));
        await session.Dispatch(() =>
        {
            var (window, panel, vm, editor, image) = Build();
            var applied = 0;
            vm.CanvasStateChanged = _ => applied++;   // E-2：Apply 是唯一咽喉点，一次点击恰一次

            Click(Chip(panel, "BgSolidChip"));
            Assert.Equal(BackgroundKind.SolidColor, editor.CompositionState.Background);
            Assert.Equal(1, applied);

            Click(Chip(panel, "BgGradientChip"));
            Assert.Equal(BackgroundKind.Gradient, editor.CompositionState.Background);

            Click(Chip(panel, "BgNoneChip"));
            Assert.Equal(BackgroundKind.None, editor.CompositionState.Background);
            Assert.Equal(3, applied);

            // 回显：三枚 chip 与 VM 严格互斥一致（选中态只由 VM 单向决定）
            Click(Chip(panel, "BgSolidChip"));
            Assert.True(Chip(panel, "BgSolidChip").IsChecked);
            Assert.False(Chip(panel, "BgNoneChip").IsChecked);
            Assert.False(Chip(panel, "BgGradientChip").IsChecked);

            window.Close();
            image.Dispose();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task RapidStateFlips_DoNotRecurse_AndStayConsistent()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestAppHeadlessBootstrap));
        await session.Dispatch(() =>
        {
            var (window, panel, vm, editor, image) = Build();
            var chips = new[]
            {
                (Chip(panel, "BgNoneChip"), BackgroundKind.None),
                (Chip(panel, "BgSolidChip"), BackgroundKind.SolidColor),
                (Chip(panel, "BgGradientChip"), BackgroundKind.Gradient),
            };

            // 修复前：这一圈在 headless 下会踩到 Q-1 的环（全量跑时表现为随机 abort）。
            for (var i = 0; i < 100; i++)
            {
                var (chip, kind) = chips[i % chips.Length];
                Click(chip);
                Assert.Equal(kind, editor.CompositionState.Background);
            }

            // 程序侧改态（色板/渐变格/自动取色都走 Apply）也必须把 chip 回显对上
            vm.SetGradient("linear-gradient(45deg, #FF0000 0%, #0000FF 100%)");
            Dispatcher.UIThread.RunJobs();
            Assert.True(chips[2].Item1.IsChecked);
            Assert.False(chips[0].Item1.IsChecked);

            window.Close();
            image.Dispose();
        }, CancellationToken.None);
    }
}
