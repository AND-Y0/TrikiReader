namespace TrikiReader.Tests;

public sealed class ImuOrientationFilterTests
{
    [Fact]
    public void Update_InitializesPitchAndRollFromAccelerometer()
    {
        var filter = new ImuOrientationFilter(alpha: 0.98);
        var sample = Sample(
            timestamp: DateTimeOffset.UnixEpoch,
            accelX: 1.0,
            accelY: 0.0,
            accelZ: -1.0);

        var orientation = filter.Update(sample);

        Assert.Equal(45.0, orientation.Pitch, precision: 6);
        Assert.Equal(0.0, orientation.Roll, precision: 6);
        Assert.Equal(0.0, orientation.Yaw, precision: 6);
    }

    [Fact]
    public void Update_BlendsGyroIntegrationWithAccelerometerCorrection()
    {
        var filter = new ImuOrientationFilter(alpha: 0.98);
        filter.Update(Sample(DateTimeOffset.UnixEpoch));

        var orientation = filter.Update(Sample(
            timestamp: DateTimeOffset.UnixEpoch.AddSeconds(1),
            gyroX: 90.0,
            accelX: 0.0,
            accelY: 0.0,
            accelZ: -1.0));

        Assert.Equal(88.2, orientation.Roll, precision: 6);
        Assert.Equal(0.0, orientation.Pitch, precision: 6);
    }

    [Fact]
    public void Update_IntegratesYawFromGyroZ()
    {
        var filter = new ImuOrientationFilter(alpha: 0.98);
        filter.Update(Sample(DateTimeOffset.UnixEpoch));

        var orientation = filter.Update(Sample(
            timestamp: DateTimeOffset.UnixEpoch.AddSeconds(0.5),
            gyroZ: 60.0));

        Assert.Equal(75.0, orientation.Yaw, precision: 6);
    }

    [Fact]
    public void Update_DistinguishesNarrowTopFlatFromWideBaseFlat()
    {
        var filter = new ImuOrientationFilter(alpha: 0.98);

        var orientation = filter.Update(Sample(
            timestamp: DateTimeOffset.UnixEpoch,
            accelX: 0.0,
            accelY: 0.0,
            accelZ: 1.0));

        Assert.Equal(180.0, orientation.Roll, precision: 6);
    }

    [Fact]
    public void Reset_ClearsOrientationAndTimestamp()
    {
        var filter = new ImuOrientationFilter(alpha: 0.98);
        filter.Update(Sample(DateTimeOffset.UnixEpoch));
        filter.Update(Sample(DateTimeOffset.UnixEpoch.AddSeconds(1), gyroZ: 60.0));

        filter.Reset();

        Assert.Equal(0.0, filter.Pitch, precision: 6);
        Assert.Equal(0.0, filter.Roll, precision: 6);
        Assert.Equal(0.0, filter.Yaw, precision: 6);

        var orientation = filter.Update(Sample(
            timestamp: DateTimeOffset.UnixEpoch.AddSeconds(10),
            gyroZ: 60.0));

        Assert.Equal(0.0, orientation.Yaw, precision: 6);
    }

    private static ImuSample Sample(
        DateTimeOffset timestamp,
        double gyroX = 0.0,
        double gyroY = 0.0,
        double gyroZ = 0.0,
        double accelX = 0.0,
        double accelY = 0.0,
        double accelZ = -1.0)
    {
        return new ImuSample(
            0,
            timestamp,
            gyroX,
            gyroY,
            gyroZ,
            accelX,
            accelY,
            accelZ,
            0,
            0,
            0,
            0,
            0,
            0);
    }
}
