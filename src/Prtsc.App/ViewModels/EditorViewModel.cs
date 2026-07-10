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

    [ObservableProperty]
    private ToolKind _currentTool = ToolKind.Select;

    [ObservableProperty]
    private AnnotationStyle _currentStyle = AnnotationStyle.Default;

    /// <summary>当前选中的标注；null=无选择。SelectTool 读写它。</summary>
    [ObservableProperty]
    private AnnotationBase? _selection;

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

        History.StateChanged += OnHistoryStateChanged;
        Document.Changed += OnDocumentChanged;
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
                                        annotations: Document.Annotations),
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

    public void Dispose()
    {
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
