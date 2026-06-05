namespace TrikiReader
{
    public readonly record struct ImuOrientation(double Pitch, double Roll, double Yaw);

    public sealed class ImuOrientationFilter
    {
        private readonly double _alpha;
        private readonly double _yawGain;
        private DateTimeOffset? _lastTimestamp;

        public ImuOrientationFilter(double alpha = 0.98, double yawGain = 2.5)
        {
            if (alpha < 0.0 || alpha > 1.0)
            {
                throw new ArgumentOutOfRangeException(nameof(alpha), "Alpha must be between 0 and 1.");
            }

            if (yawGain <= 0.0)
            {
                throw new ArgumentOutOfRangeException(nameof(yawGain), "Yaw gain must be greater than zero.");
            }

            _alpha = alpha;
            _yawGain = yawGain;
        }

        public double Pitch { get; private set; }
        public double Roll { get; private set; }
        public double Yaw { get; private set; }

        public ImuOrientation Update(ImuSample sample)
        {
            var accelPitch = CalculatePitchFromAccelerometer(sample);
            var accelRoll = CalculateRollFromAccelerometer(sample);

            if (_lastTimestamp is null)
            {
                Pitch = accelPitch;
                Roll = accelRoll;
                Yaw = 0.0;
                _lastTimestamp = sample.TimestampUtc;
                return Current;
            }

            var dt = Math.Max((sample.TimestampUtc - _lastTimestamp.Value).TotalSeconds, 0.0);
            _lastTimestamp = sample.TimestampUtc;

            var gyroPitch = Pitch + sample.GyroY * dt;
            var gyroRoll = Roll + sample.GyroX * dt;
            Yaw += sample.GyroZ * _yawGain * dt;

            Pitch = _alpha * gyroPitch + (1.0 - _alpha) * accelPitch;
            Roll = _alpha * gyroRoll + (1.0 - _alpha) * accelRoll;

            return Current;
        }

        public void Reset()
        {
            Pitch = 0.0;
            Roll = 0.0;
            Yaw = 0.0;
            _lastTimestamp = null;
        }

        private ImuOrientation Current => new(Pitch, Roll, Yaw);

        private static double CalculatePitchFromAccelerometer(ImuSample sample)
        {
            return Math.Atan2(
                sample.AccelX,
                Math.Sqrt(sample.AccelY * sample.AccelY + sample.AccelZ * sample.AccelZ)) * 180.0 / Math.PI;
        }

        private static double CalculateRollFromAccelerometer(ImuSample sample)
        {
            return Math.Atan2(sample.AccelY, -sample.AccelZ) * 180.0 / Math.PI;
        }
    }
}
