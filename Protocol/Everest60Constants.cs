using System;
using System.Collections.Generic;

namespace Everest60Rgb.Protocol
{
    /// <summary>
    /// Hardware constants for the Mountain Everest 60 USB HID protocol.
    /// Specifically targeted at controlling the 15 bottom perimeter LEDs
    /// for real-time status indication in Custom mode (0x07).
    /// </summary>
    public static class Everest60Constants
    {
        // ── USB Identifiers ──────────────────────────────────────────────────
        public const ushort VendorId      = 0x3282;
        public const ushort ProductIdAnsi = 0x0005;
        public const ushort ProductIdIso  = 0x0006;
        public const int    Interface     = 2; // MI_02 lighting endpoint

        public static readonly ushort[] SupportedProductIds = { ProductIdAnsi, ProductIdIso };

        // ── Report Format ────────────────────────────────────────────────────
        public const int ReportLength = 65; // 1-byte Report ID (0x00) + 64-byte payload

        // Magic bytes present at buf[2..4] on every command packet
        public const byte Magic0 = 0x46;
        public const byte Magic1 = 0x23;
        public const byte Magic2 = 0xEA;

        // ── Protocol Command Codes ───────────────────────────────────────────
        public const byte CmdSetMode     = 0x16; // Set active mode
        public const byte CmdLatch       = 0x1A; // Commit / latch active mode
        public const byte CmdCustomBegin = 0x34; // Start custom RGB stream
        public const byte CmdCustomMap   = 0x35; // Chunk of up to 14 (hwIndex, R, G, B) tuples
        public const byte CmdCustomEnd   = 0x36; // End custom RGB stream

        // ── Custom Mode ──────────────────────────────────────────────────────
        public const byte ModeCustom     = 0x07; // Direct per-LED custom mode

        // Stream control flags for 0x35 packets
        public const byte CustomMapMore  = 0x0E; // More packets follow
        public const byte CustomMapLast  = 0x0A; // Final packet in sequence

        public const int  CustomMapDataOffset = 9;  // Payload begins at buf[9]
        public const int  MaxLedsPerMapPacket = 14; // 14 LEDs * 4 bytes = 56 bytes

        // ── LED Counts ───────────────────────────────────────────────────────
        public const int BottomBorderLedCount = 15;
        public const int KeyCount             = 64;
        public const int SideLedCount         = 44;
        public const int TotalLeds            = 108; // 64 keys + 44 perimeter LEDs

        // ── Perimeter LED Arrays ─────────────────────────────────────────────
        public static readonly byte[] TopBorderLeds    = { 126, 127, 128, 129, 130, 131, 132, 133, 134, 135, 136, 137, 138, 139, 140 };
        public static readonly byte[] RightBorderLeds  = { 141, 142, 143, 144, 145, 146, 147, 148 };
        public static readonly byte[] BottomBorderLeds = { 163, 162, 161, 160, 159, 158, 157, 156, 155, 154, 153, 152, 151, 150, 149 };
        public static readonly byte[] LeftBorderLeds   = { 164, 165, 166, 167, 168, 169 };

        public static readonly byte[] SideLedIndices = BuildRange(126, 169);

        // ── All 64 Key Hardware LEDs ─────────────────────────────────────────
        public static readonly byte[] KeyLedIndices =
        {
            0,  22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34,
            42, 43, 44, 45, 46, 47, 48, 49, 50, 51, 52, 53, 54, 55,
            63, 64, 65, 66, 67, 68, 69, 70, 71, 72, 73, 74, 76,
            84, 85, 86, 87, 88, 89, 90, 91, 92, 93, 94, 97, 99, 56,
            105, 106, 107, 110, 113, 115, 119, 120, 121
        };

        public static readonly byte[] All108LedIndices = BuildAllLeds();

        // ── Send/Verify Timing ───────────────────────────────────────────────
        public const int SendVerifyDelayMs = 50;
        public const int SendRetries       = 3;

        private static byte[] BuildRange(byte start, byte end)
        {
            var arr = new byte[end - start + 1];
            for (int i = 0; i < arr.Length; i++)
                arr[i] = (byte)(start + i);
            return arr;
        }

        private static byte[] BuildAllLeds()
        {
            var list = new List<byte>(TotalLeds);
            list.AddRange(KeyLedIndices);
            for (byte i = 126; i <= 169; i++)
            {
                list.Add(i);
            }
            return list.ToArray();
        }

        public static byte ToFirmwareStep(int percent)
        {
            percent = Math.Max(0, Math.Min(100, percent));
            return (byte)((int)Math.Round(percent / 25.0) * 25);
        }
    }
}
