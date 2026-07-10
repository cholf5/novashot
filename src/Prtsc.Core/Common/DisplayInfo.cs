namespace Prtsc.Core.Common;

/// <summary>
/// 单个显示器的描述。物理像素坐标，DPI 独立。
/// </summary>
public sealed record DisplayInfo(
    int Index,
    string DeviceName,
    PixelRect Bounds,          // 物理像素几何（相对虚拟桌面原点）
    PixelRect WorkArea,        // 去掉任务栏
    double ScaleFactor,        // 例如 1.0 / 1.25 / 1.5 / 2.0
    bool IsPrimary);
