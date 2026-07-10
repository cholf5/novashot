using SkiaSharp;
using Prtsc.Core.Annotation.Model;

namespace Prtsc.Core.Annotation.Tools;

/// <summary>
/// 编号工具：单击（或拖动定位）落一枚圆形序号。序号从共享的 <see cref="NumberCounter"/> 领取。
/// 序号在 <see cref="OnPointerUp"/> 成功提交时递增；<see cref="Cancel"/> 不领号。
/// </summary>
public sealed class NumberTool : IAnnotationTool
{
    private readonly NumberCounter _counter;
    private SKPoint _pos;
    private AnnotationStyle? _style;
    private bool _armed;

    public AnnotationBase? Preview { get; private set; }

    public NumberTool(NumberCounter counter)
    {
        _counter = counter ?? throw new ArgumentNullException(nameof(counter));
    }

    public void OnPointerDown(SKPoint imagePoint, AnnotationStyle style)
    {
        _pos = imagePoint;
        _style = style;
        _armed = true;
        // preview 用 counter.Peek()，不消耗号码
        Preview = new NumberAnnotation(imagePoint, _counter.Peek(), style);
    }

    public void OnPointerMove(SKPoint imagePoint)
    {
        if (!_armed || _style is null) return;
        _pos = imagePoint;
        Preview = new NumberAnnotation(imagePoint, _counter.Peek(), _style);
    }

    public AnnotationBase? OnPointerUp(SKPoint imagePoint)
    {
        if (!_armed || _style is null) { _armed = false; Preview = null; return null; }
        _armed = false;
        Preview = null;
        var n = _counter.Next(); // 现在领号
        return new NumberAnnotation(imagePoint, n, _style);
    }

    public void Cancel()
    {
        _armed = false;
        Preview = null;
    }
}
