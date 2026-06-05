using System;
using System.Globalization;
using System.Windows.Media.Media3D;
using Xunit;

namespace TrikiReader.Tests;

public sealed class VisualOrientationMapperTests
{
    [Fact]
    public void Update_FlatOrientation_RemainsNearIdentity()
    {
        var mapper = new VisualOrientationMapper();
        VisualOrientation orientation = default;

        // Feed stable flat samples
        for (int i = 0; i < 100; i++)
        {
            orientation = mapper.Update(Sample(DateTimeOffset.UnixEpoch.AddSeconds(i * 0.02), accelZ: -1.0));
        }

        Assert.Equal(0.0, orientation.Pitch, precision: 1);
        Assert.Equal(0.0, orientation.Roll, precision: 1);
        Assert.Equal(0.0, orientation.Yaw, precision: 1);
    }

    [Fact]
    public void Update_IntegratesYawFromGyroZWithVisualGain()
    {
        var mapper = new VisualOrientationMapper(gyroGain: 2.0, smoothingFactor: 1.0, visualDeadbandDegrees: 0.0);
        VisualOrientation orientation = default;

        // 100 samples of 0.02s = 2.0 seconds. GyroZ = 30 deg/s. Gain = 2.0. Total = 30 * 2.0 * 2.0 = 120 degrees yaw.
        for (int i = 0; i < 100; i++)
        {
            orientation = mapper.Update(Sample(
                DateTimeOffset.UnixEpoch.AddSeconds(i * 0.02),
                gyroZ: 30.0,
                accelZ: -1.0));
        }

        // Visual Z is intentionally inverted to match the physical device direction.
        Assert.InRange(orientation.Yaw, -130.0, -110.0);
        Assert.InRange(orientation.Pitch, -5.0, 5.0);
        Assert.InRange(orientation.Roll, -5.0, 5.0);
    }

    [Fact]
    public void Update_VisualAxisMappingInvertsXAndZButKeepsY()
    {
        var x = IntegrateSingleAxis(gyroX: 30.0);
        var y = IntegrateSingleAxis(gyroY: 30.0);
        var z = IntegrateSingleAxis(gyroZ: 30.0);

        Assert.InRange(x.Roll, -7.0, -5.0);
        Assert.InRange(y.Pitch, 5.0, 7.0);
        Assert.InRange(z.Yaw, -7.0, -5.0);
    }

    [Fact]
    public void Reset_ClearsRelativeOrientation()
    {
        var mapper = new VisualOrientationMapper(gyroGain: 2.0);
        VisualOrientation orientation = default;

        // Move to some random orientation
        for (int i = 0; i < 100; i++)
        {
            orientation = mapper.Update(Sample(
                DateTimeOffset.UnixEpoch.AddSeconds(i * 0.02),
                gyroZ: 60.0,
                accelX: 0.5,
                accelZ: -0.8));
        }

        // Assert it moved
        Assert.NotEqual(0.0, orientation.Yaw, precision: 1);

        // Reset
        mapper.Reset();

        // Feed one more sample with no movement to stabilize
        orientation = mapper.Update(Sample(
            DateTimeOffset.UnixEpoch.AddSeconds(101 * 0.02),
            gyroZ: 0.0,
            accelX: 0.5,
            accelZ: -0.8));

        // Orientation should be roughly near zero. High beta causes a small jump towards gravity.
        Assert.InRange(orientation.Yaw, -10.0, 10.0);
        Assert.InRange(orientation.Pitch, -10.0, 10.0);
        Assert.InRange(orientation.Roll, -10.0, 10.0);
        AssertVector(new Vector3D(1, 0, 0), orientation.Transform.Transform(new Vector3D(1, 0, 0)), tolerance: 0.1);
    }

    [Fact]
    public void ResetForNewStream_HoldsZeroUntilMinimumSamplesEvenWhenStable()
    {
        var mapper = new VisualOrientationMapper(gyroGain: 2.0);

        mapper.ResetForNewStream(
            minimumStabilizationSamples: 3,
            stableWindowSamples: 1,
            maximumStabilizationSamples: 10);

        var first = mapper.Update(Sample(DateTimeOffset.UnixEpoch));
        var second = mapper.Update(Sample(DateTimeOffset.UnixEpoch.AddSeconds(0.02)));
        var third = mapper.Update(Sample(DateTimeOffset.UnixEpoch.AddSeconds(0.04)));
        var afterCalibration = mapper.Update(Sample(DateTimeOffset.UnixEpoch.AddSeconds(0.06)));

        AssertZeroOrientation(first);
        AssertZeroOrientation(second);
        AssertZeroOrientation(third);
        Assert.InRange(afterCalibration.Yaw, -5.0, 5.0);
        Assert.InRange(afterCalibration.Pitch, -5.0, 5.0);
        Assert.InRange(afterCalibration.Roll, -5.0, 5.0);
    }

