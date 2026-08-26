using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Everest60Rgb.Hid
{
    /// <summary>
    /// Discovers HID devices matching a given VID/PID using the Windows
    /// Configuration Manager (cfgmgr32) and SetupAPI.
    /// </summary>
    public static class HidDeviceFinder
    {
        /// <summary>
        /// Find HID device interfaces matching the given vendor and product IDs.
        /// Uses cfgmgr32 as the primary strategy (fast, reliable on x86/x64/ARM64)
        /// and falls back to SetupAPI if needed.
        /// </summary>
        public static List<HidDevice> FindDevices(ushort vendorId, ushort[] productIds = null)
        {
            var results   = new List<HidDevice>();
            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            Win32Hid.HidD_GetHidGuid(out Guid hidGuid);

            // Strategy 1: CfgMgr32
            try
            {
                foreach (var path in EnumeratePathsCfgMgr(hidGuid))
                {
                    if (seenPaths.Add(path))
                        TryAddDevice(path, vendorId, productIds, results);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HidDeviceFinder] CfgMgr32 enumeration failed: {ex.Message}");
            }

            // Strategy 2: SetupAPI (fallback)
            if (results.Count == 0)
            {
                try
                {
                    foreach (var path in EnumeratePathsSetupDi(hidGuid))
                    {
                        if (seenPaths.Add(path))
                            TryAddDevice(path, vendorId, productIds, results);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[HidDeviceFinder] SetupAPI enumeration failed: {ex.Message}");
                }
            }

            return results;
        }

        /// <summary>
        /// Find the single best HID device interface for the Everest 60 lighting
        /// endpoint (Interface 2, FeatureReportByteLength == 65).
        /// Returns null if no suitable device is found.
        /// </summary>
        public static HidDevice FindBestDevice(ushort vendorId, ushort[] productIds)
        {
            var candidates = FindDevices(vendorId, productIds);
            if (candidates.Count == 0) return null;

            // Score and sort: prefer FeatureLen==65 (lighting endpoint), then MI_02 in path
            candidates.Sort((a, b) =>
            {
                int sa = ScoreDevice(a);
                int sb = ScoreDevice(b);
                return sb.CompareTo(sa);
            });

            return candidates[0];
        }

        private static int ScoreDevice(HidDevice d)
        {
            int score = 0;
            if (d.FeatureReportByteLength == 65) score += 100;
            if (d.DevicePath.IndexOf("mi_02", StringComparison.OrdinalIgnoreCase) >= 0) score += 50;
            if (d.FeatureReportByteLength > 0) score += 20;
            return score;
        }

        // ── CfgMgr32 Enumeration ────────────────────────────────────────────

        private static List<string> EnumeratePathsCfgMgr(Guid hidGuid)
        {
            var paths = new List<string>();

            int cr = Win32Hid.CM_Get_Device_Interface_List_SizeW(
                out uint bufLen, ref hidGuid, null,
                Win32Hid.CM_GET_DEVICE_INTERFACE_LIST_PRESENT);

            if (cr != Win32Hid.CR_SUCCESS || bufLen == 0)
                return paths;

            var buffer = new char[bufLen];
            cr = Win32Hid.CM_Get_Device_Interface_ListW(
                ref hidGuid, null, buffer, bufLen,
                Win32Hid.CM_GET_DEVICE_INTERFACE_LIST_PRESENT);

            if (cr != Win32Hid.CR_SUCCESS)
                return paths;

            // The buffer is a multi-string: null-terminated strings, double-null at end
            int start = 0;
            for (int i = 0; i < buffer.Length; i++)
            {
                if (buffer[i] == '\0')
                {
                    if (i > start)
                    {
                        string path = new string(buffer, start, i - start);
                        if (!string.IsNullOrWhiteSpace(path))
                            paths.Add(path);
                    }
                    start = i + 1;
                }
            }

            return paths;
        }

        // ── SetupAPI Enumeration ─────────────────────────────────────────────

        private static List<string> EnumeratePathsSetupDi(Guid hidGuid)
        {
            var paths = new List<string>();

            IntPtr devInfoSet = Win32Hid.SetupDiGetClassDevsW(
                ref hidGuid, null, IntPtr.Zero,
                Win32Hid.DIGCF_PRESENT | Win32Hid.DIGCF_DEVICEINTERFACE);

            if (devInfoSet == IntPtr.Zero || devInfoSet == new IntPtr(-1))
                return paths;

            try
            {
                var ifData = new Win32Hid.SP_DEVICE_INTERFACE_DATA();
                ifData.cbSize = Marshal.SizeOf(typeof(Win32Hid.SP_DEVICE_INTERFACE_DATA));

                for (int idx = 0;
                     Win32Hid.SetupDiEnumDeviceInterfaces(devInfoSet, IntPtr.Zero, ref hidGuid, idx, ref ifData);
                     idx++)
                {
                    // Query required buffer size
                    Win32Hid.SetupDiGetDeviceInterfaceDetailW(
                        devInfoSet, ref ifData, IntPtr.Zero, 0, out int needed, IntPtr.Zero);

                    if (needed <= 0) continue;

                    IntPtr detailBuf = Marshal.AllocHGlobal(needed);
                    try
                    {
                        // cbSize: 6 on x86, 8 on x64 (DWORD + alignment + WCHAR[1])
                        Marshal.WriteInt32(detailBuf, IntPtr.Size == 8 ? 8 : 6);

                        if (Win32Hid.SetupDiGetDeviceInterfaceDetailW(
                            devInfoSet, ref ifData, detailBuf, needed, out _, IntPtr.Zero))
                        {
                            // Device path starts at offset 4 (after the cbSize DWORD)
                            string path = Marshal.PtrToStringUni(new IntPtr(detailBuf.ToInt64() + 4));
                            if (!string.IsNullOrEmpty(path))
                                paths.Add(path);
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(detailBuf);
                    }
                }
            }
            finally
            {
                Win32Hid.SetupDiDestroyDeviceInfoList(devInfoSet);
            }

            return paths;
        }

        // ── Device Inspection ────────────────────────────────────────────────

        private static void TryAddDevice(string devicePath, ushort vendorId, ushort[] productIds, List<HidDevice> results)
        {
            SafeFileHandle handle = Win32Hid.CreateFileW(
                devicePath,
                0, // query-only access, no locking
                Win32Hid.FILE_SHARE_READ | Win32Hid.FILE_SHARE_WRITE,
                IntPtr.Zero,
                Win32Hid.OPEN_EXISTING,
                0,
                IntPtr.Zero);

            if (handle.IsInvalid) return;

            try
            {
                var attr = new Win32Hid.HIDD_ATTRIBUTES();
                attr.Size = Marshal.SizeOf(typeof(Win32Hid.HIDD_ATTRIBUTES));

                if (!Win32Hid.HidD_GetAttributes(handle, ref attr))
                    return;

                if (attr.VendorID != vendorId)
                    return;

                // Check PID filter
                if (productIds != null && productIds.Length > 0)
                {
                    bool match = false;
                    foreach (var pid in productIds)
                    {
                        if (attr.ProductID == pid) { match = true; break; }
                    }
                    if (!match) return;
                }

                // Read capabilities
                ushort featureLen = 0, usagePage = 0, usage = 0;

                if (Win32Hid.HidD_GetPreparsedData(handle, out IntPtr ppData))
                {
                    try
                    {
                        if (Win32Hid.HidP_GetCaps(ppData, out Win32Hid.HIDP_CAPS caps) > 0)
                        {
                            featureLen = caps.FeatureReportByteLength;
                            usagePage  = caps.UsagePage;
                            usage      = caps.Usage;
                        }
                    }
                    finally
                    {
                        Win32Hid.HidD_FreePreparsedData(ppData);
                    }
                }

                results.Add(new HidDevice(
                    devicePath, attr.VendorID, attr.ProductID, attr.VersionNumber,
                    featureLen, usagePage, usage));
            }
            finally
            {
                handle.Close();
                handle.Dispose();
            }
        }
    }
}
