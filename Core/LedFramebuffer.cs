using System;
using System.Collections.Generic;
using Everest60Rgb.Protocol;

namespace Everest60Rgb.Core
{
    /// <summary>
    /// In-memory RGB framebuffer for the Everest 60.
    /// Manages the full 108-LED state map initialized from the user's custom RGB profile,
    /// and provides fast accessors for overwriting the 15 bottom perimeter status LEDs.
    /// </summary>
    public class LedFramebuffer
    {
        private const int MaxHwIndex = 170;
        private readonly RgbColor[] _leds = new RgbColor[MaxHwIndex];
        private readonly bool[] _valid = new bool[MaxHwIndex];
        private readonly object _lock = new object();

        public LedFramebuffer()
        {
            foreach (byte hw in Everest60Constants.All108LedIndices)
            {
                _valid[hw] = true;
                _leds[hw] = RgbColor.White; // Default to neutral White so keys stay lit
            }

            // Load saved custom RGB profile if present
            RgbProfileManager.LoadIntoFramebuffer(this);
        }

        /// <summary>
        /// Set the entire board to a uniform baseline color.
        /// </summary>
        public void SetBaseline(RgbColor color)
        {
            lock (_lock)
            {
                foreach (byte hw in Everest60Constants.All108LedIndices)
                {
                    _leds[hw] = color;
                }
            }
        }

        /// <summary>
        /// Reloads the custom RGB map from disk.
        /// </summary>
        public void ReloadProfile()
        {
            lock (_lock)
            {
                RgbProfileManager.LoadIntoFramebuffer(this);
            }
        }

        /// <summary>
        /// Overwrite the 15 bottom perimeter LEDs with the computed status indication colors (Left to Right).
        /// Leaves all keys and other perimeter LEDs untouched in their custom RGB map state.
        /// </summary>
        public void OverwriteBottomBorder(IReadOnlyList<RgbColor> colors)
        {
            if (colors == null) return;
            lock (_lock)
            {
                var indices = Everest60Constants.BottomBorderLeds;
                int count = Math.Min(indices.Length, colors.Count);
                for (int i = 0; i < count; i++)
                {
                    _leds[indices[i]] = colors[i];
                }
            }
        }

        /// <summary>
        /// Get the 15 (hwIndex, color) tuples for fast bottom-only transmission.
        /// </summary>
        public List<(byte hwIndex, RgbColor color)> GetBottomBorderEntries()
        {
            var list = new List<(byte, RgbColor)>(Everest60Constants.BottomBorderLedCount);
            lock (_lock)
            {
                foreach (byte hw in Everest60Constants.BottomBorderLeds)
                {
                    list.Add((hw, _leds[hw]));
                }
            }
            return list;
        }

        /// <summary>
        /// Get all 108 (hwIndex, color) tuples for full frame synchronization.
        /// </summary>
        public List<(byte hwIndex, RgbColor color)> GetAllEntries()
        {
            var list = new List<(byte, RgbColor)>(Everest60Constants.TotalLeds);
            lock (_lock)
            {
                foreach (byte hw in Everest60Constants.All108LedIndices)
                {
                    list.Add((hw, _leds[hw]));
                }
            }
            return list;
        }

        public void SetLed(byte hwIndex, RgbColor color)
        {
            if (hwIndex < MaxHwIndex)
            {
                lock (_lock)
                {
                    _leds[hwIndex] = color;
                }
            }
        }

        public RgbColor GetLed(byte hwIndex)
        {
            if (hwIndex < MaxHwIndex)
            {
                lock (_lock)
                {
                    return _leds[hwIndex];
                }
            }
            return RgbColor.Black;
        }
    }
}
