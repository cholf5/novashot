using Prtsc.Core.Annotation.Editing;

namespace Prtsc.Core.Tests;

public class StrokeWheelHelperTests
{
    [Fact]
    public void Positive_delta_increments_by_one()
    {
        Assert.Equal(5f, StrokeWheelHelper.Step(4f, +1.0));
        Assert.Equal(3f, StrokeWheelHelper.Step(2f, +0.1));  // any positive
    }

    [Fact]
    public void Negative_delta_decrements_by_one()
    {
        Assert.Equal(3f, StrokeWheelHelper.Step(4f, -1.0));
        Assert.Equal(1f, StrokeWheelHelper.Step(2f, -0.1));
    }

    [Fact]
    public void Zero_delta_keeps_current()
    {
        Assert.Equal(4f, StrokeWheelHelper.Step(4f, 0.0));
    }

    [Fact]
    public void Clamps_to_min_when_decrementing_at_floor()
    {
        Assert.Equal(1f, StrokeWheelHelper.Step(1f, -3.0));
    }

    [Fact]
    public void Clamps_to_max_when_incrementing_at_ceiling()
    {
        Assert.Equal(16f, StrokeWheelHelper.Step(16f, +3.0));
    }

    [Fact]
    public void Honors_custom_bounds()
    {
        Assert.Equal(5f, StrokeWheelHelper.Step(5f, +5.0, min: 1f, max: 5f));
        Assert.Equal(2f, StrokeWheelHelper.Step(2f, -5.0, min: 2f, max: 10f));
    }
}
