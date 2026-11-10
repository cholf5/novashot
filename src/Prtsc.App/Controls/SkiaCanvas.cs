using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Prtsc.App.ViewModels;
using Prtsc.Core.Annotation;
using Prtsc.Core.Annotation.Editing;
using Prtsc.Core.Annotation.Model;
using Prtsc.Core.Annotation.Tools;
using Prtsc.Core.Editor;
using SkiaSharp;

namespace Prtsc.App.Controls;

/// <summary>
/// 编辑器主画布：通过 <see cref="ICustomDrawOperation"/> 拿到 Avalonia Skia 后端的 <see cref="SKCanvas"/>
/// 直接绘制源图 + 标注 + 拖拽预览 + 选中框/手柄。
///
/// 坐标：内部所有 annotation 均在<b>源图像素空间</b>；控件通过 uniform fit-to-bounds 变换绘制。
/// Pointer 事件在 DIP 空间到达，经反变换后送给 <see cref="IAnnotationTool"/>。
/// 选中框与手柄画在 DIP 空间，保证不随视口缩放变形。
///
/// M4c.2：暴露 <see cref="TryMapImageRectToDip"/> + <see cref="ViewportInvalidated"/> 供
/// MainWindow 定位浮动 TextBox；双击命中 <see cref="TextAnnotation"/> 触发内联编辑；
/// 渲染时跳过 <see cref="EditorViewModel.EditingText"/> 以免与 TextBox 重叠。
/// </summary>
public sealed class SkiaCanvas : Control
{
    public static readonly StyledProperty<EditorViewModel?> EditorProperty =
        AvaloniaProperty.Register<SkiaCanvas, EditorViewModel?>(nameof(Editor));

    /// <summary>手柄命中半径（DIP）。M2 的 SelectionAdorner 用 8f，此处对齐。</summary>
    public const float HandleHitRadiusDip = 8f;

    /// <summary>手柄可视方块边长（DIP）。</summary>
    public const float HandleVisualSizeDip = 8f;

    /// <summary>Body 命中容差（DIP）：细线也能点中。</summary>
    public const float BodyHitSlopDip = 4f;

    public EditorViewModel? Editor
    {
        get => GetValue(EditorProperty);
        set => SetValue(EditorProperty, value);
    }

    /// <summary>视口矩阵可能改变时触发（Bounds 变、Editor 换、ImageSize 变）。</summary>
    public event EventHandler? ViewportInvalidated;

    private IAnnotationTool? _activeTool;
    private EditorViewModel? _subscribedEditor;

    // B-4：crop reshape 拖拽态（X 工具 + 已应用裁剪框的手柄拖拽）。非 IAnnotationTool——
    // 拖拽每步实时 ApplyCrop，Esc/CaptureLost 恢复 _cropReshapeOriginal。
    private HandleKind? _cropHandleDrag;
    private SKRect _cropReshapeOriginal;

    // M6d 起：三层坐标换算（image/content/DIP）的唯一实现在 EditorViewModel
    // （ImageToContent/ContentToImage/ImageRectToContent）——crop 激活时 content 原点 = crop 原点。

    public SkiaCanvas()
    {
        Focusable = true;
        ClipToBounds = true;
    }

