using System;
using System.Windows.Media.Media3D;

namespace TrikiReader
{
    public readonly record struct VisualOrientation(double Pitch, double Roll, double Yaw, Matrix3D Transform);

    public interface IVisualOrientationMapper
    {
        VisualOrientation Update(ImuSample sample);
        void ResetForNewStream(
            int minimumStabilizationSamples = 50,
            int stableWindowSamples = 10,
            int maximumStabilizationSamples = 200);
        void Reset();
    }

    public enum OrientationMode
    {
        Madgwick,
        ZappkaLikePitchRoll
    }

    public static class VisualOrientationMapperFactory
    {
        public static IVisualOrientationMapper Create(OrientationMode mode)
        {
            return mode switch
            {
                OrientationMode.Madgwick => new VisualOrientationMapper(),
                OrientationMode.ZappkaLikePitchRoll => new ComplementaryTiltOrientationMapper(),
                _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown orientation mode.")
            };
        }
    }

    public sealed class VisualOrientationMapper : IVisualOrientationMapper
    {
        private const double AutoZeroGyroStillThreshold = 2.0;
        private const double AutoZeroMinimumAccelMagnitude = 0.85;
        private const double AutoZeroMaximumAccelMagnitude = 1.15;
        private const double DefaultVisualDeadbandDegrees = 8.0;

        private readonly MadgwickAHRS _ahrs;
        private readonly double _gyroGain;
        private readonly double _fallbackDeltaSeconds;
        private readonly double _minimumDeltaSeconds;
        private readonly double _smoothingFactor;
        private readonly double _visualDeadbandDegrees;
        private DateTimeOffset? _lastTimestamp;
        
        private Quaternion _offset = Quaternion.Identity;
        private Quaternion _smoothedQuat = Quaternion.Identity;
        private bool _isFirstSample = true;
        private bool _isAutoZeroPending;
        private int _autoZeroSampleCount;
        private int _autoZeroStableSampleCount;
        private int _autoZeroMinimumSamples;
        private int _autoZeroRequiredStableSamples;
        private int _autoZeroMaximumSamples;

        public VisualOrientationMapper(
            double gyroGain = 2.5,
            double fallbackDeltaSeconds = 0.02,
            double minimumDeltaSeconds = 0.001,
            double beta = 1.5,
            double smoothingFactor = 0.35,
            double visualDeadbandDegrees = DefaultVisualDeadbandDegrees)
        {
            if (gyroGain <= 0.0)
                throw new ArgumentOutOfRangeException(nameof(gyroGain), "Gyro gain must be greater than zero.");
            if (fallbackDeltaSeconds <= 0.0)
                throw new ArgumentOutOfRangeException(nameof(fallbackDeltaSeconds), "Fallback sample period must be greater than zero.");
            if (minimumDeltaSeconds < 0.0)
                throw new ArgumentOutOfRangeException(nameof(minimumDeltaSeconds), "Minimum sample period cannot be negative.");
            if (smoothingFactor <= 0.0 || smoothingFactor > 1.0)
                throw new ArgumentOutOfRangeException(nameof(smoothingFactor), "Smoothing factor must be between (0, 1].");
            if (visualDeadbandDegrees < 0.0)
                throw new ArgumentOutOfRangeException(nameof(visualDeadbandDegrees), "Visual deadband cannot be negative.");

            _ahrs = new MadgwickAHRS(beta);
            _gyroGain = gyroGain;
            _fallbackDeltaSeconds = fallbackDeltaSeconds;
            _minimumDeltaSeconds = minimumDeltaSeconds;
            _smoothingFactor = smoothingFactor;
            _visualDeadbandDegrees = visualDeadbandDegrees;

            // Initialize offset so that starting state is identity until first sample is processed and stabilized
            _offset = Quaternion.Identity;
        }

        public double Yaw { get; private set; }

