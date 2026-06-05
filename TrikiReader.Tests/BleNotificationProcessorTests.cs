namespace TrikiReader.Tests;

public sealed class BleNotificationProcessorTests
{
    [Fact]
    public void Process_ParsesNotificationAndUpdatesSharedStats()
    {
        var stats = new ImuStats();
        var processor = new BleNotificationProcessor(Options(startupDiscardSamples: 0), stats);
        var timestamp = DateTimeOffset.Parse("2026-05-27T10:15:30+00:00");

        var samples = processor.Process(Frame(rawGyroX: 131), timestamp);

        var sample = Assert.Single(samples);
        Assert.Equal(timestamp, sample.TimestampUtc);
        Assert.Equal(1.0, sample.GyroX);
        Assert.Equal(1, stats.NotificationCount);
        Assert.Equal(1, stats.ParsedFrameCount);
        Assert.Equal(1, stats.WrittenSampleCount);
    }

    [Fact]
    public void Process_DoesNotInvokeSampleWorkForDiscardedStartupFrames()
    {
        var stats = new ImuStats();
        var processor = new BleNotificationProcessor(Options(startupDiscardSamples: 1), stats);
        var timestamp = DateTimeOffset.Parse("2026-05-27T10:15:30+00:00");

        var first = processor.Process(Frame(rawGyroX: 131), timestamp);
        var second = processor.Process(Frame(rawGyroX: 262), timestamp.AddMilliseconds(20));

        Assert.Empty(first);
        var sample = Assert.Single(second);
        Assert.Equal(2.0, sample.GyroX);
        Assert.Equal(2, stats.NotificationCount);
        Assert.Equal(2, stats.ParsedFrameCount);
        Assert.Equal(1, stats.DiscardedStartupSampleCount);
        Assert.Equal(1, stats.WrittenSampleCount);
    }

    [Fact]
    public void Process_KeepsNotificationTimestampInsideBurst()
    {
        var stats = new ImuStats();
        var processor = new BleNotificationProcessor(Options(startupDiscardSamples: 0), stats);
        var timestamp = DateTimeOffset.Parse("2026-05-27T10:15:30+00:00");
        var burst = Frame(131)
            .Concat(Frame(262))
            .Concat(Frame(393))
            .Concat(Frame(524))
            .ToArray();

        var samples = processor.Process(burst[..20], timestamp)
            .Concat(processor.Process(burst[20..40], timestamp))
            .Concat(processor.Process(burst[40..], timestamp))
            .ToArray();

        Assert.Equal(4, samples.Length);
        Assert.All(samples, sample => Assert.Equal(timestamp, sample.TimestampUtc));
        Assert.Equal(new[] { 1.0, 2.0, 3.0, 4.0 }, samples.Select(sample => sample.GyroX).ToArray());
    }

    private static AppOptions Options(int startupDiscardSamples)
    {
        return new AppOptions(
            "Triki",
            "triki_data.csv",
            131.0,
            2048.0,
            "triki_debug.log",
            0,
            Array.Empty<byte>(),
            20,
            startupDiscardSamples,
            30,
            3);
    }

    private static byte[] Frame(short rawGyroX)
    {
        var frame = new byte[14];
        frame[0] = 0x22;
        frame[1] = 0x00;
        frame[2] = (byte)(rawGyroX & 0xFF);
        frame[3] = (byte)((rawGyroX >> 8) & 0xFF);
        return frame;
    }
}
