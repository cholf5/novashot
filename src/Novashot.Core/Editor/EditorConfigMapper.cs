using Novashot.Core.Config;
using SkiaSharp;

namespace Novashot.Core.Editor;

/// <summary>
/// EditorState ↔ EditorConfig 双向映射（G-13/G-14 初始化 + E-2 画布参数持久化）。
/// config.editor 段语义 = <b>「上次使用的画布参数」</b>：新编辑器从 config 读初始态（FromConfig），
/// 画布面板每改一次回写 config（ApplyTo）——「编辑器默认值」即「上次使用值」，单一事实源。
/// 仅覆盖画布外观字段；outputRatio / autoBackground / shapeCornerRadius 不属画布外观，不在映射范围。
/// </summary>
public static class EditorConfigMapper
{
    /// <summary>
    /// config → 初始 EditorState。showBackground=false 时背景为 None（设置页「显示背景」开关）；
    /// includeBackground=false（winshot 导出裸图语义）时剥离背景板/padding/阴影/内衬/边框，圆角保留。
    /// </summary>
    public static EditorState FromConfig(EditorConfig config, bool includeBackground = true)
    {
        ArgumentNullException.ThrowIfNull(config);

        var isGradient = config.BackgroundColor.StartsWith("linear-gradient", StringComparison.OrdinalIgnoreCase);
        var state = new EditorState
        {
            Background = !config.ShowBackground
                ? BackgroundKind.None
                : isGradient ? BackgroundKind.Gradient : BackgroundKind.SolidColor,
            BackgroundColor = isGradient ? default : GradientParser.ParseColor(config.BackgroundColor) ?? default,
            BackgroundGradient = isGradient ? config.BackgroundColor : null,
            Padding = config.Padding,
            CornerRadius = config.CornerRadius,
            ShadowSize = config.ShadowSize,
            InsetSize = config.Inset,
            InsetColor = string.IsNullOrWhiteSpace(config.InsetBackgroundColor)
                ? null
                : GradientParser.ParseColor(config.InsetBackgroundColor),
            BorderWidth = config.BorderEnabled ? config.BorderWeight : 0f,
            BorderColor = GradientParser.ParseColor(config.BorderColor),
            Border = config.BorderType switch
            {
                "outside" => BorderStyle.Outside,
                "inside" => BorderStyle.Inside,
                _ => BorderStyle.Center,
            },
        };
        return EditorStateStripping.StripBackground(state, includeBackground).Clamped();
    }

    /// <summary>
    /// EditorState → config 回写（画布外观字段）。关态只清零数值/开关，颜色串保留备下次开启；
    /// 渐变为 null（畸形兜底）时不动 config 现值，避免抹掉可用值。
    /// </summary>
    public static void ApplyTo(EditorState state, EditorConfig config)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(config);

        config.ShowBackground = state.HasBackground;
        switch (state.Background)
        {
            case BackgroundKind.SolidColor:
                config.BackgroundColor = ToCssColor(state.BackgroundColor);
                break;
            case BackgroundKind.Gradient:
                if (!string.IsNullOrWhiteSpace(state.BackgroundGradient))
                    config.BackgroundColor = state.BackgroundGradient;
                break;
        }

        config.Padding = state.Padding;
        config.CornerRadius = (int)MathF.Round(state.CornerRadius);
        config.ShadowSize = (int)MathF.Round(state.ShadowSize);

        config.Inset = (int)MathF.Round(state.InsetSize);
        if (state.InsetSize > 0f)
            config.InsetBackgroundColor = ToCssColor(state.EffectiveInsetColor);

        if (state.BorderWidth > 0f)
        {
            config.BorderEnabled = true;
            config.BorderWeight = (int)MathF.Round(state.BorderWidth);
            config.BorderColor = ToCssColor(state.EffectiveBorderColor);
            config.BorderType = state.Border switch
            {
                BorderStyle.Outside => "outside",
                BorderStyle.Inside => "inside",
                _ => "center",
            };
        }
        else
        {
            config.BorderEnabled = false;
        }
    }

    /// <summary>SKColor → CSS 十六进制（小写，与 config 既有风格一致）：不透明 #rrggbb，带 alpha #rrggbbaa（GradientParser 可回读）；透明黑兜底白色。</summary>
    private static string ToCssColor(SKColor color)
    {
        if (color == default) return "#ffffff";
        return color.Alpha == 0xFF
            ? $"#{color.Red:x2}{color.Green:x2}{color.Blue:x2}"
            : $"#{color.Red:x2}{color.Green:x2}{color.Blue:x2}{color.Alpha:x2}";
    }
}
