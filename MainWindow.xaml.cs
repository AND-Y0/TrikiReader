using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Media.Media3D;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;

namespace TrikiReader
{
    public partial class MainWindow : Window
    {
        private TrikiBleReader? _triki;
        private CancellationTokenSource? _cts;
        private IVisualOrientationMapper _visualOrientationMapper = VisualOrientationMapperFactory.Create(OrientationMode.Madgwick);
        private OrientationMode _activeOrientationMode = OrientationMode.Madgwick;
        public ObservableCollection<ISeries> GyroSeries { get; set; }
        private ObservableCollection<ObservablePoint> _gyroXPoints;
        private ObservableCollection<ObservablePoint> _gyroYPoints;
        private ObservableCollection<ObservablePoint> _gyroZPoints;

        private int _sampleCount = 0;
        private readonly Stopwatch _sampleStopwatch = new();
        private readonly UiUpdateGate _uiUpdateGate = new(TimeSpan.FromMilliseconds(33));
        private readonly object _latestUiSnapshotLock = new();
        private UiSampleSnapshot? _latestUiSnapshot;
        private long _latestUiSnapshotVersion;
        private long _appliedUiSnapshotVersion;
        private TrikiDeviceInfo _latestDeviceInfo = TrikiDeviceInfo.Empty;

        private readonly record struct UiSampleSnapshot(
            int SampleCount,
            ImuSample Sample,
            VisualOrientation Orientation,
            double Hertz,
            long NotificationCount,
            long ParsedFrameCount,
            long DiscardedStartupSampleCount,
            long DroppedByteCount,
            double LastNotificationGapMilliseconds,
            double MaxNotificationGapMilliseconds,
            OrientationMode OrientationMode,
            TrikiDeviceInfo DeviceInfo);

        public MainWindow()
        {
            InitializeComponent();
            DeviceBodyModel.Geometry = CreateBottleCapMesh();

            _gyroXPoints = new ObservableCollection<ObservablePoint>();
            _gyroYPoints = new ObservableCollection<ObservablePoint>();
            _gyroZPoints = new ObservableCollection<ObservablePoint>();
            GyroSeries = new ObservableCollection<ISeries>
            {
                new LineSeries<ObservablePoint>
                {
                    Values = _gyroXPoints,
                    Fill = null,
                    Name = "Gyro X (dps)",
                    GeometrySize = 0,
                    LineSmoothness = 0,
                    Stroke = new SolidColorPaint(SKColor.Parse("#E53935")) { StrokeThickness = 2 }
                },
                new LineSeries<ObservablePoint>
                {
                    Values = _gyroYPoints,
                    Fill = null,
                    Name = "Gyro Y (dps)",
                    GeometrySize = 0,
                    LineSmoothness = 0,
                    Stroke = new SolidColorPaint(SKColor.Parse("#43A047")) { StrokeThickness = 2 }
                },
                new LineSeries<ObservablePoint>
                {
                    Values = _gyroZPoints,
                    Fill = null,
                    Name = "Gyro Z (dps)",
                    GeometrySize = 0,
                    LineSmoothness = 0,
                    Stroke = new SolidColorPaint(SKColor.Parse("#1E88E5")) { StrokeThickness = 2 }
                }
            };
            
            DataContext = this;
        }

        private static MeshGeometry3D CreateBottleCapMesh()
        {
            const int segments = 48;
            const double topRadius = 0.78;
            const double bottomRadius = 1.02;
            const double ridgeDepth = 0.08;
            const double halfHeight = 0.18;

            var mesh = new MeshGeometry3D();
            var topCenter = AddPoint(mesh, 0, 0, halfHeight);
            var bottomCenter = AddPoint(mesh, 0, 0, -halfHeight);
            var top = new int[segments];
            var bottom = new int[segments];

            for (var i = 0; i < segments; i++)
            {
                var angle = 2.0 * Math.PI * i / segments;
                var ridge = i % 2 == 0 ? ridgeDepth : -ridgeDepth;
                var topSideRadius = topRadius + ridge * 0.55;
                var bottomSideRadius = bottomRadius + ridge;
                top[i] = AddPoint(mesh, topSideRadius * Math.Cos(angle), topSideRadius * Math.Sin(angle), halfHeight);
                bottom[i] = AddPoint(mesh, bottomSideRadius * Math.Cos(angle), bottomSideRadius * Math.Sin(angle), -halfHeight);
            }

            for (var i = 0; i < segments; i++)
            {
                var next = (i + 1) % segments;

                AddTriangle(mesh, topCenter, top[i], top[next]);
                AddTriangle(mesh, bottomCenter, bottom[next], bottom[i]);
                AddTriangle(mesh, top[i], bottom[i], bottom[next]);
                AddTriangle(mesh, bottom[next], top[next], top[i]);
            }

            return mesh;
        }

        private static int AddPoint(MeshGeometry3D mesh, double x, double y, double z)
        {
            mesh.Positions.Add(new Point3D(x, y, z));
            return mesh.Positions.Count - 1;
        }

