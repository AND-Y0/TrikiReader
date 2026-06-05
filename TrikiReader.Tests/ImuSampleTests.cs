namespace TrikiReader.Tests;

public sealed class ImuSampleTests
{
    [Fact]
    public void FromFrame_ReadsSignedLittleEndianValuesAndAppliesScales()
    {
        var frame = new byte[]
        {
            0x22, 0x00,
            0x00, 0x01, // 256
            0x00, 0xFF, // -256
            0x7F, 0x00, // 127
            0x00, 0x08, // 2048
            0x00, 0xF8, // -2048
            0x00, 0x04  // 1024
        };

        var sample = ImuSample.FromFrame(frame, 42, gyroScale: 128.0, accelScale: 2048.0);

        Assert.Equal(42, sample.FrameIndex);
        Assert.Equal(256, sample.RawGyroX);
        Assert.Equal(-256, sample.RawGyroY);
        Assert.Equal(127, sample.RawGyroZ);
        Assert.Equal(2048, sample.RawAccelX);
        Assert.Equal(-2048, sample.RawAccelY);
        Assert.Equal(1024, sample.RawAccelZ);
        Assert.Equal(2.0, sample.GyroX);
        Assert.Equal(-2.0, sample.GyroY);
        Assert.Equal(127.0 / 128.0, sample.GyroZ);
        Assert.Equal(1.0, sample.AccelX);
        Assert.Equal(-1.0, sample.AccelY);
        Assert.Equal(0.5, sample.AccelZ);
    }

    [Fact]
    public void FromFrame_UsesProvidedNotificationTimestamp()
    {
        var frame = new byte[14];
        frame[0] = 0x22;
        frame[1] = 0x00;
        var timestamp = DateTimeOffset.Parse("2026-05-27T10:15:30+00:00");

        var sample = ImuSample.FromFrame(frame, 0, 131.0, 2048.0, timestamp);

        Assert.Equal(timestamp, sample.TimestampUtc);
    }
}
