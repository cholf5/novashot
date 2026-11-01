using System.Globalization;
using SkiaSharp;

namespace Prtsc.Core.Editor;

/// <summary>解析后的线性渐变：CSS 角度 + 按 Pos 升序的色标。</summary>
public sealed record GradientSpec(
    float AngleDeg,
    IReadOnlyList<(float Pos, SKColor Color)> Stops);

/// <summary>
/// 解析 WinShot 风格的 CSS <c>linear-gradient(135deg, #f093fb 0%, #f5576c 100%)</c> 字符串。
/// <see cref="GradientPresets"/> 的 24 条预设都是这个形状（2–3 个色标）。
/// 色标位置可省略：首=0%、尾=100%、中间=相邻中点。TryParse 语义：畸形输入返回 false。
/// </summary>
public static class GradientParser
{
    /// <summary>尝试解析。支持 #rgb / #rrggbb / #rrggbbaa；色标按 Pos 升序返回。</summary>
    public static bool TryParse(string css, out GradientSpec spec)
    {
        spec = new GradientSpec(135f, Array.Empty<(float, SKColor)>());

        if (string.IsNullOrWhiteSpace(css)) return false;
        css = css.Trim();

        const string prefix = "linear-gradient(";
        if (!css.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !css.EndsWith(')'))
            return false;
        var body = css[prefix.Length..^1].Trim();

        var firstComma = body.IndexOf(',');
        if (firstComma < 0) return false;
        var head = body[..firstComma].Trim();
        if (!head.EndsWith("deg", StringComparison.OrdinalIgnoreCase) ||
            !float.TryParse(head[..^3].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var angle))
            return false;

        // 第一遍：收集 (color, hasPos, pos)
        var tokens = new List<(SKColor Color, bool HasPos, float Pos)>();
        foreach (var raw in body[(firstComma + 1)..].Split(','))
        {
            var token = raw.Trim();
            if (token.Length == 0) return false;
            var parts = token.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length is < 1 or > 2) return false;
            if (!TryParseColor(parts[0], out var color)) return false;

            if (parts.Length == 2)
            {
                var p = parts[1];
                if (!p.EndsWith('%') ||
                    !float.TryParse(p[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var pos))
                    return false;
                tokens.Add((color, true, pos));
            }
            else
            {
                tokens.Add((color, false, 0f));
            }
        }
        if (tokens.Count < 2) return false;

        // 第二遍：补省略的位置（首 0 / 尾 100 / 中间相邻中点）
        for (var i = 0; i < tokens.Count; i++)
        {
            if (tokens[i].HasPos) continue;
            float pos;
            if (i == 0) pos = 0f;
            else if (i == tokens.Count - 1) pos = 100f;
            else
            {
                var prev = tokens[i - 1].HasPos ? tokens[i - 1].Pos : 0f;
                var next = tokens[i + 1].HasPos ? tokens[i + 1].Pos : 100f;
                pos = (prev + next) * 0.5f;
            }
            tokens[i] = (tokens[i].Color, true, pos);
        }

        var stops = tokens
            .Select(t => (t.Pos, t.Color))
            .OrderBy(s => s.Pos)
            .ToList();
        spec = new GradientSpec(angle, stops);
        return true;
    }

    private static bool TryParseColor(string token, out SKColor color)
    {
        color = default;
        if (token.Length == 0 || token[0] != '#') return false;
        try
        {
            color = SKColor.Parse(token);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
