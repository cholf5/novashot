using Prtsc.Core.Annotation.Editing;
using SkiaSharp;

namespace Prtsc.Core.Annotation.Model;

/// <summary>
/// 自由手绘标注基类（Pencil / Highlighter 共享）：一条按时间顺序的折线点列。
///
/// 手绘不做逐点 reshape（Flameshot 也没有）——<see cref="GetHandles"/> 返回空，
/// Select 工具下只能整体移动 / 删除。点的记录由工具层做最小步长去抖（见 FreehandToolBase）。
/// </summary>
public abstract class FreehandAnnotationBase : AnnotationBase
{
    private readonly List<SKPoint> _points;

    /// <summary>只读点列（image-pixel 空间）。构造后通过 <see cref="AppendPoint"/> 追加。</summary>
    public IReadOnlyList<SKPoint> Points => _points;

    /// <summary>
    /// 命中测试与 BoundingBox 用的有效线宽。Highlighter 覆盖它加上宽度加成。
    /// </summary>
    protected virtual float EffectiveStrokeWidth => Style.StrokeWidth;

    protected FreehandAnnotationBase(IEnumerable<SKPoint> points, AnnotationStyle style) : base(style)
    {
        ArgumentNullException.ThrowIfNull(points);
        _points = new List<SKPoint>(points);
    }

    /// <summary>
    /// 追加一个轨迹点（工具拖拽预览用）。与上一点重合时仍追加——去抖由工具层负责，
    /// 模型层保持"记录即事实"，方便测试与未来 CLI 重放。
    /// </summary>
    public void AppendPoint(SKPoint point) => _points.Add(point);

    public override SKRect BoundingBox
    {
        get
        {
            if (_points.Count == 0) return SKRect.Empty;
            var pad = EffectiveStrokeWidth * 0.5f + 1f;
            var l = _points[0].X;
            var t = _points[0].Y;
            var r = _points[0].X;
            var b = _points[0].Y;
            for (int i = 1; i < _points.Count; i++)
            {
                if (_points[i].X < l) l = _points[i].X;
                if (_points[i].Y < t) t = _points[i].Y;
                if (_points[i].X > r) r = _points[i].X;
                if (_points[i].Y > b) b = _points[i].Y;
            }
            return new SKRect(l - pad, t - pad, r + pad, b + pad);
        }
    }

    public override bool HitTest(SKPoint point, float slop)
        => PolylineGeometry.DistanceToPolyline(point, _points) <= EffectiveStrokeWidth * 0.5f + slop;

    /// <summary>手绘无 reshape 手柄：移动靠 body 拖拽（<see cref="MoveBy"/>），删除靠 Delete。</summary>
    public override IReadOnlyList<AnnotationHandle> GetHandles() => Array.Empty<AnnotationHandle>();

    public override void MoveHandle(HandleKind kind, SKPoint newImagePos)
    {
        // 无手柄，无操作
    }

    public override void MoveBy(SKPoint delta)
    {
        for (int i = 0; i < _points.Count; i++)
            _points[i] = new SKPoint(_points[i].X + delta.X, _points[i].Y + delta.Y);
    }

    /// <summary>把点列画成折线 path（Round cap/join 让单点/急转也显示为圆头）。</summary>
    protected SKPath BuildPath()
    {
        var path = new SKPath();
        if (_points.Count == 0) return path;
        path.MoveTo(_points[0]);
        for (int i = 1; i < _points.Count; i++)
            path.LineTo(_points[i]);
        return path;
    }
}
