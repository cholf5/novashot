using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Novashot.Core.Config;

public interface IConfigStore
{
    NovashotConfig Current { get; }
    event EventHandler<NovashotConfig>? Changed;

    Task LoadAsync(CancellationToken ct = default);
    Task SaveAsync(CancellationToken ct = default);
    void Update(Action<NovashotConfig> mutate);

    string ConfigDirectory { get; }
    string ConfigFilePath { get; }
}

/// <summary>
/// JSON 持久化，%AppData%\Novashot\config.json。写入时先写临时文件再原子替换，避免半写导致下次启动崩溃。
/// </summary>
public sealed class JsonConfigStore : IConfigStore
{
    private readonly ILogger<JsonConfigStore> _logger;
    private readonly SemaphoreSlim _ioLock = new(1, 1);
    private NovashotConfig _current = new();

    public JsonConfigStore(ILogger<JsonConfigStore> logger, string? overrideDirectory = null)
    {
        _logger = logger;
        ConfigDirectory = overrideDirectory ?? DefaultConfigDirectory();
        ConfigFilePath = Path.Combine(ConfigDirectory, "config.json");
    }

    public NovashotConfig Current => _current;
    public string ConfigDirectory { get; }
    public string ConfigFilePath { get; }

    public event EventHandler<NovashotConfig>? Changed;

    public static string DefaultConfigDirectory() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Novashot");

    public async Task LoadAsync(CancellationToken ct = default)
    {
        await _ioLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(ConfigDirectory);
            if (!File.Exists(ConfigFilePath))
            {
                _current = new NovashotConfig();
                _logger.LogInformation("No config found at {Path}, using defaults", ConfigFilePath);
                await SaveInternalAsync(ct).ConfigureAwait(false);
                return;
            }

            await using var stream = File.OpenRead(ConfigFilePath);
            var parsed = await JsonSerializer.DeserializeAsync(
                stream,
                NovashotConfigJsonContext.Default.NovashotConfig,
                ct).ConfigureAwait(false);
            _current = parsed ?? new NovashotConfig();
            _logger.LogInformation("Loaded config from {Path}", ConfigFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load config; falling back to defaults");
            _current = new NovashotConfig();
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
                NovashotConfigJsonContext.Default.NovashotConfig,
                ct).ConfigureAwait(false);
        }
        File.Move(tmp, ConfigFilePath, overwrite: true);
        _logger.LogDebug("Saved config to {Path}", ConfigFilePath);
    }

    public void Update(Action<NovashotConfig> mutate)
    {
        mutate(_current);
        Changed?.Invoke(this, _current);
    }
}
