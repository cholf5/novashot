using Novashot.Core.Annotation.Editing;
using SkiaSharp;

namespace Novashot.Core.Tests;

public class PolylineGeometryTests
{
    [Fact]
    public void DistanceTo_segment_midpoint_is_zero()
    {
        var d = PolylineGeometry.DistanceToSegment(new SKPoint(30, 10), new SKPoint(10, 10), new SKPoint(50, 10));
        Assert.Equal(0f, d, 3);
    }

    [Fact]
    public void DistanceTo_segment_clamps_foot_to_endpoints()
    {
        var a = new SKPoint(10, 10);
        var b = new SKPoint(50, 10);
        // 超出 B 端：垂足被 clamp 到 B，距离 = 5
        Assert.Equal(5f, PolylineGeometry.DistanceToSegment(new SKPoint(55, 10), a, b), 3);
        // 超出 A 端
        Assert.Equal(5f, PolylineGeometry.DistanceToSegment(new SKPoint(5, 10), a, b), 3);
    }

    [Fact]
    public void DistanceTo_zero_length_segment_is_point_distance()
    {
        var p = new SKPoint(10, 10);
        Assert.Equal(5f, PolylineGeometry.DistanceToSegment(new SKPoint(13, 14), p, p), 3);
    }

    [Fact]
    public void DistanceTo_polyline_takes_minimum_over_segments()
    {
        var pts = new[]
        {
            new SKPoint(0, 0),
            new SKPoint(0, 20),
            new SKPoint(20, 20),
        };
        // 距第 1 段 5px，距第 2 段 10px → 5
        Assert.Equal(5f, PolylineGeometry.DistanceToPolyline(new SKPoint(5, 10), pts), 3);
        // 靠近第 2 段中点
        Assert.Equal(2f, PolylineGeometry.DistanceToPolyline(new SKPoint(10, 18), pts), 3);
    }

    [Fact]
    public void DistanceTo_polyline_handles_single_and_empty()
    {
        var one = new[] { new SKPoint(3, 4) };
        Assert.Equal(5f, PolylineGeometry.DistanceToPolyline(new SKPoint(6, 8), one), 3);
        Assert.True(float.IsPositiveInfinity(PolylineGeometry.DistanceToPolyline(new SKPoint(0, 0), Array.Empty<SKPoint>())));
    }

    [Fact]
    public void PathLength_sums_segments()
    {
        var pts = new[] { new SKPoint(0, 0), new SKPoint(3, 4), new SKPoint(3, 10) };
        // 5 + 6
        Assert.Equal(11f, PolylineGeometry.PathLength(pts), 3);
    }

    [Fact]
    public void PathLength_of_fewer_than_two_points_is_zero()
    {
        Assert.Equal(0f, PolylineGeometry.PathLength(Array.Empty<SKPoint>()), 3);
        Assert.Equal(0f, PolylineGeometry.PathLength(new[] { new SKPoint(1, 1) }), 3);
    }
}
