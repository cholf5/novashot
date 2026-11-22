using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Headless;
using Novashot.App.Localization;

namespace Novashot.App.Tests;

/// <summary>
/// L-2 收尾：Loc 双语表的 CI 兜底单测。
///
/// 覆盖四件事：
/// 1. <see cref="Strings"/> 的 En/Zh 两张表 key 集合双向相等 + value 非空非纯空白
///    + per-key {N} 占位符序列一致（review I-1：丢占位符=静默吞参，多占位符=运行时 FormatException）；
/// 2. 引用完整性（单方向：引用 ⊆ 定义）：扫描 src/Novashot.App 的 .axaml（DynamicResource/StaticResource）
///    与 .cs（Loc.T("...")、托盘私有 L("...")、FindResource/Resources["L.x"]）提取 key 引用，
///    断言每个引用在 En 与 Zh 都有定义；
/// 3. 动态 key 补钉（review M-1）：SettingsViewModel.ValidateHotkeys 返回的 key 名显式断言在册；
/// 4. 回退链：zh 缺 key → en 兜底；两侧都缺 → 返回 key 本身；带参 T(key, args) 走 string.Format。
///
/// 刻意不做的断言（避免 false positive，见 batch 简报裁决）：
/// - 「定义 ⊆ 引用」反方向不断言 —— 存在动态拼 key（ValidateHotkeys，已由 M-1 显式断言兜底）
///   与刻意预留的 key（如 Picker.Empty），未引用 ≠ 错误。
/// - 多行调用的 string 提取：括号配平按当前源码风格（调用都在单行）处理，跨行调用不保证覆盖；
///   本测试只保证「引用到的 key 必须存在」，漏扫只影响覆盖面不影响正确性。
///
/// 说明：Loc.Current / Strings 都是静态状态且回退测试要注入/移除 key，
/// 因此挂在 HeadlessUi collection（DisableParallelization）下，不与其它测试并行。
/// </summary>
[Collection("HeadlessUi")]
public class LocKeyConsistencyTests
{
    // key 形状：Segment 大写字母开头（En/Zh 表全符合），如 Capture.Title、Tray.DelayedCapture.3s。
    // 用来从调用点提取的字符串里滤掉非 key 参数（如 string.Format 的 "0.##"、空串 ""）。
    private static readonly Regex KeyShape = new(@"^[A-Z][A-Za-z0-9]*(?:\.[A-Za-z0-9_]+)+$", RegexOptions.Compiled);

    // string.Format 占位符：{0} {1} ...（review I-1：En/Zh 同 key 的占位符序列必须一致）
    private static readonly Regex FormatPlaceholder = new(@"\{\d+\}", RegexOptions.Compiled);

    private static readonly Regex XamlResourceRef = new(@"(?:Dynamic|Static)Resource\s+(L\.[A-Za-z0-9_.]+)", RegexOptions.Compiled);

    // Resources["L.x"] / this.FindResource("L.x") / TryFindResource("L.x") —— 现无使用点，防将来回归
    private static readonly Regex CodeLResourceLookup = new(@"(?:(?:FindResource|TryFindResource)\(\s*|Resources\s*\[\s*)""(L\.[A-Za-z0-9_.]+)""", RegexOptions.Compiled);

    // 取串入口：Loc.T(...) 与 App.axaml.cs 托盘用的私有 helper L(...)
    private static readonly Regex CallStart = new(@"(?:Loc\.T|(?<![A-Za-z0-9_.])L)\(", RegexOptions.Compiled);
    private static readonly Regex StringLiteral = new(@"""((?:[^""\\]|\\.)*)""", RegexOptions.Compiled);

    // ---------- 1. En/Zh 集合相等 + value 非空 ----------

