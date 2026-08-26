using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Everest60Rgb.Indicators
{
    /// <summary>
    /// System polling utilities for hardware indicators: Battery (PowerStatus),
    /// CPU Usage (Win32 GetSystemTimes delta), and Master Audio Volume (Windows CoreAudio COM).
    /// </summary>
    public static class SystemMonitors
    {
        #region Battery Monitor
        public static double GetBatteryPercentage()
        {
            try
            {
                var power = SystemInformation.PowerStatus;
                if (power.BatteryLifePercent >= 0.0 && power.BatteryLifePercent <= 1.0)
                {
                    return power.BatteryLifePercent;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SystemMonitors] GetBatteryPercentage failed: {ex.Message}");
            }
            return 1.0; // Assume 100% on desktops without battery
        }

        public static bool IsBatteryCharging()
        {
            try
            {
                return (SystemInformation.PowerStatus.BatteryChargeStatus & BatteryChargeStatus.Charging) != 0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SystemMonitors] IsBatteryCharging failed: {ex.Message}");
                return false;
            }
        }
        #endregion

        #region CPU Monitor (via Win32 GetSystemTimes)
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetSystemTimes(out long idleTime, out long kernelTime, out long userTime);

        private static long _prevIdle;
        private static long _prevKernel;
        private static long _prevUser;
        private static bool _firstCpuSample = true;
        private static readonly object _cpuLock = new object();

        public static double GetCpuUsage()
        {
            lock (_cpuLock)
            {
                try
                {
                    if (GetSystemTimes(out long idle, out long kernel, out long user))
                    {
                        if (_firstCpuSample)
                        {
                            _prevIdle = idle;
                            _prevKernel = kernel;
                            _prevUser = user;
                            _firstCpuSample = false;
                            return 0.1;
                        }

                        long diffIdle = idle - _prevIdle;
                        long diffKernel = kernel - _prevKernel;
                        long diffUser = user - _prevUser;

                        _prevIdle = idle;
                        _prevKernel = kernel;
                        _prevUser = user;

                        long total = diffKernel + diffUser;
                        if (total > 0)
                        {
                            long busy = total - diffIdle;
                            double usage = (double)busy / total;
                            return Math.Max(0.0, Math.Min(1.0, usage));
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[SystemMonitors] GetCpuUsage failed: {ex.Message}");
                }
                return 0.0;
            }
        }
        #endregion

        #region Master Audio Volume Monitor (via CoreAudio COM)
        public static double GetMasterVolume()
        {
            IMMDeviceEnumerator enumerator = null;
            IMMDevice speakers = null;
            IAudioEndpointVolume endpointVolume = null;

            try
            {
                enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
                int hr = enumerator.GetDefaultAudioEndpoint(0 /* eRender */, 1 /* eMultimedia */, out speakers);
                if (hr == 0 && speakers != null)
                {
                    Guid iid = typeof(IAudioEndpointVolume).GUID;
                    hr = speakers.Activate(ref iid, 1 /* CLSCTX_INPROC_SERVER */, IntPtr.Zero, out object epvObj);
                    if (hr == 0 && epvObj != null)
                    {
                        endpointVolume = (IAudioEndpointVolume)epvObj;
                        endpointVolume.GetMasterVolumeLevelScalar(out float volume);
                        return Math.Max(0.0, Math.Min(1.0, (double)volume));
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SystemMonitors] GetMasterVolume failed: {ex.Message}");
            }
            finally
            {
                if (endpointVolume != null) Marshal.ReleaseComObject(endpointVolume);
                if (speakers != null) Marshal.ReleaseComObject(speakers);
                if (enumerator != null) Marshal.ReleaseComObject(enumerator);
            }
            return 0.5; // Fallback default
        }

        [ComImport]
        [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        private class MMDeviceEnumerator { }

        [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            int NotUsed1();
            [PreserveSig]
            int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice ppDevice);
        }

        [Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            [PreserveSig]
            int Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
        }

        [Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioEndpointVolume
        {
            int NotUsed1();
            int NotUsed2();
            int GetChannelCount(out uint pnChannelCount);
            int SetMasterVolumeLevel(float fLevelDB, ref Guid pguidEventContext);
            int SetMasterVolumeLevelScalar(float fLevel, ref Guid pguidEventContext);
            int GetMasterVolumeLevel(out float pfLevelDB);
            [PreserveSig]
            int GetMasterVolumeLevelScalar(out float pfLevel);
            int SetMute([MarshalAs(UnmanagedType.Bool)] bool bMute, ref Guid pguidEventContext);
            int GetMute([MarshalAs(UnmanagedType.Bool)] out bool pbMute);
        }
        #endregion
    }
}