    [Fact]
    public void ResetForNewStream_ClearsPreviousTimestampAndOrientation()
    {
        var mapper = new VisualOrientationMapper(gyroGain: 2.0);

        for (int i = 0; i < 100; i++)
        {
            mapper.Update(Sample(DateTimeOffset.UnixEpoch.AddSeconds(i * 0.02), gyroZ: 60.0));
        }

        mapper.ResetForNewStream(
            minimumStabilizationSamples: 1,
            stableWindowSamples: 1,
            maximumStabilizationSamples: 5);

        var calibrated = mapper.Update(Sample(DateTimeOffset.UnixEpoch.AddMinutes(5), gyroZ: 60.0));
        var next = mapper.Update(Sample(DateTimeOffset.UnixEpoch.AddMinutes(5).AddSeconds(0.02)));

        AssertZeroOrientation(calibrated);
        Assert.InRange(next.Yaw, -5.0, 5.0);
        Assert.InRange(next.Pitch, -5.0, 5.0);
        Assert.InRange(next.Roll, -5.0, 5.0);
    }

    [Fact]
    public void ResetForNewStream_WaitsForStableWindowAfterMinimumWhenMoving()
    {
        var mapper = new VisualOrientationMapper(gyroGain: 2.0);

        mapper.ResetForNewStream(
            minimumStabilizationSamples: 2,
            stableWindowSamples: 2,
            maximumStabilizationSamples: 8);

        AssertZeroOrientation(mapper.Update(Sample(DateTimeOffset.UnixEpoch)));
        AssertZeroOrientation(mapper.Update(Sample(DateTimeOffset.UnixEpoch.AddSeconds(0.02))));
        AssertZeroOrientation(mapper.Update(Sample(DateTimeOffset.UnixEpoch.AddSeconds(0.04), gyroZ: 30.0)));
        AssertZeroOrientation(mapper.Update(Sample(DateTimeOffset.UnixEpoch.AddSeconds(0.06), gyroZ: 30.0)));
        AssertZeroOrientation(mapper.Update(Sample(DateTimeOffset.UnixEpoch.AddSeconds(0.08))));
        AssertZeroOrientation(mapper.Update(Sample(DateTimeOffset.UnixEpoch.AddSeconds(0.10))));

        var afterCalibration = mapper.Update(Sample(DateTimeOffset.UnixEpoch.AddSeconds(0.12)));

        Assert.InRange(afterCalibration.Yaw, -5.0, 5.0);
        Assert.InRange(afterCalibration.Pitch, -5.0, 5.0);
        Assert.InRange(afterCalibration.Roll, -5.0, 5.0);
    }

    [Fact]
    public void ResetForNewStream_FallsBackAtMaximumSamplesWhenStillMoving()
    {
        var mapper = new VisualOrientationMapper(gyroGain: 2.0, smoothingFactor: 1.0);

        mapper.ResetForNewStream(
            minimumStabilizationSamples: 2,
            stableWindowSamples: 3,
            maximumStabilizationSamples: 5);

        for (int i = 0; i < 5; i++)
        {
            AssertZeroOrientation(mapper.Update(Sample(
                DateTimeOffset.UnixEpoch.AddSeconds(i * 0.02),
                gyroZ: 300.0)));
        }

        var afterFallback = mapper.Update(Sample(DateTimeOffset.UnixEpoch.AddSeconds(0.10), gyroZ: 300.0));

        Assert.InRange(Math.Abs(afterFallback.Yaw), 0.1, 10.0);
    }

    [Fact]
    public void ResetForNewStream_RealCalmStartupCsvDoesNotDriftAfterCalibration()
    {
        var mapper = new VisualOrientationMapper();
        VisualOrientation orientation = default;

        mapper.ResetForNewStream();

        foreach (var sample in RealCalmStartupSamples(count: 220))
        {
            orientation = mapper.Update(sample);
        }

        AssertZeroOrientation(orientation);
    }

    [Fact]
    public void Update_VisualDeadbandSuppressesSmallStableMotion()
    {
        var mapper = new VisualOrientationMapper(gyroGain: 2.0, smoothingFactor: 1.0);
        VisualOrientation orientation = default;

        for (int i = 0; i < 40; i++)
        {
            orientation = mapper.Update(Sample(
                DateTimeOffset.UnixEpoch.AddSeconds(i * 0.02),
                gyroZ: 1.0));
        }

        AssertZeroOrientation(orientation);
    }

