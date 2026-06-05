using System;
using Xunit;

namespace TrikiReader.Tests;

public sealed class ComplementaryTiltOrientationMapperTests
{
    [Fact]
    public void Create_DefaultMode_ReturnsMadgwickMapper()
    {
        var mapper = VisualOrientationMapperFactory.Create(OrientationMode.Madgwick);

        Assert.IsType<VisualOrientationMapper>(mapper);
    }

    [Fact]
    public void Create_ZappkaLikeMode_ReturnsComplementaryTiltMapper()
    {
        var mapper = VisualOrientationMapperFactory.Create(OrientationMode.ZappkaLikePitchRoll);

        Assert.IsType<ComplementaryTiltOrientationMapper>(mapper);
    }

    [Fact]
    public void Update_FlatOrientation_RemainsNearIdentity()
    {
        var mapper = new ComplementaryTiltOrientationMapper(
            gyroGain: 1.0,
            smoothingFactor: 1.0,
            visualDeadbandDegrees: 0.0);
        VisualOrientation orientation = default;

        for (var i = 0; i < 100; i++)
        {
            orientation = mapper.Update(Sample(DateTimeOffset.UnixEpoch.AddSeconds(i * 0.02)));
        }

        Assert.InRange(orientation.Pitch, -1.0, 1.0);
        Assert.InRange(orientation.Roll, -1.0, 1.0);
        Assert.InRange(orientation.Yaw, -1.0, 1.0);
    }

    [Fact]
    public void Update_IntegratesYawFromGyroZWithExistingVisualDirection()
    {
        var mapper = new ComplementaryTiltOrientationMapper(
            gyroGain: 1.0,
            smoothingFactor: 1.0,
            visualDeadbandDegrees: 0.0);
        VisualOrientation orientation = default;

        for (var i = 0; i < 100; i++)
        {
            orientation = mapper.Update(Sample(
                DateTimeOffset.UnixEpoch.AddSeconds(i * 0.02),
                gyroZ: 30.0));
        }

        Assert.InRange(orientation.Yaw, -65.0, -55.0);
        Assert.InRange(orientation.Pitch, -5.0, 5.0);
        Assert.InRange(orientation.Roll, -5.0, 5.0);
    }

    [Fact]
    public void ResetForNewStream_HoldsZeroDuringStartupCalibration()
    {
        var mapper = new ComplementaryTiltOrientationMapper();

        mapper.ResetForNewStream(
            minimumStabilizationSamples: 3,
            stableWindowSamples: 1,
            maximumStabilizationSamples: 10);

        AssertZero(mapper.Update(Sample(DateTimeOffset.UnixEpoch)));
        AssertZero(mapper.Update(Sample(DateTimeOffset.UnixEpoch.AddSeconds(0.02))));
        AssertZero(mapper.Update(Sample(DateTimeOffset.UnixEpoch.AddSeconds(0.04))));

        var afterCalibration = mapper.Update(Sample(DateTimeOffset.UnixEpoch.AddSeconds(0.06)));

        Assert.InRange(afterCalibration.Pitch, -5.0, 5.0);
        Assert.InRange(afterCalibration.Roll, -5.0, 5.0);
        Assert.InRange(afterCalibration.Yaw, -5.0, 5.0);
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

    private static void AssertZero(VisualOrientation orientation)
    {
        Assert.InRange(orientation.Pitch, -0.001, 0.001);
        Assert.InRange(orientation.Roll, -0.001, 0.001);
        Assert.InRange(orientation.Yaw, -0.001, 0.001);
    }
}
