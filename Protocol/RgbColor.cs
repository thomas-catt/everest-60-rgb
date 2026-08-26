using System;
using System.Globalization;

namespace Everest60Rgb.Protocol
{
    /// <summary>
    /// Immutable RGB colour triplet used throughout the lighting pipeline.
    /// </summary>
    public struct RgbColor : IEquatable<RgbColor>
    {
        public byte R { get; }
        public byte G { get; }
        public byte B { get; }

        // ── Well-known colours ───────────────────────────────────────────────
        public static readonly RgbColor Black   = new RgbColor(0, 0, 0);
        public static readonly RgbColor White   = new RgbColor(255, 255, 255);
        public static readonly RgbColor Red     = new RgbColor(255, 0, 0);
        public static readonly RgbColor Green   = new RgbColor(0, 255, 0);
        public static readonly RgbColor Blue    = new RgbColor(0, 0, 255);
        public static readonly RgbColor Yellow  = new RgbColor(255, 255, 0);
        public static readonly RgbColor Cyan    = new RgbColor(0, 255, 255);
        public static readonly RgbColor Magenta = new RgbColor(255, 0, 255);
        public static readonly RgbColor Orange  = new RgbColor(255, 140, 0);
        public static readonly RgbColor Purple  = new RgbColor(128, 0, 128);

        public RgbColor(byte r, byte g, byte b)
        {
            R = r;
            G = g;
            B = b;
        }

        // ── Factory methods ──────────────────────────────────────────────────

        /// <summary>
        /// Parse a hex colour string.  Accepts "#RRGGBB", "RRGGBB", "#RGB", "RGB".
        /// Returns <see cref="Black"/> on invalid input.
        /// </summary>
        public static RgbColor FromHex(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex))
                return Black;

            hex = hex.Trim().TrimStart('#');

            if (hex.Length == 3)
            {
                // Expand shorthand: "F0C" → "FF00CC"
                byte r = byte.Parse(new string(hex[0], 2), NumberStyles.HexNumber);
                byte g = byte.Parse(new string(hex[1], 2), NumberStyles.HexNumber);
                byte b = byte.Parse(new string(hex[2], 2), NumberStyles.HexNumber);
                return new RgbColor(r, g, b);
            }

            if (hex.Length >= 6)
            {
                byte r = byte.Parse(hex.Substring(0, 2), NumberStyles.HexNumber);
                byte g = byte.Parse(hex.Substring(2, 2), NumberStyles.HexNumber);
                byte b = byte.Parse(hex.Substring(4, 2), NumberStyles.HexNumber);
                return new RgbColor(r, g, b);
            }

            return Black;
        }

        /// <summary>
        /// Create a colour from HSV.  Hue is in degrees [0, 360), saturation and
        /// value are in the range [0, 1].
        /// </summary>
        public static RgbColor FromHsv(double hue, double saturation, double value)
        {
            while (hue < 0) hue += 360;
            while (hue >= 360) hue -= 360;

            saturation = Clamp01(saturation);
            value = Clamp01(value);

            int hi = ((int)Math.Floor(hue / 60)) % 6;
            double f = hue / 60.0 - Math.Floor(hue / 60.0);

            double v = value * 255.0;
            byte bv = (byte)v;
            byte bp = (byte)(v * (1.0 - saturation));
            byte bq = (byte)(v * (1.0 - f * saturation));
            byte bt = (byte)(v * (1.0 - (1.0 - f) * saturation));

            switch (hi)
            {
                case 0: return new RgbColor(bv, bt, bp);
                case 1: return new RgbColor(bq, bv, bp);
                case 2: return new RgbColor(bp, bv, bt);
                case 3: return new RgbColor(bp, bq, bv);
                case 4: return new RgbColor(bt, bp, bv);
                default: return new RgbColor(bv, bp, bq);
            }
        }

        // ── Colour math ──────────────────────────────────────────────────────

        /// <summary>Linear interpolation between two colours.</summary>
        public static RgbColor Lerp(RgbColor a, RgbColor b, double t)
        {
            t = Clamp01(t);
            return new RgbColor(
                (byte)(a.R + (b.R - a.R) * t),
                (byte)(a.G + (b.G - a.G) * t),
                (byte)(a.B + (b.B - a.B) * t));
        }

        /// <summary>Scale brightness by a 0–1 factor.</summary>
        public RgbColor Dim(double factor)
        {
            factor = Clamp01(factor);
            return new RgbColor(
                (byte)(R * factor),
                (byte)(G * factor),
                (byte)(B * factor));
        }

        // ── Formatting ──────────────────────────────────────────────────────

        /// <summary>Returns "#RRGGBB" hex string.</summary>
        public string ToHex() => $"#{R:X2}{G:X2}{B:X2}";

        public override string ToString() => $"RGB({R}, {G}, {B})";

        // ── Equality ─────────────────────────────────────────────────────────

        public bool Equals(RgbColor other) => R == other.R && G == other.G && B == other.B;
        public override bool Equals(object obj) => obj is RgbColor c && Equals(c);

        public override int GetHashCode()
        {
            // FNV-1a
            unchecked
            {
                int h = (int)2166136261;
                h = (h * 16777619) ^ R;
                h = (h * 16777619) ^ G;
                h = (h * 16777619) ^ B;
                return h;
            }
        }

        public static bool operator ==(RgbColor left, RgbColor right) => left.Equals(right);
        public static bool operator !=(RgbColor left, RgbColor right) => !left.Equals(right);

        // ── Helpers ──────────────────────────────────────────────────────────

        private static double Clamp01(double v) => v < 0.0 ? 0.0 : v > 1.0 ? 1.0 : v;
    }
}
