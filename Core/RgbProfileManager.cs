using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using Everest60Rgb.Protocol;

namespace Everest60Rgb.Core
{
    /// <summary>
    /// Manages loading and saving the user's custom RGB configuration.
    /// Sets all 64 keys to the chosen background color and turns off all 44 perimeter LEDs
    /// so the 15 bottom LEDs can act as an isolated status bar.
    /// </summary>
    public static class RgbProfileManager
    {
        private static readonly string ProfileFileName = "rgb_map.json";

        public static string GetProfilePath()
        {
            string cwdPath = Path.Combine(Environment.CurrentDirectory, ProfileFileName);
            if (File.Exists(cwdPath)) return cwdPath;

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string localPath = Path.Combine(baseDir, ProfileFileName);
            if (File.Exists(localPath)) return localPath;

            return cwdPath;
        }

        /// <summary>
        /// Loads the custom RGB profile from disk into the framebuffer.
        /// If no profile exists, initializes all 64 keys to White (#FFFFFF)
        /// and turns off all 44 perimeter LEDs.
        /// </summary>
        public static bool LoadIntoFramebuffer(LedFramebuffer framebuffer)
        {
            if (framebuffer == null) return false;

            string path = GetProfilePath();
            if (File.Exists(path))
            {
                try
                {
                    string json = File.ReadAllText(path, Encoding.UTF8);
                    ParseAndApplyJson(json, framebuffer);
                    Console.WriteLine($"[INFO] Loaded RGB profile from: {path}");
                    return true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[WARN] Failed to read {path}: {ex.Message}");
                }
            }

            // Default baseline: Keys lit in White (#FFFFFF), all perimeter LEDs dark
            SetBackground(framebuffer, RgbColor.White, saveToDisk: false);
            return false;
        }

        /// <summary>
        /// Sets all 64 keys to the chosen background color, turns off ALL 44 perimeter LEDs
        /// (top, right, bottom, left), and optionally persists the configuration to rgb_map.json.
        /// </summary>
        public static void SetBackground(LedFramebuffer framebuffer, RgbColor keyColor, bool saveToDisk = true)
        {
            if (framebuffer == null) return;

            // 1. Set all 64 key LEDs to the background color
            foreach (byte hw in Everest60Constants.KeyLedIndices)
            {
                framebuffer.SetLed(hw, keyColor);
            }

            // 2. Turn off ALL 44 perimeter LEDs (top 15, right 8, bottom 15, left 6)
            foreach (byte hw in Everest60Constants.SideLedIndices)
            {
                framebuffer.SetLed(hw, RgbColor.Black);
            }

            Console.WriteLine($"[INFO] Configured background: Keys = {keyColor}, Perimeter LEDs = OFF (All 44 dark)");

            if (saveToDisk)
            {
                SaveFromFramebuffer(framebuffer);
            }
        }

        /// <summary>
        /// Saves the current framebuffer state to rgb_map.json.
        /// </summary>
        public static void SaveFromFramebuffer(LedFramebuffer framebuffer)
        {
            if (framebuffer == null) return;

            string path = GetProfilePath();
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("{");
                sb.AppendLine("  \"description\": \"Mountain Everest 60 Custom RGB Map (Keys lit, Perimeter OFF)\",");
                sb.AppendLine("  \"keys\": [");

                var keys = Everest60Constants.KeyLedIndices;
                for (int i = 0; i < keys.Length; i++)
                {
                    var c = framebuffer.GetLed(keys[i]);
                    string comma = (i < keys.Length - 1) ? "," : "";
                    sb.AppendLine($"    \"{c.ToHex()}\"{comma}");
                }

                sb.AppendLine("  ],");
                sb.AppendLine("  \"sides\": [");

                var sides = Everest60Constants.SideLedIndices;
                for (int i = 0; i < sides.Length; i++)
                {
                    var c = framebuffer.GetLed(sides[i]);
                    string comma = (i < sides.Length - 1) ? "," : "";
                    sb.AppendLine($"    \"{c.ToHex()}\"{comma}");
                }

                sb.AppendLine("  ]");
                sb.AppendLine("}");

                File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
                Console.WriteLine($"[INFO] Saved RGB profile to: {path}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Failed to save {path}: {ex.Message}");
            }
        }

        private static void ParseAndApplyJson(string json, LedFramebuffer framebuffer)
        {
            int keysIdx = json.IndexOf("\"keys\"", StringComparison.OrdinalIgnoreCase);
            if (keysIdx >= 0)
            {
                int arrStart = json.IndexOf('[', keysIdx);
                int arrEnd = json.IndexOf(']', arrStart);
                if (arrStart >= 0 && arrEnd > arrStart)
                {
                    string keysBody = json.Substring(arrStart + 1, arrEnd - arrStart - 1);
                    var tokens = keysBody.Split(new[] { ',', '\r', '\n', ' ', '\t', '"' }, StringSplitOptions.RemoveEmptyEntries);
                    var keyHws = Everest60Constants.KeyLedIndices;
                    for (int i = 0; i < tokens.Length && i < keyHws.Length; i++)
                    {
                        var color = RgbColor.FromHex(tokens[i]);
                        framebuffer.SetLed(keyHws[i], color);
                    }
                }
            }

            int sidesIdx = json.IndexOf("\"sides\"", StringComparison.OrdinalIgnoreCase);
            if (sidesIdx >= 0)
            {
                int arrStart = json.IndexOf('[', sidesIdx);
                int arrEnd = json.IndexOf(']', arrStart);
                if (arrStart >= 0 && arrEnd > arrStart)
                {
                    string sidesBody = json.Substring(arrStart + 1, arrEnd - arrStart - 1);
                    var tokens = sidesBody.Split(new[] { ',', '\r', '\n', ' ', '\t', '"' }, StringSplitOptions.RemoveEmptyEntries);
                    var sideHws = Everest60Constants.SideLedIndices;
                    for (int i = 0; i < tokens.Length && i < sideHws.Length; i++)
                    {
                        var color = RgbColor.FromHex(tokens[i]);
                        framebuffer.SetLed(sideHws[i], color);
                    }
                }
            }
        }
    }
}
