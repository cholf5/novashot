using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Novashot.App.ViewModels;

namespace Novashot.App.Controls;

/// <summary>
/// 左侧元素列表（M-Inspector）：画布标注的总览入口。
///
/// - 行点击 = 选中该标注 + 切到 Select 工具（<see cref="EditorViewModel.SelectFromListCommand"/>）
/// - 行 hover 露出 上移/下移（<see cref="EditorViewModel.BringForwardCommand"/> 等既有层序命令）
/// - 画布选中 ↔ 行高亮双向联动；选中行滚动进可视区
/// - 文档清空时整栏隐藏（MainWindow 用 <see cref="EditorViewModel.HasElements"/> 控制列宽）
///
/// 面板只读模型——所有改动经 EditorViewModel 的命令，不自己碰 Document。
/// </summary>
public partial class ElementListPanel : UserControl
{
    private EditorViewModel? _editor;
    private int _scrollRetries;

    public ElementListPanel()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        DetachedFromVisualTree += (_, _) => Unsubscribe();
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        Unsubscribe();
        _editor = DataContext as EditorViewModel;
        if (_editor is null) return;
        _editor.PropertyChanged += OnEditorPropertyChanged;
        _editor.ElementList.CollectionChanged += OnListChanged;
        ScrollSelectionIntoView(); // 初次挂载把选中行滚进视野
    }

    private void Unsubscribe()
    {
        if (_editor is null) return;
        _editor.PropertyChanged -= OnEditorPropertyChanged;
        _editor.ElementList.CollectionChanged -= OnListChanged;
        _editor = null;
    }

    private void OnEditorPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EditorViewModel.Selection))
            ScrollSelectionIntoView();
    }

    private void OnListChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        => ScrollSelectionIntoView();

    /// <summary>
    /// 把选中行滚进可视区。容器未 realize 时<b>至多重试 2 次</b>——headless 会话里布局
    /// 不一定在 Background 优先级自然跑，无界 Post 重试会变成自续队列挂死 Dispatch。
    /// 丢一次滚动机会无妨：选中态一变就会再来。
    /// </summary>
    private void ScrollSelectionIntoView()
    {
        var editor = _editor;
        if (editor is null || editor.Selection is null) { _scrollRetries = 0; return; }

        var list = this.FindControl<ItemsControl>("ElementListItems");
        if (list is null) return;

        var row = list.GetRealizedContainers()?.OfType<RadioButton>()
            .FirstOrDefault(c => c.Tag is ElementListItemViewModel item
                                 && ReferenceEquals(item.Annotation, editor.Selection));

        if (row is null)
        {
            if (_scrollRetries >= 2) return;
            _scrollRetries++;
            Dispatcher.UIThread.Post(ScrollSelectionIntoView, DispatcherPriority.Background);
            return;
        }

        _scrollRetries = 0;
        row.BringIntoView();
    }

    private void OnRowClick(object? sender, RoutedEventArgs e)
    {
        if (_editor is null) return;
        if ((sender as RadioButton)?.Tag is ElementListItemViewModel item)
            _editor.SelectFromListCommand.Execute(item);
    }

    private void OnMoveUpClick(object? sender, RoutedEventArgs e)
    {
        if (_editor is null) return;
        if ((sender as Button)?.Tag is not ElementListItemViewModel item) return;
        // 先选中再上移：命令全部作用在 Selection 上（与画布/Inspector 同一条路径）
        _editor.Selection = item.Annotation;
        _editor.BringForwardCommand.Execute(null);
    }

    private void OnMoveDownClick(object? sender, RoutedEventArgs e)
    {
        if (_editor is null) return;
        if ((sender as Button)?.Tag is not ElementListItemViewModel item) return;
        _editor.Selection = item.Annotation;
        _editor.SendBackwardCommand.Execute(null);
    }
}
