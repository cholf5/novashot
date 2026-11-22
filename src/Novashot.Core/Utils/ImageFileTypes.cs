namespace Novashot.Core.Utils;

/// <summary>
/// I-1 导入图片的「常用图片」扩展名面（winshot 对齐：png/jpg/jpeg/gif/bmp/webp）。
/// Ctrl+O 文件选择器 Patterns 与剪贴板 HDROP 粘贴（G-11）共用同一份真值。
/// 不在册的扩展名不在此拦截——允许任意文件进解码路径，解不解得动由 Skia 说了算
/// （失败走 <c>Status.UnsupportedFormat</c> 既有提示）。
/// </summary>
public static class ImageFileTypes
{
    /// <summary>常用图片扩展名（小写、含点）。顺序仅展示用；判定走集合包含。</summary>
    public static IReadOnlyList<string> Extensions { get; } = new[]
        { ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif" };

    /// <summary>文件选择器过滤 Patterns（与 <see cref="Extensions"/> 一一对应）。</summary>
    public static IReadOnlyList<string> Patterns { get; } =
        Extensions.Select(e => "*" + e).ToArray();

    /// <summary>扩展名（含点，如 ".png"）是否属于常用图片面（大小写不敏感）。</summary>
    public static bool IsSupported(string? extension) =>
        extension is not null && Extensions.Contains(extension.ToLowerInvariant());
}
