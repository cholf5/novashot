namespace Novashot.Core.Update;

/// <summary>
/// C-1/R-1 更新检查决策（Core 纯函数，不依赖 Velopack——App 层拿到结果后才碰 UpdateManager）。
/// 版本语义 = System.Version（4 段数字版本）：带 prerelease/字母后缀的字符串一律解析失败 → 不提示，
/// 宁可少提示也不误弹。SkippedVersion 只吞同版本，更高版本会再次提示。
/// </summary>
public static class UpdateCheckPolicy
{
    /// <summary>
    /// 启动时是否该做更新检查：已打包 && 开关开 && FeedUrl 非空，三者缺一都静默跳过。
    /// （未打包 = 开发跑 / mac·linux 腿；FeedUrl 空 = 用户没配 feed。）
    /// </summary>
    public static bool ShouldCheckOnStartup(bool isPackaged, bool checkOnStartup, string? feedUrl)
        => isPackaged
           && checkOnStartup
           && !string.IsNullOrWhiteSpace(feedUrl);

    /// <summary>
    /// 检查到新版本后是否弹提示：目标版本 &gt; 当前版本，且目标版本 != 用户跳过的版本。
    /// 任一必需要解析的版本（update/current）非法 → 不提示；仅 skipped 非法 → 忽略跳过记录照常提示。
    /// </summary>
    public static bool ShouldPrompt(string? updateVersion, string? currentVersion, string? skippedVersion)
    {
        if (!TryParse(updateVersion, out var update)) return false;
        if (!TryParse(currentVersion, out var current)) return false;
        if (update <= current) return false;
        if (TryParse(skippedVersion, out var skipped) && update == skipped) return false;
        return true;
    }

    private static bool TryParse(string? text, out Version version)
    {
        if (Version.TryParse(text?.Trim(), out var parsed) && parsed is not null)
        {
            version = parsed;
            return true;
        }
        version = new Version(0, 0);
        return false;
    }
}
