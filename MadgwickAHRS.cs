using System;

namespace TrikiReader
{
    /// <summary>
    /// Madgwick's implementation of AHRS algorithm.
    /// See: http://www.x-io.co.uk/node/8#open_source_ahrs_and_imu_algorithms
    /// </summary>
    public class MadgwickAHRS
    {
        /// <summary>
        /// Gets or sets the algorithm gain beta.
        /// </summary>
        public double Beta { get; set; }

        /// <summary>
        /// Gets the quaternion representation of the rotation [w, x, y, z].
        /// </summary>
        public double[] Quaternion { get; private set; }

        public MadgwickAHRS(double beta = 0.1)
        {
            Beta = beta;
            Quaternion = new double[] { 1.0, 0.0, 0.0, 0.0 };
        }

        public void Update(double gx, double gy, double gz, double ax, double ay, double az, double dt)
        {
            if (dt <= 0.0) return;

            double q1 = Quaternion[0], q2 = Quaternion[1], q3 = Quaternion[2], q4 = Quaternion[3];
            double norm;
            double s1, s2, s3, s4;
            double qDot1, qDot2, qDot3, qDot4;

            // Auxiliary variables to avoid repeated arithmetic
            double _2q1 = 2.0 * q1;
            double _2q2 = 2.0 * q2;
            double _2q3 = 2.0 * q3;
            double _2q4 = 2.0 * q4;
            double _4q1 = 4.0 * q1;
            double _4q2 = 4.0 * q2;
            double _4q3 = 4.0 * q3;
            double _8q2 = 8.0 * q2;
            double _8q3 = 8.0 * q3;
            double q1q1 = q1 * q1;
            double q2q2 = q2 * q2;
            double q3q3 = q3 * q3;
            double q4q4 = q4 * q4;

            // Normalise accelerometer measurement
            if (ax == 0.0 && ay == 0.0 && az == 0.0) return; // handle NaN
            norm = Math.Sqrt(ax * ax + ay * ay + az * az);
            ax /= norm;
            ay /= norm;
            az /= norm;

            // Gradient decent algorithm corrective step
            s1 = _4q1 * q3q3 + _2q3 * ax + _4q1 * q2q2 - _2q2 * ay;
            s2 = _4q2 * q4q4 - _2q4 * ax + 4.0 * q1q1 * q2 - _2q1 * ay - _4q2 + _8q2 * q2q2 + _8q2 * q3q3 + _4q2 * az;
            s3 = 4.0 * q1q1 * q3 + _2q1 * ax + _4q3 * q4q4 - _2q4 * ay - _4q3 + _8q3 * q2q2 + _8q3 * q3q3 + _4q3 * az;
            s4 = 4.0 * q2q2 * q4 - _2q2 * ax + 4.0 * q3q3 * q4 - _2q3 * ay;

            // Normalise step magnitude
            norm = Math.Sqrt(s1 * s1 + s2 * s2 + s3 * s3 + s4 * s4);
            if (norm > 0)
            {
                s1 /= norm;
                s2 /= norm;
                s3 /= norm;
                s4 /= norm;
            }

            // Compute rate of change of quaternion
            qDot1 = 0.5 * (-q2 * gx - q3 * gy - q4 * gz) - Beta * s1;
            qDot2 = 0.5 * (q1 * gx + q3 * gz - q4 * gy) - Beta * s2;
            qDot3 = 0.5 * (q1 * gy - q2 * gz + q4 * gx) - Beta * s3;
            qDot4 = 0.5 * (q1 * gz + q2 * gy - q3 * gx) - Beta * s4;

            // Integrate to yield quaternion
            q1 += qDot1 * dt;
            q2 += qDot2 * dt;
            q3 += qDot3 * dt;
            q4 += qDot4 * dt;

            // Normalise quaternion
            norm = Math.Sqrt(q1 * q1 + q2 * q2 + q3 * q3 + q4 * q4);
            Quaternion[0] = q1 / norm;
            Quaternion[1] = q2 / norm;
            Quaternion[2] = q3 / norm;
            Quaternion[3] = q4 / norm;
        }

        public void Reset()
        {
            Quaternion[0] = 1.0;
            Quaternion[1] = 0.0;
            Quaternion[2] = 0.0;
            Quaternion[3] = 0.0;
        }
    }
}