        public VisualOrientation Update(ImuSample sample)
        {
            double dt = _fallbackDeltaSeconds;
            if (_lastTimestamp is not null)
            {
                dt = (sample.TimestampUtc - _lastTimestamp.Value).TotalSeconds;
                if (dt <= _minimumDeltaSeconds)
                {
                    dt = _fallbackDeltaSeconds;
                }
            }
            _lastTimestamp = sample.TimestampUtc;

            // Convert degrees/sec to radians/sec for Madgwick.
            // Applying gyroGain to all axes to compensate for hardware clipping/scale.
            double gx = sample.GyroX * _gyroGain * Math.PI / 180.0;
            double gy = sample.GyroY * _gyroGain * Math.PI / 180.0;
            double gz = sample.GyroZ * _gyroGain * Math.PI / 180.0;

            // Process AHRS update. Accelerometer should be in g's or m/s^2 (Madgwick normalizes it).
            _ahrs.Update(gx, gy, gz, sample.AccelX, sample.AccelY, sample.AccelZ, dt);

            var q = _ahrs.Quaternion;
            // Madgwick array is [w, x, y, z]. System.Windows.Media.Media3D.Quaternion is (X, Y, Z, W).
            var rawQuat = ToVisualQuaternion(q);

            if (_isAutoZeroPending)
            {
                return UpdateAutoZeroCalibration(sample, rawQuat);
            }

            // Apply reset offset (left multiply by conjugate). 
            // This ensures that if rawQuat == rawQuatAtReset, visualQuat becomes Identity.
            var targetQuat = ApplyVisualDeadband(_offset * rawQuat);

            // Smooth the rotation to filter out high-frequency vibrations
            if (_isFirstSample)
            {
                _smoothedQuat = targetQuat;
                _isFirstSample = false;
            }
            else
            {
                _smoothedQuat = Quaternion.Slerp(_smoothedQuat, targetQuat, _smoothingFactor);
            }

            // Extract approximate Euler angles for UI
            ExtractEulerAngles(_smoothedQuat, out double pitch, out double roll, out double yaw);
            Yaw = yaw;

            // Produce Matrix3D for the Viewport
            var matrix = Matrix3D.Identity;
            matrix.Rotate(_smoothedQuat);

            return new VisualOrientation(pitch, roll, yaw, matrix);
        }

        public void ResetForNewStream(
            int minimumStabilizationSamples = 50,
            int stableWindowSamples = 10,
            int maximumStabilizationSamples = 200)
        {
            if (minimumStabilizationSamples < 0)
                throw new ArgumentOutOfRangeException(nameof(minimumStabilizationSamples), "Minimum stabilization sample count cannot be negative.");
            if (stableWindowSamples <= 0)
                throw new ArgumentOutOfRangeException(nameof(stableWindowSamples), "Stable window sample count must be greater than zero.");
            if (maximumStabilizationSamples < minimumStabilizationSamples)
                throw new ArgumentOutOfRangeException(nameof(maximumStabilizationSamples), "Maximum stabilization sample count cannot be less than the minimum.");

            _ahrs.Reset();
            _lastTimestamp = null;
            _offset = Quaternion.Identity;
            _smoothedQuat = Quaternion.Identity;
            _isFirstSample = true;
            _isAutoZeroPending = true;
            _autoZeroSampleCount = 0;
            _autoZeroStableSampleCount = 0;
            _autoZeroMinimumSamples = minimumStabilizationSamples;
            _autoZeroRequiredStableSamples = stableWindowSamples;
            _autoZeroMaximumSamples = maximumStabilizationSamples;
            Yaw = 0.0;
        }

        public void Reset()
        {
            var q = _ahrs.Quaternion;
            var currentRawQuat = ToVisualQuaternion(q);
            currentRawQuat.Invert();
            _offset = currentRawQuat;
            _smoothedQuat = Quaternion.Identity;
            _isFirstSample = true;
            _isAutoZeroPending = false;
            _autoZeroSampleCount = 0;
            _autoZeroStableSampleCount = 0;
            Yaw = 0.0;
        }

