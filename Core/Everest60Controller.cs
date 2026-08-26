using System;
using System.Collections.Generic;
using System.Diagnostics;
using Everest60Rgb.Hid;
using Everest60Rgb.Protocol;

namespace Everest60Rgb.Core
{
    /// <summary>
    /// Streamlined controller dedicated to Mountain Everest 60 status indication in Custom mode (0x07).
    /// Executes the complete and reliable protocol flow:
    ///   SetCustomMode (0x16) -> PreLatch (0x1A) -> CustomBegin (0x34) -> CustomMap (0x35) -> CustomEnd (0x36) -> PostLatch (0x1A)
    /// </summary>
    public class Everest60Controller : IDisposable
    {
        private HidDevice _device;
        private readonly LedFramebuffer _framebuffer = new LedFramebuffer();
        private readonly object _usbLock = new object();
        private int _cachedBrightness = 100;

        public bool IsConnected => _device != null && _device.IsOpen;
        public LedFramebuffer Framebuffer => _framebuffer;
        public string DevicePath => _device?.DevicePath;
        public ushort ProductId => _device?.ProductId ?? 0;
        public int CachedBrightness => _cachedBrightness;

        // ── Connection ───────────────────────────────────────────────────────

        public bool Connect()
        {
            lock (_usbLock)
            {
                if (IsConnected) return true;

                var dev = HidDeviceFinder.FindBestDevice(
                    Everest60Constants.VendorId,
                    Everest60Constants.SupportedProductIds);

                if (dev == null)
                {
                    Console.WriteLine("[DEBUG] No Mountain Everest 60 device found on USB.");
                    return false;
                }

                if (dev.Open())
                {
                    _device = dev;
                    Console.WriteLine($"[INFO] Connected to Everest 60 (PID: 0x{dev.ProductId:X4}) on {dev.DevicePath}");
                    QueryCurrentBrightness();
                    return true;
                }

                Console.WriteLine("[ERROR] Failed to open HID device handle.");
                return false;
            }
        }

        public void Disconnect()
        {
            lock (_usbLock)
            {
                if (_device != null)
                {
                    Console.WriteLine("[INFO] Disconnecting from keyboard.");
                    _device.Close();
                    _device.Dispose();
                    _device = null;
                }
            }
        }

        // ── Query Device Brightness ──────────────────────────────────────────

