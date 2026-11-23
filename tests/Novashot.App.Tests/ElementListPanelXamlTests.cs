using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Novashot.App.Controls;
using Novashot.App.ViewModels;
using Novashot.Core.Annotation;
using Novashot.Core.Annotation.Model;
using Novashot.Core.Annotation.Tools;
using Novashot.Core.Capture;
using Novashot.Core.Clipboard;
using SkiaSharp;
using Xunit;

namespace Novashot.App.Tests;

/// <summary>
/// 左侧元素面板接线（M-Inspector）：行 realize、点行 = 选中 + 切 Select、
/// 层序按钮走 Editor 命令、文档清空整栏隐藏。
/// </summary>
[Collection("HeadlessUi")]
public class ElementListPanelXamlTests
{
    [Fact]
    public async Task ElementListPanel_RealizesRows_AndClickSelects()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestAppHeadlessBootstrap));
        System.Func<System.Threading.Tasks.Task> act = async () =>
        {
            var bmp = new SKBitmap(new SKImageInfo(64, 48, SKColorType.Bgra8888, SKAlphaType.Opaque));
            bmp.Erase(SKColors.DimGray);
            var vm = new MainWindowViewModel(new FakeClipboardService());
            vm.EnterEditor(new CapturedImage(bmp, new Novashot.Core.Common.PixelRect(0, 0, 64, 48), 1.0, CaptureSource.Region));
            var editor = vm.Editor!;
            editor.ExecuteAdd(new RectangleAnnotation(new SKRect(0, 0, 10, 10), AnnotationStyle.Default));
            editor.ExecuteAdd(new MosaicAnnotation(new SKRect(5, 5, 25, 25), editor.Document.Source, AnnotationStyle.Default));
            editor.CurrentTool = ToolKind.Mosaic; // 非默认工具，验证点行会切走

            var panel = new ElementListPanel { DataContext = editor };
            var window = new Window { Width = 400, Height = 600, Content = panel };
            window.Show();
            window.UpdateLayout();
            window.UpdateLayout();

            var list = panel.FindControl<ItemsControl>("ElementListItems");
            Assert.NotNull(list);
            Assert.Equal(2, list!.ItemCount);
            Assert.True(panel.IsVisible);

            // 选中联动：Editor.Selection = 栈顶 Mosaic → 列表首行高亮
            Assert.True(editor.ElementList[0].IsSelected);
            Assert.False(editor.ElementList[1].IsSelected);

            // 点首行（栈顶 Mosaic）→ 选中 + 切 Select 工具
            editor.SelectFromListCommand.Execute(editor.ElementList[0]);
            Assert.Equal(ToolKind.Select, editor.CurrentTool);
            Assert.Same(editor.ElementList[0].Annotation, editor.Selection);

            // 层序按钮路径：置底命令可执行且生效
            editor.Selection = editor.ElementList[0].Annotation;
            editor.SendToBackCommand.Execute(null);
            Assert.Same(editor.ElementList[0].Annotation, editor.ElementList[1].Annotation); // 列表顺序翻转

            // 清空文档 → 整栏隐藏
            editor.Document.Clear();
            window.UpdateLayout();
            Assert.False(panel.IsVisible);
            window.Close();
        };
        await session.Dispatch(act, System.Threading.CancellationToken.None);
    }
}
