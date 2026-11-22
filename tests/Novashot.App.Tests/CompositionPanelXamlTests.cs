using System.Threading.Tasks;
using Avalonia.Headless;
using Avalonia.Controls;
using Novashot.App.Controls;
using Novashot.App.ViewModels;
using Novashot.Core.Capture;
using Novashot.Core.Common;
using SkiaSharp;
using Xunit;

namespace Novashot.App.Tests;

/// <summary>
/// MAC-1 冒烟回归：CompositionPanel 曾因 MultiBinding 误配单值转换器（CssGradient），
/// 模板实例化时抛 InvalidCastException——全屏截图进编辑器即崩（2026-11-01 用户报告）。
/// Headless 会话里真实实例化面板并构建 24 个渐变预设项，锁定该路径。
/// </summary>
[Collection("HeadlessUi")]
public class CompositionPanelXamlTests
{
    [Fact]
    public async Task CompositionPanel_Instantiates_AndBuildsGradientItems()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestAppHeadlessBootstrap));
        System.Func<System.Threading.Tasks.Task> act = async () =>
        {
            using var img = new CapturedImage(
                TestBitmaps.MakeSolid(2, 2), new PixelRect(0, 0, 2, 2), 1.0, CaptureSource.Fullscreen);
            var editor = new EditorViewModel(img);
            var panel = new CompositionPanel { DataContext = new CompositionPanelViewModel(editor) };

            // 挂进窗口触发真实布局/项容器生成（崩溃点在 ItemTemplate.Build）
            var window = new Window { Width = 800, Height = 1200, Content = panel };
            window.Show();
            window.UpdateLayout();
            window.UpdateLayout(); // 两轮：确保 Measure/Arrange 完成、所有 item realize

            var items = panel.FindControl<ItemsControl>("GradientPresets");
            Assert.NotNull(panel.DataContext);
            Assert.NotNull(items);
            Assert.Equal(24, items!.Items?.Count);
            // 逐项验证容器已构建（ItemTemplate.Build 执行过 → 此前在这里抛 InvalidCastException）
            var realized = items.GetRealizedContainers()!.ToList();
            Assert.Equal(24, realized.Count);
            window.Close();
        };
        await session.Dispatch(act, System.Threading.CancellationToken.None);
    }
}

/// <summary>Headless 测试宿主 App：仅注册 Application 类型，无 Styles 依赖。</summary>
public class TestAppHeadlessBootstrap : global::Avalonia.Application
{
    public override void Initialize() { }
}

internal static class TestBitmaps
{
    public static SKBitmap MakeSolid(int w, int h)
    {
        var bmp = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
        bmp.Erase(new SKColor(40, 40, 40));
        return bmp;
    }
}

/// <summary>
/// 锁定转换器契约：CompositionPanel 的渐变预设是单值绑定。
/// 若将来想引入多值绑定，必须新增 IMultiValueConverter 实现——
/// 不能把 IValueConverter 直接塞给 MultiBinding（模板实例化时 InvalidCastException，MAC 冒烟实锤）。
/// </summary>
public class CssGradientConverterContractTests
{
    [Fact]
    public void CssGradientConverter_IsSingleValueConverter()
    {
        var converter = Novashot.App.Converters.CssGradientToBrushConverter.Instance;
        Assert.IsAssignableFrom<Avalonia.Data.Converters.IValueConverter>(converter);
        Assert.IsNotAssignableFrom<Avalonia.Data.Converters.IMultiValueConverter>(converter);

        // 预设 CSS 渐变能产出画刷（正常路径抽查）
        var brush = converter.Convert(
            "linear-gradient(45deg, #FF0000 0%, #0000FF 100%)",
            typeof(Avalonia.Media.IBrush), null, System.Globalization.CultureInfo.InvariantCulture);
        Assert.IsAssignableFrom<Avalonia.Media.GradientBrush>(brush);
    }
}
