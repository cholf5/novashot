using System.Net;

namespace Prtsc.Core.Tests;

/// <summary>
/// 可注入 HttpClient 的假 handler：记录请求与方法体，按委托回应。
/// 上传器测试统一用它断言"实际发出的 HTTP"。
/// </summary>
public sealed class FakeHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

    public List<HttpRequestMessage> Requests { get; } = [];
    public List<byte[]> Bodies { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        if (request.Content is not null)
        {
            using var buffer = new MemoryStream();
            await request.Content.CopyToAsync(buffer, cancellationToken);
            Bodies.Add(buffer.ToArray());
        }
        else
        {
            Bodies.Add([]);
        }
        return _responder(request);
    }
}