        private static void AddTriangle(MeshGeometry3D mesh, int a, int b, int c)
        {
            mesh.TriangleIndices.Add(a);
            mesh.TriangleIndices.Add(b);
            mesh.TriangleIndices.Add(c);
        }

        private async void BtnConnect_Click(object sender, RoutedEventArgs e)
        {
            if (_triki != null)
            {
                _cts?.Cancel();
                BtnConnect.Content = "Połącz z Triki";
                CmbOrientationMode.IsEnabled = true;
                TxtStatus.Text = "Rozłączony";
                TxtStatus.Foreground = System.Windows.Media.Brushes.DarkGray;
                _triki = null;
                return;
            }

            BtnConnect.Content = "Rozłącz";
            CmbOrientationMode.IsEnabled = false;
            TxtStatus.Text = "Łączenie...";
            TxtStatus.Foreground = System.Windows.Media.Brushes.DarkOrange;
            TxtAngles.Text = "Oczekiwanie na próbki IMU";
            TxtImu.Text = "Gyro: --\nAccel: --";
            _latestDeviceInfo = TrikiDeviceInfo.Empty;
            TxtDiagnostics.Text = BuildWaitingDiagnosticsText(_latestDeviceInfo);
            _gyroXPoints.Clear();
            _gyroYPoints.Clear();
            _gyroZPoints.Clear();
            _sampleCount = 0;
            _sampleStopwatch.Restart();
            _activeOrientationMode = SelectedOrientationMode;
            _visualOrientationMapper = VisualOrientationMapperFactory.Create(_activeOrientationMode);
            _visualOrientationMapper.ResetForNewStream();
            ResetPendingUiSnapshot();
            
            _cts = new CancellationTokenSource();
            var options = AppOptions.Default;
            Log($"Tryb orientacji: {OrientationModeLabel(_activeOrientationMode)}");
            Log("Czas próbek: BLE notify");

            _triki = new TrikiBleReader(options);
            _triki.DeviceInfoReceived += Triki_DeviceInfoReceived;
            _triki.LogMessage += Triki_LogMessage;
            _triki.SampleReceived += Triki_SampleReceived;
            _triki.ConnectionLost += Triki_ConnectionLost;

            try
            {
                await _triki.RunAsync(_cts.Token);
            }
            catch (Exception ex)
            {
                Log("Błąd: " + ex.Message);
                CmbOrientationMode.IsEnabled = true;
            }
        }

