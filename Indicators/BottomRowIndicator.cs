using System;
using Everest60Rgb.Protocol;

namespace Everest60Rgb.Indicators
{
    /// <summary>
    /// Supported system status indication sources for the 15 bottom perimeter LEDs.
    /// </summary>
    public enum StatusSource
    {
        Battery,
        Volume,
        Cpu,
        SolidColor,
        Off
    }

    /// <summary>
    /// Computes high-precision RGB colors for the 15 bottom perimeter LEDs based on system status.
    /// Supports dynamic presets (Green/Yellow/Red for CPU/Battery, Cyan/Magenta for Volume)
    /// OR user-selected custom status bar colors.
    /// </summary>
    public static class BottomRowIndicator
    {
        public const int LedCount = Everest60Constants.BottomBorderLedCount; // 15 LEDs

        /// <summary>
        /// Compute the 15 RGB colors for a normalized progress value [0.0..1.0].
        /// If <paramref name="customColor"/> is specified, uses that uniform color across the progress bar;
        /// otherwise uses the built-in dynamic gradient presets.
        /// </summary>
        public static RgbColor[] ComputeProgressColors(double progress, StatusSource source, RgbColor? customColor = null)
        {
            progress = Math.Max(0.0, Math.Min(1.0, progress));
            var colors = new RgbColor[LedCount];
            var bg = RgbColor.Black;

            double litFraction = progress * LedCount;
            int fullLeds = (int)Math.Floor(litFraction);
            double partialFraction = litFraction - fullLeds;

            for (int i = 0; i < LedCount; i++)
            {
                double normalizedPos = (double)i / Math.Max(1, LedCount - 1);
                if (i < fullLeds)
                {
                    colors[i] = GetIndicatorColor(progress, normalizedPos, source, customColor);
                }
                else if (i == fullLeds && partialFraction > 0.02)
                {
                    var targetColor = GetIndicatorColor(progress, normalizedPos, source, customColor);
                    colors[i] = RgbColor.Lerp(bg, targetColor, partialFraction);
                }
                else
                {
                    colors[i] = bg;
                }
            }

            return colors;
        }

        private static RgbColor GetIndicatorColor(double progress, double normalizedPos, StatusSource source, RgbColor? customColor)
        {
            // If user specified a custom color for the status LEDs, use it
            if (customColor.HasValue)
            {
                return customColor.Value;
            }

            // Otherwise use default dynamic presets
            switch (source)
            {
                case StatusSource.Battery:
                    // Battery indicator: Red (<=20%), Orange (<=45%), Yellow (<=70%), Green (>70%)
                    if (progress <= 0.20) return RgbColor.Red;
                    if (progress <= 0.45) return RgbColor.Orange;
                    if (progress <= 0.70) return RgbColor.Yellow;
                    return RgbColor.Green;

                case StatusSource.Volume:
                    // Cyan (quiet) -> Blue (medium) -> Magenta (loud)
                    if (normalizedPos <= 0.5)
                        return RgbColor.Lerp(RgbColor.Cyan, RgbColor.Blue, normalizedPos * 2.0);
                    return RgbColor.Lerp(RgbColor.Blue, RgbColor.Magenta, (normalizedPos - 0.5) * 2.0);

                case StatusSource.Cpu:
                    // Green (cool/idle) -> Yellow (moderate) -> Red (high load)
                    if (normalizedPos <= 0.5)
                        return RgbColor.Lerp(RgbColor.Green, RgbColor.Yellow, normalizedPos * 2.0);
                    return RgbColor.Lerp(RgbColor.Yellow, RgbColor.Red, (normalizedPos - 0.5) * 2.0);

                default:
                    return RgbColor.Cyan;
            }
        }
    }
}
