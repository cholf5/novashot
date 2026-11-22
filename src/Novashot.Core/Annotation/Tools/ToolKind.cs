namespace Novashot.Core.Annotation.Tools;

/// <summary>
/// 编辑器工具类型。M4a 只实现 <see cref="Select"/>（占位）与 <see cref="Rectangle"/>；
/// 其余枚举值预留给 M4b (Ellipse/Arrow*/Line/Text/Number/Spotlight) 与 M5 (Pencil 等)。
/// </summary>
public enum ToolKind
{
    Select,
    Rectangle,
    Ellipse,
    ArrowStraight,
    ArrowCurved,
    Line,
    Text,
    Number,
    Spotlight,
    Pencil,
    Highlighter,
    Mosaic,
    Blur,
    Invert,
    Crop,
}
