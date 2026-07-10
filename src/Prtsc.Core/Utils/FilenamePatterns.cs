using System.Globalization;
using System.Text.RegularExpressions;

namespace Prtsc.Core.Utils;

/// <summary>
/// 文件名模式：timestamp / date / increment，对齐 WinShot 的三种模式。
/// </summary>
public static class FilenamePatterns
{
    public const string Timestamp = "timestamp";
    public const string Date = "date";
    public const string Increment = "increment";

    public const string Prefix = "prtsc";

    /// <summary>
    /// 根据模式生成一个不冲突的文件名（不含目录）。
    /// </summary>
    /// <param name="pattern">timestamp | date | increment</param>
    /// <param name="folder">目标目录，用于检测重名冲突。</param>
    /// <param name="extension">扩展名（含点），例如 ".png"。</param>
    /// <param name="now">当前时间，测试可注入。</param>
    public static string Generate(string pattern, string folder, string extension, DateTime? now = null)
    {
        var stamp = now ?? DateTime.Now;
        extension = NormalizeExtension(extension);

        return pattern?.ToLowerInvariant() switch
        {
            Date => GenerateDate(folder, extension, stamp),
            Increment => GenerateIncrement(folder, extension),
            _ => GenerateTimestamp(extension, stamp),
        };
    }

    private static string GenerateTimestamp(string ext, DateTime stamp) =>
        $"{Prefix}_{stamp:yyyy-MM-dd_HH-mm-ss}{ext}";

    private static string GenerateDate(string folder, string ext, DateTime stamp)
    {
        var baseName = $"{Prefix}_{stamp:yyyy-MM-dd}";
        var candidate = baseName + ext;
        if (!File.Exists(Path.Combine(folder, candidate)))
            return candidate;
        for (var i = 1; i < 10_000; i++)
        {
            candidate = $"{baseName}_{i}{ext}";
            if (!File.Exists(Path.Combine(folder, candidate)))
                return candidate;
        }
        throw new IOException($"Too many files with base name {baseName}");
    }

    private static readonly Regex IncrementRegex =
        new($"^{Prefix}_(\\d{{3,}})", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static string GenerateIncrement(string folder, string ext)
    {
        var max = 0;
        if (Directory.Exists(folder))
        {
            foreach (var file in Directory.EnumerateFiles(folder, $"{Prefix}_*{ext}"))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                var m = IncrementRegex.Match(name);
                if (m.Success && int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
                    max = Math.Max(max, n);
            }
        }
        var next = max + 1;
        return $"{Prefix}_{next:D3}{ext}";
    }

    private static string NormalizeExtension(string ext)
    {
        if (string.IsNullOrWhiteSpace(ext)) return ".png";
        return ext.StartsWith('.') ? ext.ToLowerInvariant() : "." + ext.ToLowerInvariant();
    }
}
