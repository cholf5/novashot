namespace Prtsc.Core.Annotation.Editing;

/// <summary>
/// 标注可拖手柄的类型标签。
///
/// - 矩形/椭圆/聚光灯用 8 个盒子手柄 (<see cref="TopLeft"/>..<see cref="Left"/>)
/// - 直线/箭头用 2 个端点手柄 (<see cref="Start"/>/<see cref="End"/>)
/// - 文本/编号用 1 个 <see cref="Center"/> 手柄整体拖动
/// </summary>
public enum HandleKind
{
    TopLeft,
    Top,
    TopRight,
    Right,
    BottomRight,
    Bottom,
    BottomLeft,
    Left,
    Start,
    End,
    Center,
}
