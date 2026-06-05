namespace TrikiReader.Tests;

using System.Diagnostics;

public sealed class UiUpdateGateTests
{
    [Fact]
    public void TryBeginSchedule_AllowsOnlyOnePendingUpdate()
    {
        var gate = new UiUpdateGate();

        Assert.True(gate.TryBeginSchedule());
        Assert.False(gate.TryBeginSchedule());

        gate.Complete();

        Assert.True(gate.TryBeginSchedule());
    }

    [Fact]
    public void TryBeginSchedule_RespectsMinimumIntervalAfterCompletion()
    {
        var gate = new UiUpdateGate(TimeSpan.FromMilliseconds(33));
        var start = Stopwatch.Frequency;

        Assert.True(gate.TryBeginSchedule(start));
        gate.Complete();

        Assert.False(gate.TryBeginSchedule(start + Stopwatch.Frequency / 100));
        Assert.True(gate.TryBeginSchedule(start + Stopwatch.Frequency / 25));
    }
}
