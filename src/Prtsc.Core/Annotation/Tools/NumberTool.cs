using SkiaSharp;
using Prtsc.Core.Annotation.Model;

namespace Prtsc.Core.Annotation.Tools;

/// <summary>
/// 编号工具：单击（或拖动定位）落一枚圆形序号。号码在 <see cref="OnPointerUp"/> 时通过
/// 构造函数注入的 <c>nextNumberProvider</c> 实时求值 —— 这样能立刻反映删除/Undo（M4d 起）。
///
/// <c>Preview</c> 也用同一 provider，鼠标悬停时显示的号码始终等于"如果此刻抬手会落几"。
/// </summary>
public sealed class NumberTool : IAnnotationTool
{
    private readonly Func<int> _nextNumberProvider;
    private SKPoint _pos;
    private AnnotationStyle? _style;
    private bool _armed;

    public AnnotationBase? Preview { get; private set; }

    public NumberTool(Func<int> nextNumberProvider)
    {
        _nextNumberProvider = nextNumberProvider ?? throw new ArgumentNullException(nameof(nextNumberProvider));
    }

    public void OnPointerDown(SKPoint imagePoint, AnnotationStyle style)
    {
        _pos = imagePoint;
        _style = style;
        _armed = true;
        Preview = new NumberAnnotation(imagePoint, _nextNumberProvider(), style);
    }

    public void OnPointerMove(SKPoint imagePoint)
    {
        if (!_armed || _style is null) return;
        _pos = imagePoint;
        Preview = new NumberAnnotation(imagePoint, _nextNumberProvider(), _style);
    }

    public AnnotationBase? OnPointerUp(SKPoint imagePoint)
    {
        if (!_armed || _style is null) { _armed = false; Preview = null; return null; }
        _armed = false;
        Preview = null;
        var n = _nextNumberProvider();
        return new NumberAnnotation(imagePoint, n, _style);
    }

    public void Cancel()
    {
        _armed = false;
        Preview = null;
    }
}
