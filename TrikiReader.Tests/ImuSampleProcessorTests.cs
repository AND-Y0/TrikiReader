namespace TrikiReader.Tests;

public sealed class ImuSampleProcessorTests
{
    [Fact]
    public void ProcessFrame_DiscardsStartupSamplesBeforeWriting()
    {
        var processor = new ImuSampleProcessor(Options(startupDiscardSamples: 2));
        var frame = Frame(rawGyroX: 131);

        Assert.Null(processor.ProcessFrame(frame));
        Assert.Null(processor.ProcessFrame(frame));
        var sample = processor.ProcessFrame(frame);

        Assert.NotNull(sample);
        Assert.Equal(3, processor.Stats.ParsedFrameCount);
        Assert.Equal(2, processor.Stats.DiscardedStartupSampleCount);
        Assert.Equal(1, processor.Stats.WrittenSampleCount);
        Assert.Equal(0, sample.Value.FrameIndex);
        Assert.Equal(1.0, sample.Value.GyroX);
    }

    [Fact]
    public void ProcessFrame_IncrementsFrameIndexOnlyForWrittenSamples()
    {
        var processor = new ImuSampleProcessor(Options(startupDiscardSamples: 1));
        var frame = Frame(rawGyroX: 262);

        Assert.Null(processor.ProcessFrame(frame));
        var first = processor.ProcessFrame(frame);
        var second = processor.ProcessFrame(frame);

        Assert.Equal(0, first!.Value.FrameIndex);
        Assert.Equal(1, second!.Value.FrameIndex);
    }

    [Fact]
    public void ProcessFrame_UsesNotificationTimestampForWrittenSample()
    {
        var processor = new ImuSampleProcessor(Options(startupDiscardSamples: 0));
        var timestamp = DateTimeOffset.Parse("2026-05-27T10:15:30+00:00");

        var sample = processor.ProcessFrame(Frame(rawGyroX: 131), timestamp);

        Assert.NotNull(sample);
        Assert.Equal(timestamp, sample.Value.TimestampUtc);
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