    [Fact]
    public void Update_DefaultMappingFollowsIntentionalMotionWithoutVisibleLag()
    {
        var mapper = new VisualOrientationMapper();
        VisualOrientation orientation = default;

        for (int i = 0; i < 15; i++)
        {
            orientation = mapper.Update(Sample(
                DateTimeOffset.UnixEpoch.AddSeconds(i * 0.02),
                gyroZ: 90.0));
        }

        Assert.InRange(orientation.Yaw, -70.0, -50.0);
    }

    [Fact]
    public void ResetForNewStream_RejectsInvalidAdaptiveParameters()
    {
        var mapper = new VisualOrientationMapper();

        Assert.Throws<ArgumentOutOfRangeException>(() => mapper.ResetForNewStream(
            minimumStabilizationSamples: -1,
            stableWindowSamples: 1,
            maximumStabilizationSamples: 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => mapper.ResetForNewStream(
            minimumStabilizationSamples: 1,
            stableWindowSamples: 0,
            maximumStabilizationSamples: 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => mapper.ResetForNewStream(
            minimumStabilizationSamples: 2,
            stableWindowSamples: 1,
            maximumStabilizationSamples: 1));
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

    private static VisualOrientation IntegrateSingleAxis(
        double gyroX = 0.0,
        double gyroY = 0.0,
        double gyroZ = 0.0)
    {
        var mapper = new VisualOrientationMapper(
            gyroGain: 1.0,
            beta: 0.0,
            smoothingFactor: 1.0,
            visualDeadbandDegrees: 0.0);
        VisualOrientation orientation = default;

        for (int i = 0; i < 10; i++)
        {
            orientation = mapper.Update(Sample(
                DateTimeOffset.UnixEpoch.AddSeconds(i * 0.02),
                gyroX: gyroX,
                gyroY: gyroY,
                gyroZ: gyroZ));
        }

        return orientation;
    }

    private static IEnumerable<ImuSample> RealCalmStartupSamples(int count)
    {
        return File.ReadLines(FindTrikiDataCsv())
            .Skip(1)
            .Take(count)
            .Select(ParseCsvSample)
            .ToArray();
    }

    private static string FindTrikiDataCsv()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "triki_data.csv");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not find triki_data.csv in test output ancestors.");
    }

    private static ImuSample ParseCsvSample(string line)
    {
        var fields = line.Split(',');
        return new ImuSample(
            long.Parse(fields[0], CultureInfo.InvariantCulture),
            DateTimeOffset.Parse(fields[1], CultureInfo.InvariantCulture),
            double.Parse(fields[2], CultureInfo.InvariantCulture),
            double.Parse(fields[3], CultureInfo.InvariantCulture),
            double.Parse(fields[4], CultureInfo.InvariantCulture),
            double.Parse(fields[5], CultureInfo.InvariantCulture),
            double.Parse(fields[6], CultureInfo.InvariantCulture),
            double.Parse(fields[7], CultureInfo.InvariantCulture),
            short.Parse(fields[8], CultureInfo.InvariantCulture),
            short.Parse(fields[9], CultureInfo.InvariantCulture),
            short.Parse(fields[10], CultureInfo.InvariantCulture),
            short.Parse(fields[11], CultureInfo.InvariantCulture),
            short.Parse(fields[12], CultureInfo.InvariantCulture),
            short.Parse(fields[13], CultureInfo.InvariantCulture));
    }

    private static void AssertVector(Vector3D expected, Vector3D actual, double tolerance = 0.000001)
    {
        Assert.InRange(actual.X, expected.X - tolerance, expected.X + tolerance);
        Assert.InRange(actual.Y, expected.Y - tolerance, expected.Y + tolerance);
        Assert.InRange(actual.Z, expected.Z - tolerance, expected.Z + tolerance);
    }

    private static void AssertZeroOrientation(VisualOrientation orientation)
    {
        Assert.InRange(orientation.Pitch, -0.001, 0.001);
        Assert.InRange(orientation.Roll, -0.001, 0.001);
        Assert.InRange(orientation.Yaw, -0.001, 0.001);
        AssertVector(new Vector3D(1, 0, 0), orientation.Transform.Transform(new Vector3D(1, 0, 0)), tolerance: 0.0001);
        AssertVector(new Vector3D(0, 1, 0), orientation.Transform.Transform(new Vector3D(0, 1, 0)), tolerance: 0.0001);
        AssertVector(new Vector3D(0, 0, 1), orientation.Transform.Transform(new Vector3D(0, 0, 1)), tolerance: 0.0001);
    }
}
