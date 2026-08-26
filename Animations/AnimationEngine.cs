using System;
using System.Diagnostics;
using System.Threading;
using Everest60Rgb.Core;
using Everest60Rgb.Indicators;
using Everest60Rgb.Protocol;

namespace Everest60Rgb.Animations
{
    /// <summary>
    /// Background status engine that monitors system metrics and executes change-driven updates.
    /// Only transmits to USB when status values change, ensuring lighting works reliably while
    /// keeping USB traffic to a minimum during typing.
    /// </summary>
    public class AnimationEngine : IDisposable
    {
        private readonly Everest60Controller _controller;
        private Thread _workerThread;
        private volatile bool _isRunning;
        private readonly AutoResetEvent _wakeEvent = new AutoResetEvent(false);
        private readonly object _stateLock = new object();

        private StatusSource _currentSource = StatusSource.Battery;
        private RgbColor? _customStatusColor = null; // null = use default dynamic presets
        private int _brightnessPercent = 100;
        private bool _needsInitialSync = true;

        private readonly RgbColor[] _lastRenderedColors = new RgbColor[Everest60Constants.BottomBorderLedCount];

        public StatusSource CurrentSource
        {
            get { lock (_stateLock) return _currentSource; }
        }

        public RgbColor? CustomStatusColor
        {
            get { lock (_stateLock) return _customStatusColor; }
            set
            {
                lock (_stateLock)
                {
                    _customStatusColor = value;
                    _needsInitialSync = true;
                }
                _wakeEvent.Set();
            }
        }

        public int BrightnessPercent
        {
            get { lock (_stateLock) return _brightnessPercent; }
            set
            {
                lock (_stateLock)
                {
                    _brightnessPercent = Math.Max(0, Math.Min(100, value));
                    _needsInitialSync = true;
                }
                _wakeEvent.Set();
            }
        }

        public AnimationEngine(Everest60Controller controller)
        {
            _controller = controller ?? throw new ArgumentNullException(nameof(controller));
            for (int i = 0; i < _lastRenderedColors.Length; i++)
            {
                _lastRenderedColors[i] = RgbColor.Black;
            }
        }

        public void Start()
        {
            if (_isRunning) return;

            _isRunning = true;
            _needsInitialSync = true;
            _workerThread = new Thread(WorkerLoop)
            {
                IsBackground = true,
                Name = "Everest60-StatusWorker",
                Priority = ThreadPriority.BelowNormal
            };
            _workerThread.Start();
        }

        public void Stop()
        {
            if (!_isRunning) return;

            _isRunning = false;
            _wakeEvent.Set();
            if (_workerThread != null && _workerThread.IsAlive)
            {
                _workerThread.Join(1000);
            }
            _workerThread = null;
        }

        public void SetSource(StatusSource source, RgbColor? customColor = null)
        {
            lock (_stateLock)
            {
                _currentSource = source;
                if (customColor.HasValue)
                {
                    _customStatusColor = customColor;
                }
                _needsInitialSync = true;
            }
            _wakeEvent.Set();
        }

        private void WorkerLoop()
        {
            while (_isRunning)
            {
                StatusSource source;
                RgbColor? statusColor;
                int brightness;
                bool doSync;

                lock (_stateLock)
                {
                    source = _currentSource;
                    statusColor = _customStatusColor;
                    brightness = _brightnessPercent;
                    doSync = _needsInitialSync;
                    _needsInitialSync = false;
                }

                int pollIntervalMs = 1000;

                if (_controller.IsConnected)
                {
                    if (source != StatusSource.Off)
                    {
                        double currentValue = 0;

                        switch (source)
                        {
                            case StatusSource.Battery:
                                currentValue = SystemMonitors.GetBatteryPercentage();
                                pollIntervalMs = 3000; // Poll battery every 3s
                                break;

                            case StatusSource.Volume:
                                currentValue = SystemMonitors.GetMasterVolume();
                                pollIntervalMs = 150; // Poll volume every 150ms
                                break;

                            case StatusSource.Cpu:
                                currentValue = SystemMonitors.GetCpuUsage();
                                pollIntervalMs = 2000; // Poll CPU every 2s
                                break;

                            case StatusSource.SolidColor:
                                currentValue = 1.0;
                                pollIntervalMs = 5000;
                                break;
                        }

                        var newColors = BottomRowIndicator.ComputeProgressColors(currentValue, source, statusColor);

                        // Check if bottom LED colors changed
                        bool colorsChanged = false;
                        for (int i = 0; i < newColors.Length; i++)
                        {
                            if (newColors[i].R != _lastRenderedColors[i].R ||
                                newColors[i].G != _lastRenderedColors[i].G ||
                                newColors[i].B != _lastRenderedColors[i].B)
                            {
                                colorsChanged = true;
                                break;
                            }
                        }

                        if (colorsChanged || doSync)
                        {
                            Array.Copy(newColors, _lastRenderedColors, newColors.Length);

                            // Overwrite bottom 15 LEDs in the custom map
                            _controller.Framebuffer.OverwriteBottomBorder(newColors);

                            if (doSync)
                            {
                                // On initial sync or mode switch: synchronize full frame
                                _controller.SendFullCustomMap(brightness);
                            }
                            else
                            {
                                // Live update: stream bottom 15 LEDs
                                _controller.SendBottomBorderOnly(brightness);
                            }
                        }
                    }
                    else if (doSync)
                    {
                        _controller.ClearBottomBorderOnly(brightness);
                        for (int i = 0; i < _lastRenderedColors.Length; i++) _lastRenderedColors[i] = RgbColor.Black;
                        pollIntervalMs = 5000;
                    }
                }
                else
                {
                    _controller.Connect();
                    pollIntervalMs = 3000;
                }

                _wakeEvent.WaitOne(pollIntervalMs);
            }
        }

        public void Dispose()
        {
            Stop();
            _wakeEvent.Dispose();
        }
    }
}
