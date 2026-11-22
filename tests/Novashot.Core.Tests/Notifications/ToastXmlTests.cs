using System.Xml.Linq;
using Novashot.Core.Notifications;

namespace Novashot.Core.Tests;

/// <summary>
/// N-1：Toast XML 拼装纯函数（Platform.Windows 服务里的 XML 构造下沉 Core，可测）。
/// 产出必须是 WinRT XmlDocument.LoadXml 能吃的合法 XML：ToastGeneric 双行文本 +
/// 可选 launch 属性（点击 toast → explorer /select 用）。
/// </summary>
public class ToastXmlTests
{
    [Fact]
    public void Build_WithoutPath_Parses_TwoTexts_NoLaunch()
    {
        var xml = ToastXml.Build("Saved", "novashot-001.png");

        var toast = XDocument.Parse(xml).Root;
        Assert.NotNull(toast);
        Assert.Equal("toast", toast!.Name.LocalName);
        Assert.Null(toast.Attribute("launch"));

        var texts = toast.Descendants("text").ToList();
        Assert.Equal(2, texts.Count);
        Assert.Equal("Saved", texts[0].Value);
        Assert.Equal("novashot-001.png", texts[1].Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Build_NullOrBlankPath_NoLaunchAttribute(string? path)
    {
        var toast = XDocument.Parse(ToastXml.Build("t", "m", path)).Root!;
        Assert.Null(toast.Attribute("launch"));
    }

    [Fact]
    public void Build_WithPath_LaunchCarriesPath()
    {
        var path = @"C:\Users\me\Pictures\Novashot\novashot-20261110-094213.png";

        var toast = XDocument.Parse(ToastXml.Build("Saved", path, path)).Root!;

        Assert.Equal(path, toast.Attribute("launch")?.Value);
    }

    [Fact]
    public void Build_XmlSpecialCharacters_AreEscapedAndRoundTrip()
    {
        const string title = "Saved <a> & \"b\" 'c' >";
        const string message = @"C:\dir&sub \<weird>.png";

        var xml = ToastXml.Build(title, message, message);
        var toast = XDocument.Parse(xml).Root!; // 不抛 = 合法 XML

        var texts = toast.Descendants("text").ToList();
        Assert.Equal(title, texts[0].Value);
        Assert.Equal(message, texts[1].Value);
        Assert.Equal(message, toast.Attribute("launch")?.Value);
    }

    [Fact]
    public void Build_UsesToastGenericTemplate()
    {
        var toast = XDocument.Parse(ToastXml.Build("t", "m")).Root!;

        var binding = Assert.Single(toast.Descendants("binding"));
        Assert.Equal("ToastGeneric", binding.Attribute("template")?.Value);
    }
}