    /// <summary>
    /// Esc 取消进行中的工具拖拽（flameshot 逐级退出链的最后一环之前）：释放预览，不产生标注。
    /// 画布有焦点时（拖拽中会 Capture）先于 MainWindow 的 Deselect 生效。
    /// </summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || e.Key != Key.Escape) return;
        // B-4：reshape 拖拽中的 Esc——恢复拖拽起始框（画布有焦点时先于 MainWindow 的 Deselect 生效）
        if (_cropHandleDrag is not null)
        {
            CancelCropReshape();
            e.Handled = true;
            return;
        }
        if (_activeTool is null) return;
        _activeTool.Cancel();
        _activeTool = null;
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == EditorProperty)
        {
            OnEditorChanged(change.GetOldValue<EditorViewModel?>(), change.GetNewValue<EditorViewModel?>());
        }
        else if (change.Property == BoundsProperty)
        {
            ViewportInvalidated?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnEditorChanged(EditorViewModel? oldEditor, EditorViewModel? newEditor)
    {
        if (_subscribedEditor != null)
        {
            _subscribedEditor.Document.Changed -= OnDocumentChanged;
            _subscribedEditor.PropertyChanged -= OnEditorPropertyChanged;
        }
        _subscribedEditor = newEditor;
        if (_subscribedEditor != null)
        {
            _subscribedEditor.Document.Changed += OnDocumentChanged;
            _subscribedEditor.PropertyChanged += OnEditorPropertyChanged;
        }
        _activeTool = null;
        _cropHandleDrag = null; // B-4（review M2）：Editor 换替时 reshape 态一并失效，防旧起始框写入新 editor
        InvalidateVisual();
        ViewportInvalidated?.Invoke(this, EventArgs.Empty);
    }

    private void OnDocumentChanged(object? sender, EventArgs e) => InvalidateVisual();

    private void OnEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Tool / Style / Selection / EditingText / CompositionState 变化都可能影响渲染
        // （CompositionState 变 padding 时视口矩阵也会变，故一并通知视口重算）
        if (e.PropertyName is nameof(EditorViewModel.CurrentTool)
            or nameof(EditorViewModel.CurrentStyle)
            or nameof(EditorViewModel.Selection)
            or nameof(EditorViewModel.EditingText)
            or nameof(EditorViewModel.CompositionState))
        {
            InvalidateVisual();
            ViewportInvalidated?.Invoke(this, EventArgs.Empty);
        }
    }

    // ---- Rendering ----

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var editor = Editor;
        if (editor is null) return;
        var bounds = Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        context.Custom(new DrawOp(
            new Rect(bounds.Size),
            editor.Document,
            editor.CompositionState,
            _activeTool?.Preview,
            editor.Selection,
            editor.EditingText,
            ComputeViewportMatrix(editor, bounds),
            CropPreviewRect,
            new SKSize(editor.ContentWidth, editor.ContentHeight),
            cropToolActive: editor.CurrentTool == ToolKind.Crop));
    }

    /// <summary>
    /// 计算 image-pixel → DIP 的仿射变换：uniform 缩放 + 居中平移。
    /// M6b：fit 的是<b>内容尺寸</b>（含 padding）；标注坐标在 image 空间，由
    /// <see cref="ImageToContent"/> / <see cref="ContentToImage"/> 换算。
    /// </summary>
    private static SKMatrix ComputeViewportMatrix(EditorViewModel editor, Rect bounds)
    {
        var w = editor.ContentWidth;
        var h = editor.ContentHeight;
        if (w <= 0 || h <= 0) return SKMatrix.CreateIdentity();

        var scale = Math.Min(bounds.Width / w, bounds.Height / h);
        var dx = (bounds.Width - w * scale) * 0.5;
        var dy = (bounds.Height - h * scale) * 0.5;

        var m = SKMatrix.CreateScale((float)scale, (float)scale);
        m = m.PostConcat(SKMatrix.CreateTranslation((float)dx, (float)dy));
        return m;
    }

    /// <summary>image-pixel 空间的 slop（用于命中测试）。视口缩放会让 DIP slop 换算成像素 slop。</summary>
    private static float DipToImageDistance(SKMatrix imageToDip, float dip)
    {
        // 只取 scale X 就行——我们的视口是 uniform
        var sx = imageToDip.ScaleX;
        if (sx < 1e-6f) return dip;
        return dip / sx;
    }

    /// <summary>
    /// 把 image-pixel 空间矩形映射到本控件的 DIP 坐标。
    /// 供 MainWindow 定位浮动 TextBox。视口未就绪返回 false。
    /// </summary>
    public bool TryMapImageRectToDip(SKRect imageRect, out Rect dipRect, out double dipScale)
    {
        dipRect = default;
        dipScale = 1d;
        var editor = Editor;
        if (editor is null) return false;
        var b = Bounds;
        if (b.Width <= 0 || b.Height <= 0) return false;
        var m = ComputeViewportMatrix(editor, b);
        var mapped = m.MapRect(editor.ImageRectToContent(imageRect));
        dipRect = new Rect(mapped.Left, mapped.Top, Math.Max(0, mapped.Width), Math.Max(0, mapped.Height));
        dipScale = m.ScaleX;
        return true;
    }

    // ---- Pointer ----

    private SKPoint? DipToImage(Point dip)
    {
        var editor = Editor;
        if (editor is null) return null;
        var m = ComputeViewportMatrix(editor, Bounds);
        if (!m.TryInvert(out var inv)) return null;
        // 矩阵在内容空间；反变换后经 ContentToImage 回到 image 空间
        var content = inv.MapPoint(new SKPoint((float)dip.X, (float)dip.Y));
        return editor.ContentToImage(content);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var editor = Editor;
        if (editor is null) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        var img = DipToImage(e.GetPosition(this));
        if (img is null) return;

        Focus();

        // B-4：crop 激活时先试手柄命中——命中即进入 reshape 拖拽（不新建框）；
        // 已在拖拽中则忽略二次 press，防 _cropReshapeOriginal 被覆写成中间态（review M1）
        if (editor.CurrentTool == ToolKind.Crop && _cropHandleDrag is null &&
            editor.CompositionState.CropRect is { } cropRect)
        {
            var handleRadius = DipToImageDistance(ComputeViewportMatrix(editor, Bounds), HandleHitRadiusDip);
            if (CropReshape.HitTest(cropRect, img.Value, handleRadius) is { } kind)
            {
                _cropHandleDrag = kind;
                _cropReshapeOriginal = cropRect;
                e.Pointer.Capture(this);
                InvalidateVisual();
                e.Handled = true;
                return;
            }
        }

        // 双击 + Select 工具 + 命中 TextAnnotation → 进入内联编辑
        if (e.ClickCount == 2 && editor.CurrentTool == ToolKind.Select)
        {
            if (TryFindTextAt(editor, img.Value) is TextAnnotation ta)
            {
                editor.Selection = ta;
                BeginTextEdit(editor, ta, isNewlyCreated: false);
                e.Handled = true;
                return;
            }
        }

        // SelectTool 需要在 image-pixel 空间的 slop：由当前视口 scale 换算
        var matrix = ComputeViewportMatrix(editor, Bounds);
        var handleRadiusImg = DipToImageDistance(matrix, HandleHitRadiusDip);
        var bodySlopImg = DipToImageDistance(matrix, BodyHitSlopDip);

        _activeTool = CreateToolWithScale(editor, handleRadiusImg, bodySlopImg);
        _activeTool.OnPointerDown(img.Value, editor.CurrentStyle);
        e.Pointer.Capture(this);
        InvalidateVisual();
        e.Handled = true;
    }

    private static TextAnnotation? TryFindTextAt(EditorViewModel editor, SKPoint imagePoint)
    {
        // 逆序（栈顶优先）
        var annotations = editor.Document.Annotations;
        for (int i = annotations.Count - 1; i >= 0; i--)
        {
            if (annotations[i] is TextAnnotation t && t.HitTest(imagePoint, slop: 4f))
                return t;
        }
        return null;
    }

    private static IAnnotationTool CreateToolWithScale(EditorViewModel editor, float handleRadiusImg, float bodySlopImg)
    {
        // 只有 SelectTool 需要 scale-aware 参数；其他工具走 editor.CreateActiveTool。
        if (editor.CurrentTool == ToolKind.Select)
        {
            return new SelectTool(
                getSelection: () => editor.Selection,
                setSelection: a => editor.Selection = a,
                history: editor.History,
                annotations: editor.Document.Annotations,
                document: editor.Document,
                bodyHitSlop: bodySlopImg,
                handleHitRadius: handleRadiusImg);
        }
        return editor.CreateActiveTool();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        // B-4：reshape 拖拽——每步实时写回 CropRect（比例吸附在 Core DragTo 内）
        if (_cropHandleDrag is { } dragKind)
        {
            var reshapeEditor = Editor;
            var reshapeImg = DipToImage(e.GetPosition(this));
            if (reshapeEditor is not null && reshapeImg is { } p && reshapeEditor.CompositionState.CropRect is { } current)
            {
                var (rw, rh) = reshapeEditor.PendingCropRatio;
                reshapeEditor.ApplyCrop(CropReshape.DragTo(current, dragKind, p,
                    new SKRect(0, 0, reshapeEditor.Document.Width, reshapeEditor.Document.Height), rw, rh));
                InvalidateVisual();
                e.Handled = true;
            }
            return;
        }

        if (_activeTool is null) return;
        var img = DipToImage(e.GetPosition(this));
        if (img is null) return;

        // 角度吸附（Flameshot drawMoveWithAdjustment）：Line/Arrow Ctrl=八方向、Shift=水平垂直；
        // Rectangle/Ellipse Ctrl=对角（正方/正圆）。预览不落库，直接改预览终点。
        if ((e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Shift)) != 0)
        {
            var snapped = SnapPreviewPoint(_activeTool, img.Value, e.KeyModifiers);
            if (snapped is { } s) { _activeTool.OnPointerMove(s); InvalidateVisual(); e.Handled = true; return; }
        }

        _activeTool.OnPointerMove(img.Value);
        InvalidateVisual();
    }

    /// <summary>
    /// 按修饰键吸附拖拽终点；不适用吸附的工具/状态返回 null（走普通路径）。
    /// 拖拽起点从预览标注上反推（两点类 = Start；矩形/椭圆 = 预览包围盒角点）。
    /// </summary>
    private static SKPoint? SnapPreviewPoint(IAnnotationTool tool, SKPoint cursor, KeyModifiers mods)
    {
        SKPoint from;
        switch (tool)
        {
            case LineTool when tool.Preview is LineAnnotation l:
                from = l.Start;
                break;
            case ArrowStraightTool when tool.Preview is ArrowStraightAnnotation a:
                from = a.Start;
                break;
            case ArrowCurvedTool when tool.Preview is ArrowCurvedAnnotation c:
                from = c.Start;
                break;
            case RectangleTool when tool.Preview is RectangleAnnotation r:
                from = new SKPoint(r.BoundingBox.Left, r.BoundingBox.Top);
                break;
            case EllipseTool when tool.Preview is EllipseAnnotation el:
                from = new SKPoint(el.BoundingBox.Left, el.BoundingBox.Top);
                break;
            default:
                return null;
        }

        var dx = cursor.X - from.X;
        var dy = cursor.Y - from.Y;
        var snapped = tool switch
        {
            LineTool or ArrowStraightTool or ArrowCurvedTool
                => (mods & KeyModifiers.Control) != 0 ? AngleSnap.Snap45(dx, dy)
                 : (mods & KeyModifiers.Shift) != 0 ? AngleSnap.SnapOrtho(dx, dy)
                 : (SKPoint?)null,
            RectangleTool or EllipseTool
                => (mods & KeyModifiers.Control) != 0 ? AngleSnap.SnapDiagonal(dx, dy) : (SKPoint?)null,
            _ => null,
        };
        return snapped is { } s ? new SKPoint(from.X + s.X, from.Y + s.Y) : null;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        var editor = Editor;

        // B-4：reshape 提交——move 已实时写回，up 点再精确收尾一次（笔类输入 up 可能与末次 move 不同）
        if (_cropHandleDrag is { } dragKind)
        {
            _cropHandleDrag = null;
            e.Pointer.Capture(null);
            if (editor is not null)
            {
                var up = DipToImage(e.GetPosition(this));
                if (up is { } p && editor.CompositionState.CropRect is { } current)
                {
                    var (rw, rh) = editor.PendingCropRatio;
                    editor.ApplyCrop(CropReshape.DragTo(current, dragKind, p,
                        new SKRect(0, 0, editor.Document.Width, editor.Document.Height), rw, rh));
                }
            }
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (_activeTool is null || editor is null) return;

        var img = DipToImage(e.GetPosition(this));
        var committed = img is not null ? _activeTool.OnPointerUp(img.Value) : null;

        // M6d: CropTool 不产出标注——拖拽结果写入 CompositionState.CropRect（含比例吸附）
        if (_activeTool is CropTool cropTool)
        {
            _activeTool = null;
            e.Pointer.Capture(null);
            if (cropTool.Committed is { } rect)
            {
                var (rw, rh) = editor.PendingCropRatio;
                editor.ApplyCrop(rw > 0 && rh > 0
                    ? CropRects.SnapToRatio(rect, rw, rh, new SKRect(0, 0, editor.Document.Width, editor.Document.Height))
                    : rect);
            }
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        _activeTool = null;
        e.Pointer.Capture(null);

        if (committed is not null)
        {
            editor.ExecuteAdd(committed);
            // TextTool 新落点：立即进入内联编辑
            if (committed is TextAnnotation ta)
                BeginTextEdit(editor, ta, isNewlyCreated: true);
        }

        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        CancelCropReshape();
        _activeTool?.Cancel();
        _activeTool = null;
        InvalidateVisual();
    }

    /// <summary>B-4：放弃 reshape 拖拽，恢复拖拽起始框（Esc / 捕获丢失共用）。</summary>
    private void CancelCropReshape()
    {
        if (_cropHandleDrag is null) return;
        _cropHandleDrag = null;
        Editor?.ApplyCrop(_cropReshapeOriginal);
        InvalidateVisual();
    }

    /// <summary>
    /// Ctrl+滚轮：
    /// - 选中 Mosaic/Blur 时调效果强度（BlockSize 4..48 / Sigma 2..40），不动 CurrentStyle；
    /// - 否则调整当前 <see cref="EditorViewModel.CurrentStyle"/> 的描边宽度（1..16），
    ///   有 Selection 时同步写回 Selection.Style + <see cref="AnnotationDocument.NotifyMutated"/>。
    /// 样式改动不进 Undo（延续 M4c 决定）。
    /// </summary>
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var editor = Editor;
        if (editor is null) return;
        if ((e.KeyModifiers & KeyModifiers.Control) == 0) return;

        // M5：效果标注优先——调强度而非描边宽
        if (editor.Selection is MosaicAnnotation mosaic)
        {
            var nextBlock = (int)StrokeWheelHelper.Step(mosaic.BlockSize, e.Delta.Y,
                MosaicAnnotation.MinBlockSize, MosaicAnnotation.MaxBlockSize);
            if (nextBlock != mosaic.BlockSize)
            {
                mosaic.BlockSize = nextBlock;
                editor.Document.NotifyMutated();
            }
            e.Handled = true;
            return;
        }

        // G-3（flameshot circlecount）：选中 Number 时 Ctrl+滚轮改编号值（1..999）
        if (editor.Selection is NumberAnnotation number)
        {
            var nextNumber = (int)StrokeWheelHelper.Step(number.Number, e.Delta.Y, 1, 999);
            if (nextNumber != number.Number)
            {
                number.SetNumber(nextNumber);
                editor.Document.NotifyMutated();
            }
            e.Handled = true;
            return;
        }

        if (editor.Selection is BlurAnnotation blur)
        {
            var nextSigma = StrokeWheelHelper.Step(blur.Sigma, e.Delta.Y,
                BlurAnnotation.MinSigma, BlurAnnotation.MaxSigma);
            if (Math.Abs(nextSigma - blur.Sigma) > 0.01f)
            {
                blur.Sigma = nextSigma;
                editor.Document.NotifyMutated();
            }
            e.Handled = true;
            return;
        }

        var current = editor.CurrentStyle.StrokeWidth;
        var next = StrokeWheelHelper.Step(current, e.Delta.Y);
        if (Math.Abs(next - current) < 0.5f)
        {
            e.Handled = true;
            return;
        }
        editor.CurrentStyle = editor.CurrentStyle with { StrokeWidth = next };
        if (editor.Selection is { } sel)
        {
            sel.Style = sel.Style with { StrokeWidth = next };
            editor.Document.NotifyMutated();
        }
        e.Handled = true;
    }

    /// <summary>拖拽预览（image 空间）；Crop 工具的预览矩形不走标注渲染，overlay 分支用。</summary>
    private SKRect? CropPreviewRect => _activeTool as CropTool is { } t ? t.PreviewRect : null;

    /// <summary>
    /// 计算 TextBox 需要覆盖的 DIP 矩形并请求 EditorViewModel 进入编辑态。
    /// 编辑用的矩形：以 Text.Position（baseline）向上一个 ascent 高，宽度按当前文本 measure。
    /// </summary>
    private void BeginTextEdit(EditorViewModel editor, TextAnnotation text, bool isNewlyCreated)
    {
        if (!TryComputeTextEditBounds(text, out var dipRect, out var fontDip))
        {
            // 没有视口就先记一个占位；ViewportInvalidated 会再算一次
            dipRect = new Rect(0, 0, 120, 30);
            fontDip = 24;
        }
        editor.BeginTextEdit(text, dipRect, fontDip, isNewlyCreated);
    }

    private bool TryComputeTextEditBounds(TextAnnotation text, out Rect dipRect, out double fontDip)
    {
        dipRect = default;
        fontDip = 24d;
        if (!TryMapImageRectToDip(text.BoundingBox, out var mapped, out var scale)) return false;
        // 至少 60px 宽，方便输入
        var w = Math.Max(mapped.Width, 60);
        var h = Math.Max(mapped.Height, 24);
        dipRect = new Rect(mapped.X - 2, mapped.Y - 2, w + 12, h + 4);
        fontDip = text.FontSize * scale;
        return true;
    }

    // ---- Custom draw operation ----

    private sealed class DrawOp : ICustomDrawOperation
    {
        private static readonly SKColor SelectionOutlineColor = new(0xFF, 0xFF, 0xFF);
        private static readonly SKColor HandleFillColor       = new(0xFF, 0xFF, 0xFF);
        private static readonly SKColor HandleStrokeColor     = new(0x00, 0x00, 0x00);

        private readonly AnnotationDocument _doc;
        private readonly Prtsc.Core.Editor.EditorState _state;
        private readonly AnnotationBase? _preview;
        private readonly AnnotationBase? _selection;
        private readonly AnnotationBase? _editingText;
        private readonly SKMatrix _matrix;
        private readonly SKRect? _cropPreviewRect;
        private readonly bool _cropToolActive;
        private readonly SKSize _contentSize;

        public DrawOp(Rect bounds, AnnotationDocument doc, Prtsc.Core.Editor.EditorState state,
                      AnnotationBase? preview, AnnotationBase? selection, AnnotationBase? editingText,
                      SKMatrix matrix, SKRect? cropPreviewRect = null, SKSize contentSize = default,
                      bool cropToolActive = false)
        {
            Bounds = bounds;
            _doc = doc;
            _state = state;
            _preview = preview;
            _selection = selection;
            _editingText = editingText;
            _matrix = matrix;
            _cropPreviewRect = cropPreviewRect;
            _cropToolActive = cropToolActive;
            if (contentSize.Width > 0 && contentSize.Height > 0)
            {
                _contentSize = contentSize;
            }
            else
            {
                var measured = Composition.Measure(doc, state);
                _contentSize = new SKSize(measured.Width, measured.Height);
            }
        }

        public Rect Bounds { get; }

        public void Dispose() { }

        public bool Equals(ICustomDrawOperation? other) => false;

        public bool HitTest(Point p) => Bounds.Contains(p);

        public void Render(ImmediateDrawingContext context)
        {
            var lease = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
            if (lease is null) return;
            using var api = lease.Lease();
            var canvas = api.SkCanvas;

            // 背景（DIP 空间，内容区之外也可见的暗板）
            using (var bg = new SKPaint { Color = new SKColor(0x1A, 0x1A, 0x1A) })
            {
                canvas.DrawRect(0, 0, (float)Bounds.Width, (float)Bounds.Height, bg);
            }

            // 无背景 = 导出后内容区是透明的（padding 环 / 圆角外角 / 透明源图）。
            // 预览用棋盘格示意透明区域，否则那些地方与暗色衬底同色，用户看不出画布边界，
            // 导出后才发现图边上多了一圈透明。占位格只是编辑器 chrome，永不进导出。
            if (!_state.HasBackground)
            {
                TransparencyChecker.Draw(canvas, ContentDipRect());
            }

            var restore = canvas.Save();
            canvas.Concat(in _matrix);

            // 内容空间：完整合成管线（背景板/阴影/圆角裁剪/边框/内衬 + 文档内容）。
            // 标注坐标是 image 空间——管线内部平移 (Padding, Padding)，此处无需换算。
            Composition.RenderScene(canvas, _doc, _state, _preview, _editingText);

            canvas.RestoreToCount(restore);

            // DIP 空间：画选中框与手柄（不随视口缩放变形；编辑中的 Text 不画）
            if (_selection is not null && !ReferenceEquals(_selection, _editingText))
                DrawSelectionOverlay(canvas, _selection);

            // M6d/B-4: 裁剪 overlay——框外暗遮罩 + 白框（编辑器 chrome，永不导出）。
            // B-4：crop 工具激活且已有应用过的裁剪框（无预览拖拽）→ overlay 常驻 + 8 手柄供拖拽。
            SKRect? cropOverlayRect = _cropPreviewRect;
            var showCropHandles = false;
            if (cropOverlayRect is null && _cropToolActive && _state.CropRect is { } applied)
            {
                cropOverlayRect = applied;
                showCropHandles = true;
            }
            if (cropOverlayRect is { } cropRect)
            {
                DrawCropOverlay(canvas, cropRect);
                if (showCropHandles)
                    DrawCropHandles(canvas, cropRect);
            }
        }

        /// <summary>内容区（= 导出图尺寸）在 DIP 空间的矩形，用于铺透明占位格。</summary>
        private SKRect ContentDipRect()
            => _matrix.MapRect(new SKRect(0, 0, _contentSize.Width, _contentSize.Height));

        /// <summary>与 EditorViewModel.ImageRectToContent 同式（DrawOp 持的是 state 快照）。</summary>
        private SKRect ImageRectToContent(SKRect imageRect)
        {
            var s = _state;
            var o = s.CropRect?.Location ?? default;
            return new SKRect(
                imageRect.Left - o.X + s.Padding, imageRect.Top - o.Y + s.Padding,
                imageRect.Right - o.X + s.Padding, imageRect.Bottom - o.Y + s.Padding);
        }

        /// <summary>裁剪 overlay：全内容暗遮罩，裁剪框内清透 + 白色描边。</summary>
        private void DrawCropOverlay(SKCanvas canvas, SKRect imageCropRect)
        {
            var content = _matrix.MapRect(ImageRectToContent(imageCropRect));

            var restore = canvas.Save();
            canvas.ResetMatrix();

            using (var dim = new SKPaint { Color = new SKColor(0, 0, 0, 0x80) })
            {
                canvas.DrawRect(0, 0, (float)Bounds.Width, (float)Bounds.Height, dim);
            }
            using (var clear = new SKPaint { BlendMode = SKBlendMode.Clear })
            {
                canvas.DrawRect(content.Left, content.Top, content.Width, content.Height, clear);
            }
            using (var frame = new SKPaint
            {
                Color = SKColors.White,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1.5f,
                IsAntialias = true,
            })
            {
                canvas.DrawRect(content.Left, content.Top, content.Width, content.Height, frame);
            }

            canvas.RestoreToCount(restore);
        }

        /// <summary>B-4：应用态裁剪框的 8 手柄（BoxHandles 拓扑 → DIP 方块，不随视口缩放变形）。</summary>
        private void DrawCropHandles(SKCanvas canvas, SKRect imageCropRect)
        {
            using var fillPaint = new SKPaint
            {
                Color = HandleFillColor,
                Style = SKPaintStyle.Fill,
                IsAntialias = false,
            };
            using var strokePaint = new SKPaint
            {
                Color = HandleStrokeColor,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1f,
                IsAntialias = false,
            };

            var half = HandleVisualSizeDip * 0.5f;
            foreach (var h in BoxHandles.GetHandles(imageCropRect))
            {
                var pt = MapImageToDip(h.ImagePos);
                var r = new SKRect(pt.X - half, pt.Y - half, pt.X + half, pt.Y + half);
                canvas.DrawRect(r, fillPaint);
                canvas.DrawRect(r, strokePaint);
            }
        }

        /// <summary>image 空间点 → 内容空间（含 crop 原点 + padding）→ DIP。</summary>
        private SKPoint MapImageToDip(SKPoint imagePoint)
        {
            var s = _state;
            var o = s.CropRect?.Location ?? default;
            return _matrix.MapPoint(new SKPoint(imagePoint.X - o.X + s.Padding, imagePoint.Y - o.Y + s.Padding));
        }

        private void DrawSelectionOverlay(SKCanvas canvas, AnnotationBase selection)
        {
            // BoundingBox 从 image 空间经内容偏移（crop 原点 + padding）映射到 DIP
            var box = _matrix.MapRect(ImageRectToContent(selection.BoundingBox));

            using (var outline = new SKPaint
            {
                Color = SelectionOutlineColor,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1f,
                IsAntialias = true,
                PathEffect = SKPathEffect.CreateDash(new float[] { 4f, 3f }, 0f),
            })
            {
                canvas.DrawRect(box, outline);
            }

            using var fillPaint = new SKPaint
            {
                Color = HandleFillColor,
                Style = SKPaintStyle.Fill,
                IsAntialias = false,
            };
            using var strokePaint = new SKPaint
            {
                Color = HandleStrokeColor,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1f,
                IsAntialias = false,
            };

            var half = HandleVisualSizeDip * 0.5f;
            foreach (var h in selection.GetHandles())
            {
                var pt = MapImageToDip(h.ImagePos);
                var r = new SKRect(pt.X - half, pt.Y - half, pt.X + half, pt.Y + half);
                canvas.DrawRect(r, fillPaint);
                canvas.DrawRect(r, strokePaint);
            }
        }
    }
}
