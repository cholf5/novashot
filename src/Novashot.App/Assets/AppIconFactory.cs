using Avalonia.Controls;
using Avalonia.Media.Imaging;

namespace Novashot.App.Assets;

/// <summary>
/// 应用图标（窗口/托盘）：统一加载嵌入资产 Novashot.App.app-icon.png（256px 管线产物，
/// scripts/make-app-icon.py 生成），与安装器（vpk --icon）/exe（ApplicationIcon）同一出处，
/// 不再程序化绘制占位符。
/// </summary>
public static class AppIconFactory
{
    private const string ResourceName = "Novashot.App.app-icon.png";

    public static Bitmap CreateIcon()
    {
        var asm = typeof(AppIconFactory).Assembly;
        using var stream = asm.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"missing embedded resource: {ResourceName}");
        return new Bitmap(stream);
    }

    public static WindowIcon CreateWindowIcon() => new(CreateIcon());
}
