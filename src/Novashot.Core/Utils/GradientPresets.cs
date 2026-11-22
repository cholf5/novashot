namespace Novashot.Core.Utils;

/// <summary>
/// 24 个渐变背景预设，字段和顺序与 WinShot settings-panel.tsx 中的 preset 数组一一对齐。
/// 值为 CSS linear-gradient 字符串，编辑器合成时解析。
/// </summary>
public static class GradientPresets
{
    public sealed record Preset(string Name, string Value);

    public static readonly IReadOnlyList<Preset> All =
    [
        new("Sunset",     "linear-gradient(135deg, #f093fb 0%, #f5576c 100%)"),
        new("Ocean",      "linear-gradient(135deg, #667eea 0%, #764ba2 100%)"),
        new("Forest",     "linear-gradient(135deg, #11998e 0%, #38ef7d 100%)"),
        new("Fire",       "linear-gradient(135deg, #f12711 0%, #f5af19 100%)"),
        new("Cool Blue",  "linear-gradient(135deg, #2193b0 0%, #6dd5ed 100%)"),
        new("Lavender",   "linear-gradient(135deg, #c471f5 0%, #fa71cd 100%)"),
        new("Aqua",       "linear-gradient(135deg, #13547a 0%, #80d0c7 100%)"),
        new("Grape",      "linear-gradient(135deg, #5f2c82 0%, #49a09d 100%)"),
        new("Peach",      "linear-gradient(135deg, #ffecd2 0%, #fcb69f 100%)"),
        new("Sky",        "linear-gradient(135deg, #a8edea 0%, #fed6e3 100%)"),
        new("Warm",       "linear-gradient(135deg, #ff9a9e 0%, #fecfef 100%)"),
        new("Mint",       "linear-gradient(135deg, #d4fc79 0%, #96e6a1 100%)"),
        new("Midnight",   "linear-gradient(135deg, #0f0c29 0%, #302b63 50%, #24243e 100%)"),
        new("Carbon",     "linear-gradient(135deg, #1a1a2e 0%, #16213e 100%)"),
        new("Deep Space", "linear-gradient(135deg, #000428 0%, #004e92 100%)"),
        new("Noir",       "linear-gradient(135deg, #232526 0%, #414345 100%)"),
        new("Royal",      "linear-gradient(135deg, #141e30 0%, #243b55 100%)"),
        new("Rose Gold",  "linear-gradient(135deg, #f4c4f3 0%, #fc67fa 100%)"),
        new("Emerald",    "linear-gradient(135deg, #1d976c 0%, #93f9b9 100%)"),
        new("Amethyst",   "linear-gradient(135deg, #9d50bb 0%, #6e48aa 100%)"),
        new("Neon",       "linear-gradient(135deg, #00f260 0%, #0575e6 100%)"),
        new("Aurora",     "linear-gradient(135deg, #00c6fb 0%, #005bea 100%)"),
        new("Candy",      "linear-gradient(135deg, #ff6a88 0%, #ff99ac 100%)"),
        new("Clean",      "linear-gradient(135deg, #f5f7fa 0%, #c3cfe2 100%)"),
    ];

    public const string Default = "linear-gradient(135deg, #667eea 0%, #764ba2 100%)"; // Ocean
}