        private void Triki_ConnectionLost(object? sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                TxtStatus.Text = "Rozłączony";
                TxtStatus.Foreground = System.Windows.Media.Brushes.DarkGray;
                BtnConnect.Content = "Połącz z Triki";
                CmbOrientationMode.IsEnabled = true;
                _triki = null;
            });
        }

        private void BtnResetOrientation_Click(object sender, RoutedEventArgs e)
        {
            _visualOrientationMapper.Reset();
            ModelMatrixTransform.Matrix = Matrix3D.Identity;
            TxtAngles.Text = "Pitch:     0.0  Roll:     0.0  Yaw:     0.0";
            Log("Orientacja wyzerowana.");
        }

        private void Triki_SampleReceived(object? sender, ImuSample e)
        {
            // Raw data: no axis mapping, no filter
            var sample = e; 

            var orientation = _visualOrientationMapper.Update(sample);

            _sampleCount++;
            if (!_sampleStopwatch.IsRunning)
            {
                _sampleStopwatch.Start();
            }

            // Update UI only every N samples to avoid freezing the UI thread
            if (_sampleCount % 5 != 0) return;

            var elapsedSeconds = Math.Max(_sampleStopwatch.Elapsed.TotalSeconds, 0.001);
            var hz = _sampleCount / elapsedSeconds;
            var stats = _triki?.Stats;
            var snapshot = new UiSampleSnapshot(
                _sampleCount,
                sample,
                orientation,
                hz,
                stats?.NotificationCount ?? 0,
                stats?.ParsedFrameCount ?? 0,
                stats?.DiscardedStartupSampleCount ?? 0,
                stats?.DroppedByteCount ?? 0,
                stats?.LastNotificationGapMilliseconds ?? 0.0,
                stats?.MaxNotificationGapMilliseconds ?? 0.0,
                _activeOrientationMode,
                _latestDeviceInfo);

            lock (_latestUiSnapshotLock)
            {
                _latestUiSnapshot = snapshot;
                _latestUiSnapshotVersion++;
            }

            ScheduleUiRefreshIfNeeded();
        }

        private void ResetPendingUiSnapshot()
        {
            lock (_latestUiSnapshotLock)
            {
                _latestUiSnapshot = null;
                _latestUiSnapshotVersion = 0;
                _appliedUiSnapshotVersion = 0;
            }
            _uiUpdateGate.Complete();
        }

        private void ScheduleUiRefreshIfNeeded()
        {
            if (!_uiUpdateGate.TryBeginSchedule())
            {
                return;
            }

            Dispatcher.InvokeAsync(ApplyLatestUiSnapshot);
        }

        private void ApplyLatestUiSnapshot()
        {
            long appliedVersion = 0;

            try
            {
                UiSampleSnapshot? snapshot;
                lock (_latestUiSnapshotLock)
                {
                    snapshot = _latestUiSnapshot;
                    appliedVersion = _latestUiSnapshotVersion;
                }

                if (snapshot is not null)
                {
                    ApplyUiSnapshot(snapshot.Value);
                }
            }
            finally
            {
                lock (_latestUiSnapshotLock)
                {
                    _appliedUiSnapshotVersion = Math.Max(_appliedUiSnapshotVersion, appliedVersion);
                }
                _uiUpdateGate.Complete();
            }

            var hasNewerSnapshot = false;
            lock (_latestUiSnapshotLock)
            {
                hasNewerSnapshot = _latestUiSnapshotVersion > _appliedUiSnapshotVersion;
            }

            if (hasNewerSnapshot)
            {
                ScheduleUiRefreshIfNeeded();
            }
        }

        private void ApplyUiSnapshot(UiSampleSnapshot snapshot)
        {
            var sample = snapshot.Sample;
            var orientation = snapshot.Orientation;

            TxtStatus.Text = $"Połączono. Odczyty: {snapshot.SampleCount}";
            TxtStatus.Foreground = System.Windows.Media.Brushes.SeaGreen;

            ModelMatrixTransform.Matrix = orientation.Transform;
            TxtAngles.Text = $"Pitch: {orientation.Pitch,7:F1}  Roll: {orientation.Roll,7:F1}  Yaw: {orientation.Yaw,7:F1}";
            TxtImu.Text =
                $"Gyro:  X={sample.GyroX,7:F2} Y={sample.GyroY,7:F2} Z={sample.GyroZ,7:F2}\n" +
                $"Accel: X={sample.AccelX,7:F3} Y={sample.AccelY,7:F3} Z={sample.AccelZ,7:F3}";
            TxtDiagnostics.Text =
                snapshot.DeviceInfo.ToDisplayText() + "\n" +
                $"Raw gyro:  X={sample.RawGyroX,6} Y={sample.RawGyroY,6} Z={sample.RawGyroZ,6}\n" +
                $"Raw accel: X={sample.RawAccelX,6} Y={sample.RawAccelY,6} Z={sample.RawAccelZ,6}\n" +
                $"Hz: {snapshot.Hertz,6:F1}\n" +
                $"Orient: {OrientationModeLabel(snapshot.OrientationMode)}\n" +
                $"Time: BLE notify\n" +
                $"BLE gap: last={snapshot.LastNotificationGapMilliseconds,6:F1}ms max={snapshot.MaxNotificationGapMilliseconds,6:F1}ms\n" +
                $"Parser: notif={snapshot.NotificationCount} frames={snapshot.ParsedFrameCount}\n" +
                $"Drop: startup={snapshot.DiscardedStartupSampleCount} bytes={snapshot.DroppedByteCount}";

            _gyroXPoints.Add(new ObservablePoint(snapshot.SampleCount, sample.GyroX));
            _gyroYPoints.Add(new ObservablePoint(snapshot.SampleCount, sample.GyroY));
            _gyroZPoints.Add(new ObservablePoint(snapshot.SampleCount, sample.GyroZ));
            if (_gyroZPoints.Count > 100)
            {
                _gyroXPoints.RemoveAt(0);
                _gyroYPoints.RemoveAt(0);
                _gyroZPoints.RemoveAt(0);
            }
        }

        private void Triki_DeviceInfoReceived(object? sender, TrikiDeviceInfo e)
        {
            Dispatcher.InvokeAsync(() =>
            {
                _latestDeviceInfo = e;
                if (_sampleCount == 0)
                {
                    TxtDiagnostics.Text = BuildWaitingDiagnosticsText(e);
                }
            });
        }

        private void Triki_LogMessage(object? sender, string e)
        {
            Dispatcher.InvokeAsync(() =>
            {
                Log(e);
            });
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _cts?.Cancel();
            Environment.Exit(0);
        }

        private OrientationMode SelectedOrientationMode => CmbOrientationMode.SelectedIndex == 1
            ? OrientationMode.ZappkaLikePitchRoll
            : OrientationMode.Madgwick;

        private static string OrientationModeLabel(OrientationMode mode)
        {
            return mode == OrientationMode.ZappkaLikePitchRoll
                ? "Żappka-like tilt"
                : "Madgwick";
        }

        private static string BuildWaitingDiagnosticsText(TrikiDeviceInfo deviceInfo)
        {
            return deviceInfo.ToDisplayText() + "\nRaw: --\nHz: --\nParser: --";
        }

        private void Log(string msg)
        {
            TxtLog.Text = msg + "\n" + TxtLog.Text;
            if (TxtLog.Text.Length > 3000)
            {
                TxtLog.Text = TxtLog.Text.Substring(0, 3000);
            }
        }
    }
}
