using System;
using System.Collections.Generic;

namespace Everest60Rgb.Protocol
{
    /// <summary>
    /// Builds 65-byte USB HID feature reports specifically for Everest 60 Custom Mode (0x07).
    /// Eliminates all unused firmware mode builders to keep the protocol footprint lightweight and fast.
    /// </summary>
    public static class Everest60PacketBuilder
    {
        private static byte[] MakeBuffer(byte command)
        {
            var buf = new byte[Everest60Constants.ReportLength];
            buf[0] = 0x00; // Report ID
            buf[1] = command;
            buf[2] = Everest60Constants.Magic0;
            buf[3] = Everest60Constants.Magic1;
            buf[4] = Everest60Constants.Magic2;
            return buf;
        }

        /// <summary>
        /// Activate Custom Mode (cmd 0x16, buf[5]=0x01, buf[9]=0x07).
        /// Note: Deliberately avoids cmd 0x17 (ModeDetails) to prevent white flash.
        /// </summary>
        public static byte[] BuildSetCustomMode()
        {
            var buf = MakeBuffer(Everest60Constants.CmdSetMode);
            buf[5] = 0x01;
            buf[9] = Everest60Constants.ModeCustom;
            return buf;
        }

        /// <summary>
        /// Latch/commit the active Custom mode state (cmd 0x1A, buf[5]=0x07).
        /// </summary>
        public static byte[] BuildLatchCustom()
        {
            var buf = MakeBuffer(Everest60Constants.CmdLatch);
            buf[5] = Everest60Constants.ModeCustom;
            return buf;
        }

        /// <summary>
        /// Begin Custom RGB stream (cmd 0x34, buf[5]=brightness step, buf[6]=0xC0).
        /// </summary>
        public static byte[] BuildCustomBegin(int brightnessPercent = 100)
        {
            var buf = MakeBuffer(Everest60Constants.CmdCustomBegin);
            buf[5] = Everest60Constants.ToFirmwareStep(brightnessPercent);
            buf[6] = 0xC0;
            return buf;
        }

        /// <summary>
        /// Build a single 0x35 packet containing up to 14 (hwIndex, R, G, B) tuples.
        /// Padds unused slots with the last entry to prevent hardware index 0 (ESC) blanking.
        /// </summary>
        public static byte[] BuildCustomMap(IReadOnlyList<(byte hwIndex, RgbColor color)> entries, bool isLast)
        {
            if (entries == null || entries.Count == 0)
                throw new ArgumentException("Map packet must contain at least one entry.", nameof(entries));
            if (entries.Count > Everest60Constants.MaxLedsPerMapPacket)
                throw new ArgumentException($"Cannot fit more than {Everest60Constants.MaxLedsPerMapPacket} entries in one packet.", nameof(entries));

            var buf = MakeBuffer(Everest60Constants.CmdCustomMap);
            buf[5] = isLast ? Everest60Constants.CustomMapLast : Everest60Constants.CustomMapMore;

            int pos = Everest60Constants.CustomMapDataOffset;

            // Write actual entries
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                buf[pos]     = e.hwIndex;
                buf[pos + 1] = e.color.R;
                buf[pos + 2] = e.color.G;
                buf[pos + 3] = e.color.B;
                pos += 4;
            }

            // Pad remaining slots by repeating the last real entry
            var lastEntry = entries[entries.Count - 1];
            while (pos < Everest60Constants.CustomMapDataOffset + Everest60Constants.MaxLedsPerMapPacket * 4)
            {
                buf[pos]     = lastEntry.hwIndex;
                buf[pos + 1] = lastEntry.color.R;
                buf[pos + 2] = lastEntry.color.G;
                buf[pos + 3] = lastEntry.color.B;
                pos += 4;
            }

            return buf;
        }

        /// <summary>
        /// End Custom RGB stream (cmd 0x36).
        /// </summary>
        public static byte[] BuildCustomEnd()
        {
            return MakeBuffer(Everest60Constants.CmdCustomEnd);
        }
    }
}