        /// <summary>
        /// Queries the device once for its hardware brightness value and caches it.
        /// </summary>
        public int QueryCurrentBrightness()
        {
            lock (_usbLock)
            {
                if (!EnsureConnected()) return _cachedBrightness;

                try
                {
                    var buf = new byte[Everest60Constants.ReportLength];
                    buf[0] = 0x00; // Report ID
                    if (_device.GetFeatureReport(buf))
                    {
                        byte candidate = 0;
                        if (buf[1] == 0x34 && buf[5] >= 25 && buf[5] <= 100)
                            candidate = buf[5];
                        else if (buf[1] == 0x17 && buf[8] >= 25 && buf[8] <= 100)
                            candidate = buf[8];
                        else if (buf[8] >= 25 && buf[8] <= 100 && buf[8] % 25 == 0)
                            candidate = buf[8];
                        else if (buf[5] >= 25 && buf[5] <= 100 && buf[5] % 25 == 0)
                            candidate = buf[5];

                        if (candidate > 0)
                        {
                            _cachedBrightness = candidate;
                            return candidate;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[WARN] Brightness query failed: {ex.Message}");
                }

                return _cachedBrightness;
            }
        }

        // ── Status Indication Flow ───────────────────────────────────────────

        /// <summary>
        /// Overwrites the 15 status indicator colors into the active custom RGB map
        /// and sends only the 15 bottom perimeter LEDs over USB.
        /// </summary>
        public bool UpdateStatusIndication(IReadOnlyList<RgbColor> bottomColors, int brightnessPercent = -1)
        {
            int brightness = brightnessPercent > 0 ? brightnessPercent : _cachedBrightness;

            // 1. Overwrite status into current custom RGB map
            _framebuffer.OverwriteBottomBorder(bottomColors);

            // 2. Send only the 15 bottom perimeter LEDs with brightness
            return SendBottomBorderOnly(brightness);
        }

        /// <summary>
        /// Sends ONLY the 15 bottom perimeter LEDs in 2 map packets.
        /// </summary>
        public bool SendBottomBorderOnly(int brightnessPercent = 100)
        {
            lock (_usbLock)
            {
                if (!EnsureConnected()) return false;

                var bottomEntries = _framebuffer.GetBottomBorderEntries();
                return StreamCustomMap(bottomEntries, brightnessPercent);
            }
        }

        /// <summary>
        /// Full stream: synchronizes all 108 LEDs on the keyboard (used on initial sync or background change).
        /// </summary>
        public bool SendFullCustomMap(int brightnessPercent = 100)
        {
            lock (_usbLock)
            {
                if (!EnsureConnected()) return false;

                var allEntries = _framebuffer.GetAllEntries();
                return StreamCustomMap(allEntries, brightnessPercent);
            }
        }

        /// <summary>
        /// Clears ONLY the 15 bottom perimeter LEDs (sets them to Black),
        /// keeping all 64 keys and non-status perimeter LEDs lit in their current state.
        /// </summary>
        public bool ClearBottomBorderOnly(int brightnessPercent = -1)
        {
            Console.WriteLine("[INFO] Clearing bottom perimeter status LEDs (keys remain illuminated).");
            var blackColors = new RgbColor[Everest60Constants.BottomBorderLedCount];
            for (int i = 0; i < blackColors.Length; i++) blackColors[i] = RgbColor.Black;

            _framebuffer.OverwriteBottomBorder(blackColors);
            int bri = brightnessPercent > 0 ? brightnessPercent : _cachedBrightness;
            return SendFullCustomMap(bri);
        }

        /// <summary>
        /// Turn off all LEDs by blanking framebuffer and sending a full black custom map.
        /// </summary>
        public bool TurnOffAll()
        {
            Console.WriteLine("[INFO] Turning off all keyboard LEDs.");
            _framebuffer.SetBaseline(RgbColor.Black);
            return SendFullCustomMap(0);
        }

        // ── Reliable Protocol Stream Helper ──────────────────────────────────

        private bool StreamCustomMap(List<(byte hwIndex, RgbColor color)> stream, int brightnessPercent)
        {
            if (stream == null || stream.Count == 0) return true;

            // 1. Activate Custom mode
            Send(Everest60PacketBuilder.BuildSetCustomMode());

            // 2. Pre-map latch
            Send(Everest60PacketBuilder.BuildLatchCustom());

            // 3. Custom Begin with brightness
            Send(Everest60PacketBuilder.BuildCustomBegin(brightnessPercent));

            // 4. Send 0x35 Map packets (up to 14 entries each)
            int chunkSize = Everest60Constants.MaxLedsPerMapPacket;
            int total = stream.Count;

            for (int i = 0; i < total; i += chunkSize)
            {
                int count = Math.Min(chunkSize, total - i);
                var chunk = stream.GetRange(i, count);
                bool isLast = (i + count >= total);

                Send(Everest60PacketBuilder.BuildCustomMap(chunk, isLast));
            }

            // 5. Custom End
            Send(Everest60PacketBuilder.BuildCustomEnd());

            // 6. Post-map latch
            return Send(Everest60PacketBuilder.BuildLatchCustom());
        }

        private bool Send(byte[] report)
        {
            if (_device == null) return false;

            var resp = _device.SendAndVerify(report);
            if (resp != null) return true;

            Console.WriteLine($"[ERROR] USB Send failed for command 0x{report[1]:X2}");
            return false;
        }

        private bool EnsureConnected()
        {
            if (IsConnected) return true;
            return Connect();
        }

        public void Dispose()
        {
            Disconnect();
        }
    }
}
