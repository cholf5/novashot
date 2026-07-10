using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Prtsc.App.ViewModels;
using Prtsc.Core.Annotation;
using Prtsc.Core.Annotation.Tools;
using SkiaSharp;

namespace Prtsc.App.Controls;

/// <summary>
/// 编辑器主画布：通过 <see cref="ICustomDrawOperation"/> 拿到 Avalonia Skia 后端的 <see cref="SKCanvas"/>
/// 直接绘制源图 + 标注 + 拖拽预览。
///
/// 坐标：内部所有 annotation 均在<b>源图像素空间</b>；控件通过 uniform fit-to-bounds 变换绘制。
/// Pointer 事件在 DIP 空间到达，经反变换后送给 <see cref="IAnnotationTool"/>。
///
/// M4a：只做全量重绘 + 单次拖拽 + Rectangle 提交路径。
/// </summary>
public sealed class SkiaCanvas : Control
{
    public static readonly StyledProperty<EditorViewModel?> EditorProperty =
        AvaloniaProperty.Register<SkiaCanvas, EditorViewModel?>(nameof(Editor));

    public EditorViewModel? Editor
    {
        get => GetValue(EditorProperty);
        set => SetValue(EditorProperty, value);
    }

    private IAnnotationTool? _activeTool;
    private EditorViewModel? _subscribedEditor;

    public SkiaCanvas()
    {
        Focusable = true;
        // 不透明背景默认；具体清屏在 DrawOp 里做
        ClipToBounds = true;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == EditorProperty)
        {
            OnEditorChanged(change.GetOldValue<EditorViewModel?>(), change.GetNewValue<EditorViewModel?>());
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
    }

    private void OnDocumentChanged(object? sender, EventArgs e) => InvalidateVisual();

    private void OnEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Tool / Style 变化都可能影响拖拽预览外观
        if (e.PropertyName is nameof(EditorViewModel.CurrentTool)
            or nameof(EditorViewModel.CurrentStyle))
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

    // ---- Pointer ----

    /// <summary>DIP 点 → image-pixel 点。若无 Editor / 变换不可逆返回 <c>null</c>。</summary>
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
        _activeTool = editor.CreateActiveTool();
        _activeTool.OnPointerDown(img.Value, editor.CurrentStyle);
        e.Pointer.Capture(this);
        InvalidateVisual();
        e.Handled = true;
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
            editor.ExecuteAdd(committed);

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

    // ---- Custom draw operation ----

    private sealed class DrawOp : ICustomDrawOperation
    {
        private readonly AnnotationDocument _doc;
        private readonly Prtsc.Core.Annotation.Model.AnnotationBase? _preview;
        private readonly SKMatrix _matrix;

        public DrawOp(Rect bounds, AnnotationDocument doc,
                      Prtsc.Core.Annotation.Model.AnnotationBase? preview, SKMatrix matrix)
        {
            Bounds = bounds;
            _doc = doc;
            _preview = preview;
            _matrix = matrix;
        }

        public Rect Bounds { get; }

        public void Dispose() { }

        public bool Equals(ICustomDrawOperation? other) => false;

        public bool HitTest(Point p) => Bounds.Contains(p);

        public void Render(ImmediateDrawingContext context)
        {
            var lease = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
            if (lease is null) return;   // 非 Skia 后端（不应发生）
            using var api = lease.Lease();
            var canvas = api.SkCanvas;

            // 编辑区背景（略深，方便看图像边界）
            using (var bg = new SKPaint { Color = new SKColor(0x1A, 0x1A, 0x1A) })
            {
                canvas.DrawRect(0, 0, (float)Bounds.Width, (float)Bounds.Height, bg);
            }

            var restore = canvas.Save();
            canvas.Concat(in _matrix);

            // 图像
            canvas.DrawBitmap(_doc.Source, 0, 0);

            // 已提交标注
            foreach (var a in _doc.Annotations)
                a.Render(canvas);

            // 拖拽预览
            _preview?.Render(canvas);

            canvas.RestoreToCount(restore);
        }
    }
}
