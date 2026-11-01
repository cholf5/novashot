using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Prtsc.Core.Library;

/// <summary>
/// Library 历史索引：%AppData%\Prtsc\library.json，追加式（Add 追加，Remove 按 Id 删）。
/// 写入先写 .tmp 再原子替换；文件损坏时回退空索引，下次 Add 直接重建。
/// overrideDirectory 供测试 / 便携模式注入。
/// </summary>
public sealed class LibraryStore
{
    private readonly ILogger<LibraryStore> _logger;
    private readonly SemaphoreSlim _ioLock = new(1, 1);

    public LibraryStore(ILogger<LibraryStore> logger, string? overrideDirectory = null)
    {
        _logger = logger;
        Directory = overrideDirectory ?? DefaultDirectory();
        FilePath = Path.Combine(Directory, "library.json");
    }

    public string Directory { get; }
    public string FilePath { get; }

    public static string DefaultDirectory() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Prtsc");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>读全部条目。文件缺失/损坏 → 空列表（只读不建文件）。</summary>
    public async Task<List<LibraryEntry>> LoadAsync(CancellationToken ct = default)
    {
        await _ioLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await LoadInternalAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _ioLock.Release();
        }
    }

    /// <summary>追加一条。空/重复 Id 自动生成唯一 Id；完成后返回最终条目。</summary>
    public async Task<LibraryEntry> AddAsync(LibraryEntry entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        await _ioLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var entries = await LoadInternalAsync(ct).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(entry.Id))
                entry.Id = Guid.NewGuid().ToString("N");
            while (entries.Any(e => string.Equals(e.Id, entry.Id, StringComparison.Ordinal)))
                entry.Id = Guid.NewGuid().ToString("N");

            entries.Add(entry);
            await SaveInternalAsync(entries, ct).ConfigureAwait(false);
            _logger?.LogDebug("Library: added {File} (total {Count})", entry.FileName, entries.Count);
            return entry;
        }
        finally
        {
            _ioLock.Release();
        }
    }

    /// <summary>按 Id 删除。找到并删掉返回 true。</summary>
    public async Task<bool> RemoveAsync(string id, CancellationToken ct = default)
    {
        await _ioLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var entries = await LoadInternalAsync(ct).ConfigureAwait(false);
            var next = entries.Where(e => !string.Equals(e.Id, id, StringComparison.Ordinal)).ToList();
            if (next.Count == entries.Count)
                return false;
            await SaveInternalAsync(next, ct).ConfigureAwait(false);
            return true;
        }
        finally
        {
            _ioLock.Release();
        }
    }

    private async Task<List<LibraryEntry>> LoadInternalAsync(CancellationToken ct)
    {
        if (!File.Exists(FilePath))
            return [];

        try
        {
            await using var stream = File.OpenRead(FilePath);
            var parsed = await JsonSerializer.DeserializeAsync<List<LibraryEntry>>(stream, Options, ct)
                .ConfigureAwait(false);
            return parsed ?? [];
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            _logger?.LogError(ex, "Library index corrupted at {Path}; starting fresh", FilePath);
            return [];
        }
    }

    private async Task SaveInternalAsync(List<LibraryEntry> entries, CancellationToken ct)
    {
        System.IO.Directory.CreateDirectory(Directory);
        var tmp = FilePath + ".tmp";
        await using (var stream = File.Create(tmp))
        {
            await JsonSerializer.SerializeAsync(stream, entries, Options, ct).ConfigureAwait(false);
        }
        File.Move(tmp, FilePath, overwrite: true);
    }
}
