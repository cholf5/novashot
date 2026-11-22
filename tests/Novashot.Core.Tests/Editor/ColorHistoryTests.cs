using Novashot.Core.Editor;
using SkiaSharp;

namespace Novashot.Core.Tests;

/// <summary>
/// E-1 M9：会话级色值历史（非 config 持久化）。最近 10 色、去重、最新在前、事件通知。
/// </summary>
public class ColorHistoryTests
{
    private static SKColor Color(byte v) => new(v, v, v);

    [Fact]
    public void Capacity_IsTen()
    {
        Assert.Equal(10, ColorHistory.DefaultCapacity);
        Assert.Equal(10, new ColorHistory().Capacity);
    }

    [Fact]
    public void Add_NewColor_PutsItFirst()
    {
        var history = new ColorHistory();
        history.Add(Color(1));
        history.Add(Color(2));

        Assert.Equal(2, history.Colors.Count);
        Assert.Equal(Color(2), history.Colors[0]); // 最新在前
        Assert.Equal(Color(1), history.Colors[1]);
    }

    [Fact]
    public void Add_DuplicateColor_DedupesAndBumpsToFront()
    {
        var history = new ColorHistory();
        history.Add(Color(1));
        history.Add(Color(2));
        history.Add(Color(1)); // 重取旧色：只保留一份，且提到最新

        Assert.Equal(2, history.Colors.Count);
        Assert.Equal(Color(1), history.Colors[0]);
        Assert.Equal(Color(2), history.Colors[1]);
    }

    [Fact]
    public void Add_BeyondCapacity_DropsOldest()
    {
        var history = new ColorHistory();
        for (byte i = 0; i < 13; i++) history.Add(Color(i)); // 13 色 → 只剩最近 10

        Assert.Equal(10, history.Colors.Count);
        Assert.Equal(Color(12), history.Colors[0]);
        Assert.Equal(Color(3), history.Colors[^1]); // 0..2 被挤掉
        Assert.DoesNotContain(Color(2), history.Colors);
    }

    [Fact]
    public void Add_ComparedByRgbValue_NotByReference()
    {
        // SKColor 是值类型，但语义上仍要按 RGB 相等去重（alpha 不参与——取色结果一律不透明）
        var history = new ColorHistory();
        history.Add(new SKColor(10, 20, 30, 255));
        history.Add(new SKColor(10, 20, 30, 255));

        Assert.Single(history.Colors);
    }

    [Fact]
    public void Clear_RemovesAll()
    {
        var history = new ColorHistory();
        history.Add(Color(1));
        history.Clear();

        Assert.Empty(history.Colors);
    }

    [Fact]
    public void ChangedEvent_FiresOnAddDedupMoveAndClear()
    {
        var history = new ColorHistory();
        var counts = new List<int>();
        history.Changed += (_, _) => counts.Add(history.Colors.Count);

        history.Add(Color(1));
        history.Add(Color(2));
        history.Add(Color(1)); // 去重上浮也通知（面板顺序要刷新）
        history.Clear();

        Assert.Equal(4, counts.Count);
        Assert.Equal(new[] { 1, 2, 2, 0 }, counts);
    }

    [Fact]
    public void Colors_Snapshot_IsNotLiveView()
    {
        // 面板绑定拿到的是快照，后续 Add 不会从背后改已返回的列表
        var history = new ColorHistory();
        history.Add(Color(1));
        var snapshot = history.Colors;
        history.Add(Color(2));

        Assert.Single(snapshot);
        Assert.Equal(2, history.Colors.Count);
    }

    [Fact]
    public void Add_AfterClear_WorksNormally()
    {
        var history = new ColorHistory();
        history.Add(Color(1));
        history.Clear();
        history.Add(Color(2));

        Assert.Equal(new[] { Color(2) }, history.Colors);
    }
}
