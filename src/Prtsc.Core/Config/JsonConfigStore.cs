using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Prtsc.Core.Config;

public interface IConfigStore
{
    PrtscConfig Current { get; }
    event EventHandler<PrtscConfig>? Changed;

    Task LoadAsync(CancellationToken ct = default);
    Task SaveAsync(CancellationToken ct = default);
    void Update(Action<PrtscConfig> mutate);

    string ConfigDirectory { get; }
    string ConfigFilePath { get; }
}

/// <summary>
/// JSON 持久化，%AppData%\Prtsc\config.json。写入时先写临时文件再原子替换，避免半写导致下次启动崩溃。
/// </summary>
public sealed class JsonConfigStore : IConfigStore
{
    private readonly ILogger<JsonConfigStore> _logger;
    private readonly SemaphoreSlim _ioLock = new(1, 1);
    private PrtscConfig _current = new();

    public JsonConfigStore(ILogger<JsonConfigStore> logger, string? overrideDirectory = null)
    {
        _logger = logger;
        ConfigDirectory = overrideDirectory ?? DefaultConfigDirectory();
        ConfigFilePath = Path.Combine(ConfigDirectory, "config.json");
    }

    public PrtscConfig Current => _current;
    public string ConfigDirectory { get; }
    public string ConfigFilePath { get; }

    public event EventHandler<PrtscConfig>? Changed;

    public static string DefaultConfigDirectory() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Prtsc");

    public async Task LoadAsync(CancellationToken ct = default)
    {
        await _ioLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(ConfigDirectory);
            if (!File.Exists(ConfigFilePath))
            {
                _current = new PrtscConfig();
                _logger.LogInformation("No config found at {Path}, using defaults", ConfigFilePath);
                await SaveInternalAsync(ct).ConfigureAwait(false);
                return;
            }

            await using var stream = File.OpenRead(ConfigFilePath);
            var parsed = await JsonSerializer.DeserializeAsync(
                stream,
                PrtscConfigJsonContext.Default.PrtscConfig,
                ct).ConfigureAwait(false);
            _current = parsed ?? new PrtscConfig();
            _logger.LogInformation("Loaded config from {Path}", ConfigFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load config; falling back to defaults");
            _current = new PrtscConfig();
        }
        finally
        {
            _ioLock.Release();
        }
    }

    public async Task SaveAsync(CancellationToken ct = default)
    {
        await _ioLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await SaveInternalAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _ioLock.Release();
        }
    }

    private async Task SaveInternalAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(ConfigDirectory);
        var tmp = ConfigFilePath + ".tmp";
        await using (var stream = File.Create(tmp))
        {
            await JsonSerializer.SerializeAsync(
                stream,
                _current,
                PrtscConfigJsonContext.Default.PrtscConfig,
                ct).ConfigureAwait(false);
        }
        File.Move(tmp, ConfigFilePath, overwrite: true);
        _logger.LogDebug("Saved config to {Path}", ConfigFilePath);
    }

    public void Update(Action<PrtscConfig> mutate)
    {
        mutate(_current);
        Changed?.Invoke(this, _current);
    }
}
