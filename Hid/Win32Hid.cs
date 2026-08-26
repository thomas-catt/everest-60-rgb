using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Everest60Rgb.Hid
{
    /// <summary>
    /// P/Invoke declarations for the Windows HID, SetupAPI, Configuration Manager,
    /// and Kernel32 APIs used to discover and communicate with HID devices.
    /// </summary>
    internal static class Win32Hid
    {
        // ── Kernel32 constants ───────────────────────────────────────────────
        public const uint GENERIC_READ       = 0x80000000;
        public const uint GENERIC_WRITE      = 0x40000000;
        public const uint FILE_SHARE_READ    = 0x00000001;
        public const uint FILE_SHARE_WRITE   = 0x00000002;
        public const uint OPEN_EXISTING      = 3;
        public const uint FILE_FLAG_OVERLAPPED = 0x40000000;

        // ── SetupAPI constants ───────────────────────────────────────────────
        public const int DIGCF_PRESENT         = 0x00000002;
        public const int DIGCF_DEVICEINTERFACE = 0x00000010;

        // ── CfgMgr32 constants ───────────────────────────────────────────────
        public const int  CR_SUCCESS = 0x00000000;
        public const uint CM_GET_DEVICE_INTERFACE_LIST_PRESENT = 0x00000000;

        // ── Structures ───────────────────────────────────────────────────────

        [StructLayout(LayoutKind.Sequential)]
        public struct SP_DEVICE_INTERFACE_DATA
        {
            public int    cbSize;
            public Guid   InterfaceClassGuid;
            public int    Flags;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HIDD_ATTRIBUTES
        {
            public int    Size;
            public ushort VendorID;
            public ushort ProductID;
            public ushort VersionNumber;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HIDP_CAPS
        {
            public ushort Usage;
            public ushort UsagePage;
            public ushort InputReportByteLength;
            public ushort OutputReportByteLength;
            public ushort FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
            public ushort[] Reserved;
            public ushort NumberLinkCollectionNodes;
            public ushort NumberInputButtonCaps;
            public ushort NumberInputValueCaps;
            public ushort NumberInputDataIndices;
            public ushort NumberOutputButtonCaps;
            public ushort NumberOutputValueCaps;
            public ushort NumberOutputDataIndices;
            public ushort NumberFeatureButtonCaps;
            public ushort NumberFeatureValueCaps;
            public ushort NumberFeatureDataIndices;
        }

        // ── hid.dll ──────────────────────────────────────────────────────────

        [DllImport("hid.dll", SetLastError = true)]
        public static extern void HidD_GetHidGuid(out Guid hidGuid);

        [DllImport("hid.dll", SetLastError = true)]
        public static extern bool HidD_GetAttributes(
            SafeFileHandle hidDeviceObject,
            ref HIDD_ATTRIBUTES attributes);

        [DllImport("hid.dll", SetLastError = true)]
        public static extern bool HidD_SetFeature(
            SafeFileHandle hidDeviceObject,
            byte[] reportBuffer,
            int reportBufferLength);

        [DllImport("hid.dll", SetLastError = true)]
        public static extern bool HidD_GetFeature(
            SafeFileHandle hidDeviceObject,
            byte[] reportBuffer,
            int reportBufferLength);

        [DllImport("hid.dll", SetLastError = true)]
        public static extern bool HidD_GetPreparsedData(
            SafeFileHandle hidDeviceObject,
            out IntPtr preparsedData);

        [DllImport("hid.dll", SetLastError = true)]
        public static extern bool HidD_FreePreparsedData(IntPtr preparsedData);

        [DllImport("hid.dll", SetLastError = true)]
        public static extern int HidP_GetCaps(
            IntPtr preparsedData,
            out HIDP_CAPS capabilities);

        // ── cfgmgr32.dll ─────────────────────────────────────────────────────

        [DllImport("cfgmgr32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern int CM_Get_Device_Interface_List_SizeW(
            out uint pulLen,
            ref Guid pInterfaceClassGuid,
            string pDeviceID,
            uint ulFlags);

        [DllImport("cfgmgr32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern int CM_Get_Device_Interface_ListW(
            ref Guid pInterfaceClassGuid,
            string pDeviceID,
            [Out] char[] Buffer,
            uint BufferLen,
            uint ulFlags);

        // ── setupapi.dll ─────────────────────────────────────────────────────

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr SetupDiGetClassDevsW(
            ref Guid classGuid,
            string enumerator,
            IntPtr hwndParent,
            int flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        public static extern bool SetupDiEnumDeviceInterfaces(
            IntPtr deviceInfoSet,
            IntPtr deviceInfoData,
            ref Guid interfaceClassGuid,
            int memberIndex,
            ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool SetupDiGetDeviceInterfaceDetailW(
            IntPtr deviceInfoSet,
            ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData,
            IntPtr deviceInterfaceDetailData,
            int deviceInterfaceDetailDataSize,
            out int requiredSize,
            IntPtr deviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true)]
        public static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

        // ── kernel32.dll ─────────────────────────────────────────────────────

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern SafeFileHandle CreateFileW(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);
    }
}
