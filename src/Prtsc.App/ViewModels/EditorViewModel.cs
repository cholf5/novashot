using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Prtsc.Core.Annotation;
using Prtsc.Core.Annotation.History;
using Prtsc.Core.Annotation.Model;
using Prtsc.Core.Annotation.Tools;
using Prtsc.Core.Capture;

namespace Prtsc.App.ViewModels;

/// <summary>
/// 编辑器视图模型。每次进入编辑器新建一个实例；<see cref="MainWindowViewModel.EnterEditor"/>
/// 拥有其生命期并在返回捕获态时 Dispose。
///
/// - 拥有 <see cref="CapturedImage"/>：Dispose 时释放（SKBitmap 一并释放）。
/// - <see cref="Document"/> 借用其 <c>Bitmap</c> 引用，本身不 IDisposable。
/// - <see cref="History"/>：单一 UndoRedoStack，capacity=50。
///
/// M4c.1：默认工具 Select；新增 <see cref="Selection"/> / <see cref="DeleteSelectionCommand"/> /
/// <see cref="DeselectCommand"/>。M4d 会补工具热键与更多样式。
///
/// M4c.2：新增 EditingText 系列——把某个 <see cref="TextAnnotation"/> 置入内联编辑态；
/// SkiaCanvas 会跳过对它的渲染，MainWindow 会在其 DIP 位置弹出 TextBox。
/// </summary>
public sealed partial class EditorViewModel : ObservableObject, IDisposable
{
    private readonly CapturedImage _capturedImage;

    public AnnotationDocument Document { get; }
    public UndoRedoStack History { get; }
    public NumberCounter NumberCounter { get; } = new();

    public RelayCommand UndoCommand { get; }
    public RelayCommand RedoCommand { get; }
    public RelayCommand DeleteSelectionCommand { get; }
    public RelayCommand DeselectCommand { get; }
    public RelayCommand CommitTextEditCommand { get; }
    public RelayCommand CancelTextEditCommand { get; }

    /// <summary>右侧属性面板 VM——生命期与 EditorViewModel 一致。</summary>
    public StylePanelViewModel StylePanel { get; }

    [ObservableProperty]
    private ToolKind _currentTool = ToolKind.Select;

    [ObservableProperty]
    private AnnotationStyle _currentStyle = AnnotationStyle.Default;

    /// <summary>当前选中的标注；null=无选择。SelectTool 读写它。</summary>
    [ObservableProperty]
    private AnnotationBase? _selection;

    // ---- Inline text editing state (M4c.2) ----

    /// <summary>正在内联编辑的 Text；null=不在编辑态。</summary>
    [ObservableProperty]
    private TextAnnotation? _editingText;

    /// <summary>TextBox 双向绑定的中间值。</summary>
    [ObservableProperty]
    private string _editingTextValue = string.Empty;

    /// <summary>TextBox 在 DIP 空间的位置与大小（由 SkiaCanvas 反算后写入）。</summary>
    [ObservableProperty]
    private Rect _editingTextDipBounds;

    /// <summary>TextBox 的字号（DIP）；应约等于 Text.FontSize × 视口 scale。</summary>
    [ObservableProperty]
    private double _editingTextFontSizeDip = 24d;

    /// <summary>true 时表示编辑的是刚刚创建的 Text；Cancel 会连带撤销 Add，Commit 空串也会撤销 Add。</summary>
    private bool _editingTextIsNewlyCreated;

    /// <summary>进入编辑前的原始文本，用于生成 <see cref="ChangeTextCommand"/>。</summary>
    private string _editingTextBefore = string.Empty;

    public int ImageWidth => Document.Width;
    public int ImageHeight => Document.Height;

    public EditorViewModel(CapturedImage image)
    {
        _capturedImage = image ?? throw new ArgumentNullException(nameof(image));
        Document = new AnnotationDocument(image.Bitmap);
        History = new UndoRedoStack();

        UndoCommand = new RelayCommand(
            execute: () => History.Undo(),
            canExecute: () => History.CanUndo);
        RedoCommand = new RelayCommand(
            execute: () => History.Redo(),
            canExecute: () => History.CanRedo);
        DeleteSelectionCommand = new RelayCommand(
            execute: DeleteSelection,
            canExecute: () => Selection is not null);
        DeselectCommand = new RelayCommand(
            execute: () => Selection = null);
        CommitTextEditCommand = new RelayCommand(CommitTextEdit);
        CancelTextEditCommand = new RelayCommand(CancelTextEdit);

        History.StateChanged += OnHistoryStateChanged;
        Document.Changed += OnDocumentChanged;

        StylePanel = new StylePanelViewModel(this);
    }

