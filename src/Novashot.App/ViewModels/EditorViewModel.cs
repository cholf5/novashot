using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Novashot.Core.Annotation;
using Novashot.Core.Annotation.History;
using Novashot.Core.Annotation.Model;
using Novashot.Core.Annotation.Tools;
using Novashot.Core.Capture;
using Novashot.Core.Editor;
using SkiaSharp;

namespace Novashot.App.ViewModels;

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

    public RelayCommand UndoCommand { get; }
    public RelayCommand RedoCommand { get; }
    public RelayCommand DeleteSelectionCommand { get; }
    public RelayCommand DeselectCommand { get; }
    public RelayCommand CommitTextEditCommand { get; }
    public RelayCommand CancelTextEditCommand { get; }

    /// <summary>右侧属性面板 VM——生命期与 EditorViewModel 一致。</summary>
    public StylePanelViewModel StylePanel { get; }

    /// <summary>M6b：合成外观面板 VM——生命期与 EditorViewModel 一致。</summary>
    public CompositionPanelViewModel CompositionPanelViewModel { get; }

    [ObservableProperty]
    private ToolKind _currentTool = ToolKind.Select;

    [ObservableProperty]
    private AnnotationStyle _currentStyle = AnnotationStyle.Default;

    /// <summary>
    /// M6b：文档级合成状态（背景/padding/圆角/阴影/内衬/边框）。改动不进 Undo，
    /// 但要触发 <see cref="AnnotationDocument.NotifyMutated"/> 让画布重绘（视口矩阵也随内容尺寸变化）。
    /// </summary>
    [ObservableProperty]
    private Novashot.Core.Editor.EditorState _compositionState = Novashot.Core.Editor.EditorState.Default;

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

    /// <summary>合成输出尺寸（含 padding）。M6b 起视口 fit 依据它。</summary>
    public int ContentWidth { get; private set; }
    public int ContentHeight { get; private set; }

    partial void OnCompositionStateChanged(Novashot.Core.Editor.EditorState value)
    {
        var size = Novashot.Core.Editor.Composition.Measure(Document, value);
        ContentWidth = size.Width;
        ContentHeight = size.Height;
        OnPropertyChanged(nameof(ContentWidth));
        OnPropertyChanged(nameof(ContentHeight));
        Document.NotifyMutated();
    }

    public EditorViewModel(CapturedImage image, Novashot.Core.Editor.ColorHistory? colorHistory = null)
    {
        _capturedImage = image ?? throw new ArgumentNullException(nameof(image));
        Document = new AnnotationDocument(image.Bitmap);
        History = new UndoRedoStack();

        var initialSize = Novashot.Core.Editor.Composition.Measure(Document, CompositionState);
        ContentWidth = initialSize.Width;
        ContentHeight = initialSize.Height;

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

        StylePanel = new StylePanelViewModel(this, colorHistory);
        CompositionPanelViewModel = new CompositionPanelViewModel(this);
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

    /// <summary>image 空间 → 内容空间（crop 原点平移 + padding）。crop 激活时内容区左上角 = crop 原点。</summary>
    public SKPoint ImageToContent(SKPoint imagePoint)
    {
        var s = CompositionState;
        var o = s.CropRect?.Location ?? default;
        return new SKPoint(imagePoint.X - o.X + s.Padding, imagePoint.Y - o.Y + s.Padding);
    }

    /// <summary>内容空间 → image 空间（<see cref="ImageToContent"/> 的逆）。</summary>
    public SKPoint ContentToImage(SKPoint contentPoint)
    {
        var s = CompositionState;
        var o = s.CropRect?.Location ?? default;
        return new SKPoint(contentPoint.X - s.Padding + o.X, contentPoint.Y - s.Padding + o.Y);
    }

    /// <summary>image 空间矩形 → 内容空间矩形。</summary>
    public SKRect ImageRectToContent(SKRect imageRect)
    {
        var tl = ImageToContent(new SKPoint(imageRect.Left, imageRect.Top));
        return new SKRect(tl.X, tl.Y, imageRect.Width, imageRect.Height);
    }

    /// <summary>当前工具每次拖拽用完即弃（每次 pointer down 新建）。</summary>
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
        ToolKind.Number        => new NumberTool(() => NumberSequenceMinUnused.NextFor(Document.Annotations)),
        ToolKind.Spotlight     => new SpotlightTool(Document.Width, Document.Height),
        // M5a：自由手绘
        ToolKind.Pencil        => new PencilTool(),
        ToolKind.Highlighter   => new HighlighterTool(),
        // M6d：非破坏裁剪（拖拽结果走 ApplyCrop，不产出标注）
        ToolKind.Crop          => new CropTool(new SKRect(0, 0, Document.Width, Document.Height)),
        // M5b：采样型区域效果（非拥有引用 Document.Source，渲染时现场采样）
        ToolKind.Mosaic        => new RegionEffectTool((r, s) => new MosaicAnnotation(r, Document.Source, s)),
        ToolKind.Blur          => new RegionEffectTool((r, s) => new BlurAnnotation(r, Document.Source, s)),
        ToolKind.Invert        => new RegionEffectTool((r, s) => new InvertAnnotation(r, Document.Source, s)),
        _ => NopTool.Instance,
    };

    /// <summary>把新标注封装成 AddAnnotationCommand 提交给 <see cref="History"/>。</summary>
    public void ExecuteAdd(AnnotationBase annotation)
    {
        ArgumentNullException.ThrowIfNull(annotation);
        History.Execute(new AddAnnotationCommand(Document, annotation));
    }

    // ---- M6d: non-destructive crop ----

    /// <summary>
    /// 应用裁剪框（image 空间；越界自动钳回图像内）。写入 CompositionState.CropRect ——
    /// 走 OnCompositionStateChanged 通路（不进 Undo，同合成状态策略），ContentWidth/Height 自动跟随。
    /// null = 撤销裁剪。
    /// </summary>
    public void ApplyCrop(SKRect? cropRect)
    {
        var bounds = new SKRect(0, 0, Document.Width, Document.Height);
        var clamped = cropRect is null ? null : CropRects.ClampToImage(cropRect.Value, bounds);
        CompositionState = CompositionState with { CropRect = clamped };
    }

    /// <summary>把当前裁剪框（或全图）吸到 w:h 比例并应用。</summary>
    public void SnapCropToRatio(float w, float h)
    {
        var bounds = new SKRect(0, 0, Document.Width, Document.Height);
        var current = CompositionState.CropRect ?? bounds;
        var snapped = CropRects.SnapToRatio(current, w, h, bounds);
        ApplyCrop(snapped);
    }

    /// <summary>当前裁剪框比例吸附目标（0,0 = auto）。CropTool 拖拽结束时使用。</summary>
    public (float W, float H) PendingCropRatio { get; set; } = (0f, 0f);

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
        CompositionPanelViewModel.Dispose();
        History.StateChanged -= OnHistoryStateChanged;
        Document.Changed -= OnDocumentChanged;
        Document.ReleaseRenderImage(); // 渲染视图缓存随会话释放（先于 Source 所有的 CapturedImage）
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
