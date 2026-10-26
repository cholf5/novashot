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

    public SkiaCanvas()
    {
        Focusable = true;
        ClipToBounds = true;
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
        InvalidateVisual();
        ViewportInvalidated?.Invoke(this, EventArgs.Empty);
    }

    private void OnDocumentChanged(object? sender, EventArgs e) => InvalidateVisual();

    private void OnEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Tool / Style / Selection / EditingText 变化都可能影响渲染
        if (e.PropertyName is nameof(EditorViewModel.CurrentTool)
            or nameof(EditorViewModel.CurrentStyle)
            or nameof(EditorViewModel.Selection)
            or nameof(EditorViewModel.EditingText))
        {
            InvalidateVisual();
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
            _activeTool?.Preview,
            editor.Selection,
            editor.EditingText,
            ComputeViewportMatrix(editor, bounds)));
    }

    /// <summary>
    /// 计算 image-pixel → DIP 的仿射变换：uniform 缩放 + 居中平移。
    /// image 坐标 (0,0)..(W,H) 会被映射到控件内部一块居中的矩形。
    /// </summary>
    private static SKMatrix ComputeViewportMatrix(EditorViewModel editor, Rect bounds)
    {
        var w = editor.ImageWidth;
        var h = editor.ImageHeight;
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
        var mapped = m.MapRect(imageRect);
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
        return inv.MapPoint(new SKPoint((float)dip.X, (float)dip.Y));
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
        if (_activeTool is null) return;
        var img = DipToImage(e.GetPosition(this));
        if (img is null) return;
        _activeTool.OnPointerMove(img.Value);
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        var editor = Editor;
        if (_activeTool is null || editor is null) return;

        var img = DipToImage(e.GetPosition(this));
        var committed = img is not null ? _activeTool.OnPointerUp(img.Value) : null;
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
        _activeTool?.Cancel();
        _activeTool = null;
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
        private readonly AnnotationBase? _preview;
        private readonly AnnotationBase? _selection;
        private readonly AnnotationBase? _editingText;
        private readonly SKMatrix _matrix;

        public DrawOp(Rect bounds, AnnotationDocument doc, AnnotationBase? preview,
                      AnnotationBase? selection, AnnotationBase? editingText, SKMatrix matrix)
        {
            Bounds = bounds;
            _doc = doc;
            _preview = preview;
            _selection = selection;
            _editingText = editingText;
            _matrix = matrix;
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

            // 背景
            using (var bg = new SKPaint { Color = new SKColor(0x1A, 0x1A, 0x1A) })
            {
                canvas.DrawRect(0, 0, (float)Bounds.Width, (float)Bounds.Height, bg);
            }

            var restore = canvas.Save();
            canvas.Concat(in _matrix);

            // image-pixel 空间：画图像 + 标注（跳过正在内联编辑的 Text） + 预览
            canvas.DrawBitmap(_doc.Source, 0, 0);
            foreach (var a in _doc.Annotations)
            {
                if (ReferenceEquals(a, _editingText)) continue;
                a.Render(canvas);
            }
            _preview?.Render(canvas);

            canvas.RestoreToCount(restore);

            // DIP 空间：画选中框与手柄（不随视口缩放变形；编辑中的 Text 不画）
            if (_selection is not null && !ReferenceEquals(_selection, _editingText))
                DrawSelectionOverlay(canvas, _selection);
        }

        private void DrawSelectionOverlay(SKCanvas canvas, AnnotationBase selection)
        {
            // BoundingBox 从像素映射到 DIP
            var box = _matrix.MapRect(selection.BoundingBox);

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
                var pt = _matrix.MapPoint(h.ImagePos);
                var r = new SKRect(pt.X - half, pt.Y - half, pt.X + half, pt.Y + half);
                canvas.DrawRect(r, fillPaint);
                canvas.DrawRect(r, strokePaint);
            }
        }
    }
}