    [Fact]
    public void En_and_Zh_have_identical_key_sets_and_no_blank_values()
    {
        var en = Strings.En;
        var zh = Strings.Zh;

        Assert.NotEmpty(en);
        Assert.NotEmpty(zh);

        var onlyInEn = en.Keys.Except(zh.Keys, StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal).ToList();
        var onlyInZh = zh.Keys.Except(en.Keys, StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal).ToList();
        Assert.True(onlyInEn.Count == 0 && onlyInZh.Count == 0,
            "En/Zh key 集合必须双向相等。\n" +
            $"只在 En 存在（{onlyInEn.Count}）: {string.Join(", ", onlyInEn)}\n" +
            $"只在 Zh 存在（{onlyInZh.Count}）: {string.Join(", ", onlyInZh)}");

        var blankEn = en.Where(kv => string.IsNullOrWhiteSpace(kv.Value)).Select(kv => kv.Key).ToList();
        var blankZh = zh.Where(kv => string.IsNullOrWhiteSpace(kv.Value)).Select(kv => kv.Key).ToList();
        Assert.True(blankEn.Count == 0 && blankZh.Count == 0,
            "所有 value 必须非空非纯空白。\n" +
            $"En 空值 key: {string.Join(", ", blankEn)}\n" +
            $"Zh 空值 key: {string.Join(", ", blankZh)}");

        // review I-1：per-key 占位符序列一致性（排序后比较多重集）。
        // zh 丢 {0} → string.Format 静默吞参（如导出路径从状态栏文案消失）；
        // zh 多/错位占位符 → 调用点 Loc.T(key, args) 运行时 FormatException。
        // key 集合相等 + value 非空都拦不住这种 value 层损坏（mutation 实证过的盲区）。
        string Ph(string s) => string.Join(", ", FormatPlaceholder.Matches(s).Select(m => m.Value)
            .OrderBy(v => v, StringComparer.Ordinal));
        var placeholderMismatch = en.Keys
            .Where(k => !FormatPlaceholder.Matches(en[k]).Select(m => m.Value)
                        .OrderBy(v => v, StringComparer.Ordinal)
                        .SequenceEqual(FormatPlaceholder.Matches(zh[k]).Select(m => m.Value)
                        .OrderBy(v => v, StringComparer.Ordinal)))
            .OrderBy(k => k, StringComparer.Ordinal).ToList();
        Assert.True(placeholderMismatch.Count == 0,
            "En/Zh 同 key 的 {N} 占位符序列必须一致。\n" +
            string.Join("\n", placeholderMismatch.Select(k => $"{k}:  En=[{Ph(en[k])}]  Zh=[{Ph(zh[k])}]")));
    }

    // ---------- 2. 引用完整性（引用 ⊆ 定义） ----------

    [Fact]
    public void Every_referenced_key_is_defined_in_both_tables()
    {
        var srcDir = Path.Combine(TestRepoRoot.Path, "src", "Novashot.App");
        Assert.True(Directory.Exists(srcDir), $"找不到源码目录: {srcDir}");

        var references = new HashSet<string>(StringComparer.Ordinal);
        var files = Directory.EnumerateFiles(srcDir, "*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".axaml", StringComparison.OrdinalIgnoreCase)
                        || f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            // Loc.cs 是定义处不是引用处（词典本身、Loc.T 实现）
            .Where(f => !f.EndsWith("Loc.cs", StringComparison.OrdinalIgnoreCase));

        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            var isXaml = file.EndsWith(".axaml", StringComparison.OrdinalIgnoreCase);

            foreach (Match m in XamlResourceRef.Matches(text))
                references.Add(m.Groups[1].Value["L.".Length..]);

            if (!isXaml)
            {
                foreach (Match m in CodeLResourceLookup.Matches(text))
                    references.Add(m.Groups[1].Value["L.".Length..]);

                foreach (var call in ExtractCallArguments(text))
                    foreach (Match m in StringLiteral.Matches(call))
                        if (KeyShape.IsMatch(m.Groups[1].Value))
                            references.Add(m.Groups[1].Value);
            }
        }

        // 扫描一旦失配（文件挪走/正则过时）导致 0 引用，必须显式失败，不能静默通过
        Assert.NotEmpty(references);

        var missing = references
            .Where(k => !Strings.En.ContainsKey(k) || !Strings.Zh.ContainsKey(k))
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();
        Assert.True(missing.Count == 0,
            "以下被引用的 key 没有同时在 En/Zh 两表定义：\n  " +
            string.Join("\n  ", missing.Select(k =>
                $"{k}  (En: {(Strings.En.ContainsKey(k) ? "✓" : "缺")}, Zh: {(Strings.Zh.ContainsKey(k) ? "✓" : "缺")})")));
    }

