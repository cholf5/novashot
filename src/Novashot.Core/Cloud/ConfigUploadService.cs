using Novashot.Core.Config;
using Microsoft.Extensions.Logging;

namespace Novashot.Core.Cloud;

/// <summary>
/// 按 IConfigStore.Current.Cloud.Provider 每次上传时分发（r2 / gdrive / none，大小写不敏感）。
/// 不缓存 per-provider 实例中的 config 快照——每次现读，配置改动即时生效。
/// HttpClient 共享一个静态实例（连接池复用、无 socket 泄漏）。
/// </summary>
public sealed class ConfigUploadService : IUploadService
{
    private readonly IConfigStore _configStore;
    private readonly HttpClient _http;

    public ConfigUploadService(IConfigStore configStore, HttpClient? http = null)
    {
        _configStore = configStore ?? throw new ArgumentNullException(nameof(configStore));
        _http = http ?? CloudHttp.Client;
    }

    public async Task<UploadResult> UploadAsync(UploadRequest request, CancellationToken ct = default)
    {
        var cloud = _configStore.Current.Cloud;
        switch (cloud.Provider?.Trim().ToLowerInvariant())
        {
            case "r2":
                return await new R2Uploader(cloud.R2, _http).UploadAsync(request, ct).ConfigureAwait(false);
            case "gdrive":
                return await new GDriveUploader(cloud.GDrive, _http).UploadAsync(request, ct).ConfigureAwait(false);
            case "none" or null or "":
                return await new NullUploadService().UploadAsync(request, ct).ConfigureAwait(false);
            default:
                return UploadResult.Fail($"未知的云上传渠道：{cloud.Provider}（支持 r2 / gdrive / none）。");
        }
    }
}
