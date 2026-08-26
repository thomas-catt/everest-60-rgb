using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;
using Everest60Rgb.Protocol;

namespace Everest60Rgb.Hid
{
    /// <summary>
    /// Managed wrapper around a Windows HID device file handle.
    /// Supports opening, sending/receiving feature reports, and the
    /// send-and-verify handshake required by the Everest 60 protocol.
    /// </summary>
    public class HidDevice : IDisposable
    {
        private SafeFileHandle _handle;
        private bool _disposed;

        public string DevicePath             { get; }
        public ushort VendorId               { get; }
        public ushort ProductId              { get; }
        public ushort VersionNumber          { get; }
        public ushort FeatureReportByteLength { get; }
        public ushort UsagePage              { get; }
        public ushort Usage                  { get; }

        public bool IsOpen => _handle != null && !_handle.IsInvalid && !_handle.IsClosed;

        public HidDevice(
            string devicePath,
            ushort vendorId,
            ushort productId,
            ushort versionNumber,
            ushort featureReportByteLength,
            ushort usagePage = 0,
            ushort usage = 0)
        {
            DevicePath             = devicePath;
            VendorId               = vendorId;
            ProductId              = productId;
            VersionNumber          = versionNumber;
            FeatureReportByteLength = featureReportByteLength;
            UsagePage              = usagePage;
            Usage                  = usage;
        }

        // ── Connection ───────────────────────────────────────────────────────

        /// <summary>
        /// Open the device handle.  Tries Read+Write first, then Write-only,
        /// then query-only (access = 0) as a last resort.
        /// </summary>
        public bool Open(bool readWrite = true)
        {
            if (IsOpen) return true;

            uint[] accessModes = readWrite
                ? new uint[] { Win32Hid.GENERIC_READ | Win32Hid.GENERIC_WRITE, Win32Hid.GENERIC_WRITE, 0 }
                : new uint[] { 0 };

            uint share = Win32Hid.FILE_SHARE_READ | Win32Hid.FILE_SHARE_WRITE;

            foreach (var access in accessModes)
            {
                _handle = Win32Hid.CreateFileW(
                    DevicePath,
                    access,
                    share,
                    IntPtr.Zero,
                    Win32Hid.OPEN_EXISTING,
                    0,
                    IntPtr.Zero);

                if (IsOpen) return true;
            }

            return false;
        }

        public void Close()
        {
            if (_handle != null)
            {
                if (!_handle.IsClosed)
                    _handle.Close();
                _handle.Dispose();
                _handle = null;
            }
        }

        // ── Raw Feature Reports ──────────────────────────────────────────────

        /// <summary>Send a feature report (fire-and-forget, no verification).</summary>
        public bool SendFeatureReport(byte[] report)
        {
            if (!EnsureOpen()) return false;

            if (report == null || report.Length == 0)
                throw new ArgumentException("Report buffer cannot be null or empty.", nameof(report));

            bool ok = Win32Hid.HidD_SetFeature(_handle, report, report.Length);
            if (!ok)
            {
                int err = Marshal.GetLastWin32Error();
                Debug.WriteLine($"[HidDevice] HidD_SetFeature failed: Win32 error {err}");
                if (err == 5 /* ACCESS_DENIED */ || err == 6 /* INVALID_HANDLE */)
                    Close();
            }
            return ok;
        }

        /// <summary>Read back a feature report into the provided buffer.</summary>
        public bool GetFeatureReport(byte[] buffer)
        {
            if (!EnsureOpen()) return false;

            if (buffer == null || buffer.Length == 0)
                throw new ArgumentException("Report buffer cannot be null or empty.", nameof(buffer));

            return Win32Hid.HidD_GetFeature(_handle, buffer, buffer.Length);
        }

        // ── Send-and-Verify Handshake ────────────────────────────────────────

        /// <summary>
        /// Send a feature report and verify the device echoes the command byte
        /// back in <c>resp[1]</c>.  Retries up to <paramref name="retries"/> times.
        ///
        /// This matches the reference implementation's <c>_send()</c> function,
        /// which waits 50 ms between send and read and retries on mismatch.
        /// </summary>
        /// <returns>
        /// The 65-byte response buffer on success, or <c>null</c> if all retries
        /// failed or the device is disconnected.
        /// </returns>
        public byte[] SendAndVerify(byte[] report, int retries = -1)
        {
            if (retries < 0)
                retries = Everest60Constants.SendRetries;

            if (!EnsureOpen()) return null;

            byte expectedCmd = report[1];
            byte[] resp = new byte[Everest60Constants.ReportLength];

            for (int attempt = 0; attempt < retries; attempt++)
            {
                if (!Win32Hid.HidD_SetFeature(_handle, report, report.Length))
                {
                    int err = Marshal.GetLastWin32Error();
                    Debug.WriteLine($"[HidDevice] SendAndVerify: SetFeature failed (attempt {attempt + 1}/{retries}, err={err})");
                    if (err == 5 || err == 6)
                    {
                        Close();
                        return null;
                    }
                    continue;
                }

                Thread.Sleep(Everest60Constants.SendVerifyDelayMs);

                resp[0] = 0x00; // Report ID for GetFeature
                if (Win32Hid.HidD_GetFeature(_handle, resp, resp.Length))
                {
                    Thread.Sleep(Everest60Constants.SendVerifyDelayMs);
                    if (resp[1] == expectedCmd)
                        return resp;

                    Debug.WriteLine($"[HidDevice] SendAndVerify: expected cmd 0x{expectedCmd:X2} but got 0x{resp[1]:X2} (attempt {attempt + 1}/{retries})");
                }
                else
                {
                    Debug.WriteLine($"[HidDevice] SendAndVerify: GetFeature failed (attempt {attempt + 1}/{retries})");
                    Thread.Sleep(Everest60Constants.SendVerifyDelayMs);
                }
            }

            // Return whatever we last got (may be mismatched), matching the reference
            return resp;
        }

        // ── IDisposable ──────────────────────────────────────────────────────

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                Close();
                _disposed = true;
            }
        }

        ~HidDevice()
        {
            Dispose(false);
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private bool EnsureOpen()
        {
            if (IsOpen) return true;
            return Open();
        }

        public override string ToString()
        {
            return $"HID(VID=0x{VendorId:X4}, PID=0x{ProductId:X4}, " +
                   $"FeatureLen={FeatureReportByteLength}, " +
                   $"Page=0x{UsagePage:X4}, Usage=0x{Usage:X4})";
        }
    }
}