    /// <summary>
    /// review M-1：动态 key 盲区补钉。<see cref="Novashot.App.ViewModels.SettingsViewModel.ValidateHotkeys"/>
    /// 返回 key 名字面量，经变量传给 Loc.T（SettingsViewModel.Save），上面的扫描提取不到；
    /// 这里显式断言在册，把「动态引用」从注释声明变成断言。
    /// </summary>
    public static readonly string[] DynamicallyReferencedKeys =
    [
        "Settings.Hint.HotkeyInvalid",
    ];

    [Fact]
    public void Dynamically_referenced_keys_are_defined_in_both_tables()
    {
        var missing = DynamicallyReferencedKeys
            .Where(k => !Strings.En.ContainsKey(k) || !Strings.Zh.ContainsKey(k))
            .ToList();
        Assert.True(missing.Count == 0,
            $"动态引用的 key 必须双表在册（引用方: SettingsViewModel.ValidateHotkeys）: {string.Join(", ", missing)}");
    }

    /// <summary>
    /// 提取 <c>Loc.T(...)</c> / <c>L(...)</c> 调用的括号内实参文本，供字符串字面量扫描。
    /// 跳过字符串字面量内部的括号，避免把值里的 "(...)" 当成嵌套调用；当前约定调用不跨行。
    /// </summary>
    private static IEnumerable<string> ExtractCallArguments(string text)
    {
        foreach (Match start in CallStart.Matches(text))
        {
            var i = start.Index + start.Length;
            var depth = 1;
            var inString = false;
            var sb = new System.Text.StringBuilder();
            for (; i < text.Length && depth > 0; i++)
            {
                var c = text[i];
                if (inString)
                {
                    if (c == '\\') { sb.Append(c); if (i + 1 < text.Length) sb.Append(text[++i]); continue; }
                    if (c == '"') inString = false;
                }
                else if (c == '"') inString = true;
                else if (c == '(') depth++;
                else if (c == ')') { depth--; if (depth == 0) break; }
                sb.Append(c);
            }
            yield return sb.ToString();
        }
    }

    // ---------- 3. 回退链 ----------

    [Fact]
    public async Task T_missing_key_falls_back_to_en_then_returns_key_itself()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(SnapshotBootstrap));
        await session.Dispatch(() =>
        {
            var app = Application.Current!;
            Loc.Initialize("en", app);

            // 两侧都缺 → 返回 key 本身（开发期一眼看出漏翻）
            Assert.Equal("TestOnly.MissingEverywhere", Loc.T("TestOnly.MissingEverywhere"));

            // zh 缺 → en 兜底：注入测试 key 只进 En 表（Strings internal，测试后清理）
            Strings.En["TestOnly.EnOnly"] = "english only";
            try
            {
                Loc.Initialize("zh-CN", app);
                Assert.Equal("english only", Loc.T("TestOnly.EnOnly"));

                // zh 侧补齐后优先 zh
                Strings.Zh["TestOnly.EnOnly"] = "中文优先";
                Assert.Equal("中文优先", Loc.T("TestOnly.EnOnly"));
            }
            finally
            {
                Strings.En.Remove("TestOnly.EnOnly");
                Strings.Zh.Remove("TestOnly.EnOnly");
                Loc.Initialize("en", app); // 还原默认语言，避免污染后续测试
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task T_with_args_formats_via_string_format()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(SnapshotBootstrap));
        await session.Dispatch(() =>
        {
            var app = Application.Current!;
            Loc.Initialize("en", app);

            // 真实带参 key：Status.Captured = "Captured {0} × {1} @ {2}×"
            Assert.Equal("Captured 4 × 2 @ 1.5×", Loc.T("Status.Captured", 4, 2, "1.5"));
            Assert.Equal("Saved /tmp/x.png", Loc.T("Status.Exported", "/tmp/x.png"));

            // zh 缺、en 有占位符的注入 key：走 string.Format
            Strings.En["TestOnly.Formatted"] = "Saved {0}!";
            try
            {
                Loc.Initialize("zh-CN", app);
                Assert.Equal("Saved /tmp/a.png!", Loc.T("TestOnly.Formatted", "/tmp/a.png"));
            }
            finally
            {
                Strings.En.Remove("TestOnly.Formatted");
                Loc.Initialize("en", app);
            }

            // 未知 key + 参数：key 本身没有占位符，Format 原样返回 key
            Assert.Equal("TestOnly.MissingEverywhere", Loc.T("TestOnly.MissingEverywhere", "unused"));
        }, CancellationToken.None);
    }
}