    private void OnHistoryStateChanged(object? sender, EventArgs e)
    {
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    private void OnDocumentChanged(object? sender, EventArgs e)
    {
        // 若被选 annotation 已经从文档中移除（如别处 Undo 掉 Add），清空选择
        if (Selection is not null && Document.IndexOf(Selection) < 0)
            Selection = null;
        // 若正在编辑的 Text 已从文档移除（Undo 掉刚 Add 的），退出编辑态
        if (EditingText is not null && Document.IndexOf(EditingText) < 0)
        {
            _editingTextIsNewlyCreated = false;
            EditingText = null;
        }
    }

    partial void OnSelectionChanged(AnnotationBase? value)
    {
        DeleteSelectionCommand.NotifyCanExecuteChanged();
    }

    partial void OnCurrentToolChanged(ToolKind value)
    {
        // 切离 Select 工具时清空选中，避免非 Select 工具下手柄仍显示
        if (value != ToolKind.Select) Selection = null;
    }

    public bool CanUndo => History.CanUndo;
    public bool CanRedo => History.CanRedo;

    /// <summary>按 <see cref="CurrentTool"/> 造一个新工具实例（每次拖拽用完即弃）。</summary>
    public IAnnotationTool CreateActiveTool() => CurrentTool switch
    {
        ToolKind.Select        => new SelectTool(
                                        getSelection: () => Selection,
                                        setSelection: a => Selection = a,
                                        history: History,
                                        annotations: Document.Annotations,
                                        document: Document),
        ToolKind.Rectangle     => new RectangleTool(),
        ToolKind.Ellipse       => new EllipseTool(),
        ToolKind.Line          => new LineTool(),
        ToolKind.ArrowStraight => new ArrowStraightTool(),
        ToolKind.ArrowCurved   => new ArrowCurvedTool(),
        ToolKind.Text          => new TextTool(),
        ToolKind.Number        => new NumberTool(NumberCounter),
        ToolKind.Spotlight     => new SpotlightTool(Document.Width, Document.Height),
        _ => NopTool.Instance,
    };

    /// <summary>把新标注封装成 AddAnnotationCommand 提交给 <see cref="History"/>。</summary>
    public void ExecuteAdd(AnnotationBase annotation)
    {
        ArgumentNullException.ThrowIfNull(annotation);
        History.Execute(new AddAnnotationCommand(Document, annotation));
    }

    private void DeleteSelection()
    {
        var sel = Selection;
        if (sel is null) return;
        // 先清 Selection，再 Execute，避免 SkiaCanvas 拿到已删除对象的 handles
        Selection = null;
        History.Execute(new RemoveAnnotationCommand(Document, sel));
    }

    // ---- Inline text editing (M4c.2) ----

    /// <summary>
    /// 进入 Text 内联编辑态。SkiaCanvas 会跳过对 <paramref name="text"/> 的渲染，
    /// MainWindow 在其 DIP 位置弹出 TextBox。
    /// </summary>
    /// <param name="text">已在文档里的 Text 标注。</param>
    /// <param name="dipBounds">TextBox 的 DIP 位置矩形（含少量 padding）。</param>
    /// <param name="fontSizeDip">TextBox 的字号（DIP）。</param>
    /// <param name="isNewlyCreated">true = 刚由 TextTool 创建，Cancel/空 Commit 会 Undo 掉 Add。</param>
    public void BeginTextEdit(TextAnnotation text, Rect dipBounds, double fontSizeDip, bool isNewlyCreated)
    {
        ArgumentNullException.ThrowIfNull(text);
        _editingTextBefore = text.Text;
        _editingTextIsNewlyCreated = isNewlyCreated;
        EditingTextValue = text.Text;
        EditingTextDipBounds = dipBounds;
        EditingTextFontSizeDip = fontSizeDip;
        EditingText = text;
    }

    /// <summary>SkiaCanvas 视口变化时用来刷新位置（不改文本内容）。</summary>
    public void UpdateEditingTextBounds(Rect dipBounds, double fontSizeDip)
    {
        if (EditingText is null) return;
        EditingTextDipBounds = dipBounds;
        EditingTextFontSizeDip = fontSizeDip;
    }

    private void CommitTextEdit()
    {
        var target = EditingText;
        if (target is null) return;
        var after = EditingTextValue ?? string.Empty;
        var before = _editingTextBefore;
        var wasNew = _editingTextIsNewlyCreated;

        // 先退出编辑态，再改历史，避免 OnDocumentChanged 回调时状态不一致
        EditingText = null;
        _editingTextIsNewlyCreated = false;

        // 空文本：新建的丢掉；既有的当作没改（也可以扩展成 Remove 但风险较大，暂保留原文本）
        if (string.IsNullOrEmpty(after))
        {
            if (wasNew)
            {
                // 撤销刚才的 AddAnnotationCommand + 从文档移除
                History.Undo();
            }
            return;
        }

        if (after == before) return;
        History.Execute(new ChangeTextCommand(target, before, after, Document));
    }

    private void CancelTextEdit()
    {
        var wasNew = _editingTextIsNewlyCreated;
        EditingText = null;
        _editingTextIsNewlyCreated = false;
        if (wasNew) History.Undo();
    }

    public void Dispose()
    {
        StylePanel.Dispose();
        History.StateChanged -= OnHistoryStateChanged;
        Document.Changed -= OnDocumentChanged;
        _capturedImage.Dispose();
    }

    /// <summary>M5+ 未落地工具用这个吞掉指针交互。</summary>
    private sealed class NopTool : IAnnotationTool
    {
        public static readonly NopTool Instance = new();
        public AnnotationBase? Preview => null;
        public void OnPointerDown(SkiaSharp.SKPoint imagePoint, AnnotationStyle style) { }
        public void OnPointerMove(SkiaSharp.SKPoint imagePoint) { }
        public AnnotationBase? OnPointerUp(SkiaSharp.SKPoint imagePoint) => null;
        public void Cancel() { }
    }
}
