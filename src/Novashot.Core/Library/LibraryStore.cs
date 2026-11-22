using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Novashot.Core.Library;

/// <summary>
/// Library 历史索引：%AppData%\Novashot\library.json，追加式（Add 追加，Remove 按 Id 删）。
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
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Novashot");

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

    /// <summary>B-7：按 Id 删除，可选连磁盘文件一起删。
    /// 顺序裁决：记录先提交（library.json 是记录索引，文件删除失败不回滚）；
    /// 文件删除失败仅影响返回的 File 结果，由 UI 提示。
    /// tryDelete 供测试注入失败/验证调用时机；null 走真实 IO（LibraryFiles.TryDelete，失败吞异常返 false）。</summary>
    public async Task<(bool RecordRemoved, LibraryFileDeleteResult File)> RemoveAsync(
        string id, bool deleteLocalFile, CancellationToken ct = default, Func<string, bool>? tryDelete = null)
    {
        await _ioLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var entries = await LoadInternalAsync(ct).ConfigureAwait(false);
            var entry = entries.FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.Ordinal));
            if (entry is null)
                return (false, LibraryFileDeleteResult.NotRequested);

            var next = entries.Where(e => !string.Equals(e.Id, id, StringComparison.Ordinal)).ToList();
            await SaveInternalAsync(next, ct).ConfigureAwait(false);

            var hasPath = !string.IsNullOrEmpty(entry.LocalPath);
            var exists = hasPath && File.Exists(entry.LocalPath);
            // 删除 IO 只能在勾选后发生——决策先行，副作用后置（默认 false 绝不动磁盘文件）
            var deleted = deleteLocalFile && exists && (tryDelete ?? LibraryFiles.TryDelete)(entry.LocalPath);
            var file = LibraryFileDeletePolicy.Decide(deleteLocalFile, entry.LocalPath, exists, deleted);
            if (file == LibraryFileDeleteResult.FailedButRecordDeleted)
                _logger?.LogWarning("Library: record {Id} removed but file delete failed at {Path}", entry.Id, entry.LocalPath);
            return (true, file);
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