        private VisualOrientation UpdateAutoZeroCalibration(ImuSample sample, Quaternion rawQuat)
        {
            _autoZeroSampleCount++;

            if (_autoZeroSampleCount > _autoZeroMinimumSamples)
            {
                _autoZeroStableSampleCount = IsStillForAutoZero(sample)
                    ? _autoZeroStableSampleCount + 1
                    : 0;
            }

            if (_autoZeroStableSampleCount >= _autoZeroRequiredStableSamples ||
                _autoZeroSampleCount >= _autoZeroMaximumSamples)
            {
                _offset = rawQuat;
                _offset.Invert();
                _smoothedQuat = Quaternion.Identity;
                _isFirstSample = true;
                _isAutoZeroPending = false;
            }

            Yaw = 0.0;
            return new VisualOrientation(0.0, 0.0, 0.0, Matrix3D.Identity);
        }

        private static bool IsStillForAutoZero(ImuSample sample)
        {
            var gyroMagnitude = Math.Sqrt(
                sample.GyroX * sample.GyroX +
                sample.GyroY * sample.GyroY +
                sample.GyroZ * sample.GyroZ);
            if (gyroMagnitude > AutoZeroGyroStillThreshold)
            {
                return false;
            }

            var accelMagnitude = Math.Sqrt(
                sample.AccelX * sample.AccelX +
                sample.AccelY * sample.AccelY +
                sample.AccelZ * sample.AccelZ);
            return accelMagnitude >= AutoZeroMinimumAccelMagnitude &&
                   accelMagnitude <= AutoZeroMaximumAccelMagnitude;
        }

        private static Quaternion ToVisualQuaternion(double[] madgwickQuaternion)
        {
            return new Quaternion(
                -madgwickQuaternion[1],
                madgwickQuaternion[2],
                -madgwickQuaternion[3],
                madgwickQuaternion[0]);
        }

        private Quaternion ApplyVisualDeadband(Quaternion q)
        {
            if (_visualDeadbandDegrees <= 0.0)
            {
                return q;
            }

            q.Normalize();
            if (q.W < 0.0)
            {
                q = new Quaternion(-q.X, -q.Y, -q.Z, -q.W);
            }

            var clampedW = Math.Clamp(q.W, -1.0, 1.0);
            var angleDegrees = 2.0 * Math.Acos(clampedW) * 180.0 / Math.PI;
            if (angleDegrees <= _visualDeadbandDegrees)
            {
                return Quaternion.Identity;
            }

            var axisLength = Math.Sqrt(q.X * q.X + q.Y * q.Y + q.Z * q.Z);
            if (axisLength <= double.Epsilon)
            {
                return Quaternion.Identity;
            }

            var axis = new Vector3D(q.X / axisLength, q.Y / axisLength, q.Z / axisLength);
            return new Quaternion(axis, angleDegrees - _visualDeadbandDegrees);
        }

        private static void ExtractEulerAngles(Quaternion q, out double pitch, out double roll, out double yaw)
        {
            // Note: Euler angle conversion from quaternion can be done in multiple conventions.
            // This is a standard ZYX/XYZ conversion.
            
            // Roll (x-axis rotation)
            double sinr_cosp = 2 * (q.W * q.X + q.Y * q.Z);
            double cosr_cosp = 1 - 2 * (q.X * q.X + q.Y * q.Y);
            roll = Math.Atan2(sinr_cosp, cosr_cosp) * 180.0 / Math.PI;

            // Pitch (y-axis rotation)
            double sinp = 2 * (q.W * q.Y - q.Z * q.X);
            if (Math.Abs(sinp) >= 1)
                pitch = Math.CopySign(Math.PI / 2, sinp) * 180.0 / Math.PI; // use 90 degrees if out of range
            else
                pitch = Math.Asin(sinp) * 180.0 / Math.PI;

            // Yaw (z-axis rotation)
            double siny_cosp = 2 * (q.W * q.Z + q.X * q.Y);
            double cosy_cosp = 1 - 2 * (q.Y * q.Y + q.Z * q.Z);
            yaw = Math.Atan2(siny_cosp, cosy_cosp) * 180.0 / Math.PI;
        }
    }
}
