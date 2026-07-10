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
/// M4a：默认工具 Rectangle、默认样式红色 2 px；M4d 会加工具切换热键与更多样式。
/// </summary>
public sealed partial class EditorViewModel : ObservableObject, IDisposable
{
    private readonly CapturedImage _capturedImage;

    public AnnotationDocument Document { get; }
    public UndoRedoStack History { get; }
    public NumberCounter NumberCounter { get; } = new();

    public RelayCommand UndoCommand { get; }
    public RelayCommand RedoCommand { get; }

    [ObservableProperty]
    private ToolKind _currentTool = ToolKind.Rectangle;

    [ObservableProperty]
    private AnnotationStyle _currentStyle = AnnotationStyle.Default;

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

        History.StateChanged += OnHistoryStateChanged;
    }

    private void OnHistoryStateChanged(object? sender, EventArgs e)
    {
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    public bool CanUndo => History.CanUndo;
    public bool CanRedo => History.CanRedo;

    /// <summary>按 <see cref="CurrentTool"/> 造一个新工具实例（每次拖拽用完即弃）。</summary>
    public IAnnotationTool CreateActiveTool() => CurrentTool switch
    {
        ToolKind.Rectangle     => new RectangleTool(),
        ToolKind.Ellipse       => new EllipseTool(),
        ToolKind.Line          => new LineTool(),
        ToolKind.ArrowStraight => new ArrowStraightTool(),
        ToolKind.ArrowCurved   => new ArrowCurvedTool(),
        ToolKind.Text          => new TextTool(),
        ToolKind.Number        => new NumberTool(NumberCounter),
        ToolKind.Spotlight     => new SpotlightTool(Document.Width, Document.Height),
        _ => NopTool.Instance, // Select 与 M5+ 未落地工具用 no-op 兜底
    };

    /// <summary>把新标注封装成 AddAnnotationCommand 提交给 <see cref="History"/>。</summary>
    public void ExecuteAdd(AnnotationBase annotation)
    {
        ArgumentNullException.ThrowIfNull(annotation);
        History.Execute(new AddAnnotationCommand(Document, annotation));
    }

    public void Dispose()
    {
        History.StateChanged -= OnHistoryStateChanged;
        _capturedImage.Dispose();
    }

    /// <summary>M4a 占位：Select / 未实现工具用这个吞掉指针交互。</summary>
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
