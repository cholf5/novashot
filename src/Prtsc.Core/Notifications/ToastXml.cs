using System.Security;

namespace Prtsc.Core.Notifications;

/// <summary>
/// N-1：Windows Toast XML 拼装纯函数（从 Platform.Windows 服务里抽到 Core，纯单测钉死）。
/// 产出喂给 WinRT <c>Windows.Data.Xml.Dom.XmlDocument.LoadXml</c>：
/// ToastGeneric 模板（首行 title、次行 message）；<paramref name="filePath"/> 非空时写入
/// launch 属性，toast 激活回调经 <c>ToastActivatedEventArgs.Arguments</c> 拿回原值
/// （WinRT XML DOM 已做实体解码）供 explorer /select 用。
/// 所有文本与属性值过 <see cref="SecurityElement.Escape"/>：路径含 <c>&amp; &lt; &gt; 引号</c> 不破 XML，
/// 解析回读与原值逐字相等。
/// </summary>
public static class ToastXml
{
    public static string Build(string title, string message, string? filePath = null)
    {
        var launch = string.IsNullOrWhiteSpace(filePath)
            ? string.Empty
            : $" launch=\"{Escape(filePath!)}\"";
        return "<toast" + launch + ">"
            + "<visual><binding template=\"ToastGeneric\">"
            + "<text>" + Escape(title) + "</text>"
            + "<text>" + Escape(message) + "</text>"
            + "</binding></visual></toast>";
    }

    private static string Escape(string value) => SecurityElement.Escape(value) ?? string.Empty;
}
