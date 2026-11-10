using Prtsc.Core.Annotation.Tools;
using Prtsc.Core.Config;

namespace Prtsc.Core.Tests;

/// <summary>
/// B-6 工具栏按钮自定义：可见性决策表 + config 往返。
/// 口径：Editor.VisibleTools 空/缺 = 全开（向前兼容——日后新增工具自动出现）；
/// 非空时未知名忽略、重复去重、Select 恒可见（默认工具必须有落点）；
/// 输出恒为目录规范序（dock 顺序与配置书写顺序无关）。
/// </summary>
public class ToolVisibilityTests
{
    [Fact]
    public void Resolve_NullOrEmpty_Config_MeansAllTools()
    {
        var all = ToolVisibility.Resolve(null);
        Assert.Equal(ToolCatalog.All.Select(e => e.Kind), all);

        var empty = ToolVisibility.Resolve(new List<string>());
        Assert.Equal(ToolCatalog.All.Select(e => e.Kind), empty);
    }

    [Fact]
    public void Resolve_CanonicalOrder_RegardlessOfConfigOrder()
    {
        var resolved = ToolVisibility.Resolve(new[] { "Crop", "Select", "Ellipse" });
        Assert.Equal(new[] { ToolKind.Select, ToolKind.Ellipse, ToolKind.Crop }, resolved);
    }

    [Fact]
    public void Resolve_UnknownIds_Ignored_CaseInsensitive()
    {
        var resolved = ToolVisibility.Resolve(new[] { "rect", "Nonexistent", "RECTANGLE" });
        Assert.Equal(new[] { ToolKind.Select, ToolKind.Rectangle }, resolved);
    }

    [Fact]
    public void Resolve_Duplicates_Deduped()
    {
        var resolved = ToolVisibility.Resolve(new[] { "Crop", "crop", "Select" });
        Assert.Equal(new[] { ToolKind.Select, ToolKind.Crop }, resolved);
    }

    [Fact]
    public void Resolve_Select_AlwaysVisible_EvenWhenExcluded()
    {
        var resolved = ToolVisibility.Resolve(ToolCatalog.All.Select(e => e.Kind.ToString())
            .Where(n => n != nameof(ToolKind.Select)).ToArray());
        Assert.Contains(ToolKind.Select, resolved);
    }

    [Fact]
    public void Resolve_AllKnownKinds_Parseable()
    {
        // 目录里的每个 kind 都必须能从自己的枚举名 round-trip 回来
        foreach (var e in ToolCatalog.All)
            Assert.Contains(e.Kind, ToolVisibility.Resolve(new[] { e.Kind.ToString() }));
    }

    [Fact]
    public void ToConfig_AllSelected_EmptyList()
    {
        var list = ToolVisibility.ToConfig(ToolCatalog.All.Select(e => e.Kind));
        Assert.Empty(list);
    }

    [Fact]
    public void ToConfig_Partial_PreservesMembership()
    {
        var list = ToolVisibility.ToConfig(new[] { ToolKind.Select, ToolKind.Crop });
        Assert.Equal(new[] { "Select", "Crop" }, list);

        var resolved = ToolVisibility.Resolve(list);
        Assert.Equal(new[] { ToolKind.Select, ToolKind.Crop }, resolved);
    }

    // —— config JSON 往返 ——

    [Fact]
    public void Config_Roundtrip_VisibleTools()
    {
        var config = new PrtscConfig();
        config.Editor.VisibleTools = new List<string> { "Select", "Crop" };
        var json = System.Text.Json.JsonSerializer.Serialize(config, PrtscConfigJsonContext.Default.PrtscConfig);
        var parsed = System.Text.Json.JsonSerializer.Deserialize(json, PrtscConfigJsonContext.Default.PrtscConfig);

        Assert.NotNull(parsed);
        Assert.Equal(new[] { "Select", "Crop" }, parsed!.Editor.VisibleTools);

        // 缺省 config 反序列化 → 空表 = 全开
        var fresh = System.Text.Json.JsonSerializer.Deserialize("{}", PrtscConfigJsonContext.Default.PrtscConfig);
        Assert.NotNull(fresh);
        Assert.Empty(fresh!.Editor.VisibleTools);
    }

    // —— dock 渲染辅助：分隔线可见性（App ToolDock 子项 → IsVisible 决策表） ——

    [Fact]
    public void DockVisibility_AllVisible_SeparatorsStay()
    {
        // Select | Rect.. | Crop 形态：radio, sep, radio, sep, radio
        bool[] visible = ToolVisibility.ComputeDockChildVisibility(
            new[] { Child("Select"), Sep(), Child("Rectangle"), Sep(), Child("Crop") },
            ToolVisibility.Resolve(null));
        Assert.Equal(new[] { true, true, true, true, true }, visible);
    }

    [Fact]
    public void DockVisibility_MiddleGroupHidden_AdjacentSeparatorsCollapseToOne()
    {
        // 中间组全隐：Select 与 Crop 之间恰好保留一条组边界线（点亮紧邻 Crop 前的那条）
        bool[] visible = ToolVisibility.ComputeDockChildVisibility(
            new[] { Child("Select"), Sep(), Child("Rectangle"), Sep(), Child("Crop") },
            ToolVisibility.Resolve(new[] { "Select", "Crop" }));
        Assert.Equal(new[] { true, false, false, true, true }, visible);
    }

    [Fact]
    public void DockVisibility_LeadingOrTrailingSeparators_Hidden()
    {
        // 首尾是分隔线（异常构造的防御口径）时不得悬空
        bool[] visible = ToolVisibility.ComputeDockChildVisibility(
            new[] { Sep(), Child("Select"), Sep(), Child("Crop"), Sep() },
            ToolVisibility.Resolve(new[] { "Select", "Crop" }));
        Assert.Equal(new[] { false, true, true, true, false }, visible);
    }

    [Fact]
    public void DockVisibility_OnlySelectVisible_NoSeparators()
    {
        bool[] visible = ToolVisibility.ComputeDockChildVisibility(
            new[] { Child("Select"), Sep(), Child("Rectangle"), Sep(), Child("Crop") },
            ToolVisibility.Resolve(new[] { "Select" }));
        Assert.Equal(new[] { true, false, false, false, false }, visible);
    }

    private static (bool IsSeparator, string? Tag) Child(string tag) => (false, tag);
    private static (bool IsSeparator, string? Tag) Sep() => (true, null);
}
