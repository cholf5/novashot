namespace Prtsc.Core.Annotation.Tools;

/// <summary>
/// B-6 工具栏按钮自定义：工具目录 + 可见性决策（纯函数，无 UI 依赖）。
/// dock 上的分组顺序与 MainWindow.axaml 的分隔线位置一致；
/// config 侧 <c>Editor.VisibleTools</c> 存 ToolKind 枚举名，空表 = 全开（向前兼容）。
/// </summary>
public static class ToolVisibility
{
    /// <summary>
    /// 解析 config 的可见工具 id 列表为规范序 ToolKind 列表。
    /// 空/null = 全开；未知名忽略（大小写不敏感）；重复去重；
    /// <see cref="ToolKind.Select"/> 恒可见（默认工具必须有落点，热键 V 始终有对应 UI 锚点）。
    /// </summary>
    public static IReadOnlyList<ToolKind> Resolve(IReadOnlyCollection<string>? configured)
    {
        if (configured is null || configured.Count == 0)
            return ToolCatalog.All.Select(e => e.Kind).ToArray();

        var set = new HashSet<ToolKind>();
        foreach (var raw in configured)
        {
            if (Enum.TryParse<ToolKind>(raw.Trim(), ignoreCase: true, out var kind))
                set.Add(kind);
        }

        // Select 恒可见；顺序走目录规范序而非配置书写序
        set.Add(ToolKind.Select);
        return ToolCatalog.All.Select(e => e.Kind).Where(set.Contains).ToArray();
    }

    /// <summary>
    /// 设置页勾选结果 → config 存储值。全选时存空表（「默认全开」的规范形态，
    /// 日后新增工具自动出现）；部分勾选存枚举名（目录规范序）。
    /// </summary>
    public static List<string> ToConfig(IEnumerable<ToolKind> selected)
    {
        var set = new HashSet<ToolKind>(selected);
        if (set.Count >= ToolCatalog.All.Count)
            return new List<string>();

        return ToolCatalog.All.Select(e => e.Kind).Where(set.Contains)
            .Select(k => k.ToString()).ToList();
    }

    /// <summary>
    /// dock 子项可见性决策：RadioButtons 按 <paramref name="visible"/> 过滤；
    /// 分隔线按「组边界」发射——每个新组的第一个可见工具点亮紧邻其前的那条分隔线，
    /// 其余（含整组隐藏后夹在中间的、首尾悬空的）一律隐藏。
    /// </summary>
    public static bool[] ComputeDockChildVisibility(
        IReadOnlyList<(bool IsSeparator, string? Tag)> children,
        IReadOnlyList<ToolKind> visible)
    {
        var visibleSet = new HashSet<ToolKind>(visible);
        var result = new bool[children.Count];

        int? prevGroup = null;
        for (var i = 0; i < children.Count; i++)
        {
            var (isSeparator, tag) = children[i];
            if (isSeparator) continue;
            if (!Enum.TryParse<ToolKind>(tag, ignoreCase: true, out var kind) || !visibleSet.Contains(kind))
                continue; // 隐藏项不打断组序
            var group = GroupOf(kind);
            if (prevGroup is int prev && prev != group)
            {
                // 新组第一个可见项：点亮紧邻其前的分隔线（其前须已有可见工具，防首部悬空线）
                for (var j = i - 1; j >= 0; j--)
                {
                    if (!children[j].IsSeparator) continue;
                    result[j] = HasVisibleRadioBefore(children, visibleSet, j);
                    break;
                }
            }
            prevGroup = group;
        }

        for (var i = 0; i < children.Count; i++)
        {
            if (children[i].IsSeparator) continue;
            result[i] = Enum.TryParse<ToolKind>(children[i].Tag, ignoreCase: true, out var kind)
                        && visibleSet.Contains(kind);
        }

        return result;
    }

    private static int GroupOf(ToolKind kind) =>
        ToolCatalog.All.First(e => e.Kind == kind).Group;

    private static bool HasVisibleRadioBefore(
        IReadOnlyList<(bool IsSeparator, string? Tag)> children,
        HashSet<ToolKind> visibleSet,
        int end)
    {
        for (var i = 0; i < end; i++)
        {
            if (children[i].IsSeparator) continue;
            if (Enum.TryParse<ToolKind>(children[i].Tag, ignoreCase: true, out var kind)
                && visibleSet.Contains(kind)) return true;
        }
        return false;
    }
}

/// <summary>工具目录：dock 规范顺序 + 分组（组间渲染分隔线）。</summary>
public static class ToolCatalog
{
    public sealed record Entry(ToolKind Kind, int Group);

    /// <summary>与 MainWindow.axaml 工具坞的分隔线位置一致的四组（+ Select 独占组）。</summary>
    public static readonly IReadOnlyList<Entry> All = new[]
    {
        new Entry(ToolKind.Select, 0),
        new Entry(ToolKind.Rectangle, 1),
        new Entry(ToolKind.Ellipse, 1),
        new Entry(ToolKind.Line, 1),
        new Entry(ToolKind.ArrowStraight, 1),
        new Entry(ToolKind.ArrowCurved, 1),
        new Entry(ToolKind.Text, 2),
        new Entry(ToolKind.Number, 2),
        new Entry(ToolKind.Spotlight, 2),
        new Entry(ToolKind.Pencil, 3),
        new Entry(ToolKind.Highlighter, 3),
        new Entry(ToolKind.Mosaic, 3),
        new Entry(ToolKind.Blur, 3),
        new Entry(ToolKind.Invert, 3),
        new Entry(ToolKind.Crop, 4),
    };
}
