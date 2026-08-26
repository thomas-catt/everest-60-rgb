using System;
using System.Diagnostics;
using System.Globalization;
using System.Windows.Forms;
using Everest60Rgb.Animations;
using Everest60Rgb.Core;
using Everest60Rgb.Hid;
using Everest60Rgb.Indicators;
using Everest60Rgb.Protocol;
using Everest60Rgb.UI;

namespace Everest60Rgb
{
    /// <summary>
    /// Application entry point for the Mountain Everest 60 Status Indicator.
    /// Runs directly in System Tray mode by default with console logging,
    /// or executes one-shot CLI commands for external automation scripts.
    /// </summary>
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            // If command-line arguments are provided, execute one-shot CLI commands with logging
            if (args.Length > 0)
            {
                string cmd = args[0].ToLowerInvariant();
                switch (cmd)
                {
                    case "--status":
                    case "--set-status":
                    case "-s":
                    case "--percent":
                    case "-p":
                        if (args.Length > 1)
                        {
                            double progress = ParseProgress(args[1]);
                            RgbColor? customColor = (args.Length > 2) ? RgbColor.FromHex(args[2]) : (RgbColor?)null;
                            RunManualStatus(progress, customColor);
                        }
                        else
                        {
                            Console.WriteLine("[ERROR] Missing percentage argument. Example: --status 75 \"#00FFCC\" or -s 50");
                        }
                        return;

                    case "--battery":
                    case "-b":
                        RgbColor? batColor = (args.Length > 1) ? RgbColor.FromHex(args[1]) : (RgbColor?)null;
                        RunOneShotIndicator(StatusSource.Battery, SystemMonitors.GetBatteryPercentage(), batColor);
                        return;

                    case "--volume":
                    case "-v":
                        RgbColor? volColor = (args.Length > 1) ? RgbColor.FromHex(args[1]) : (RgbColor?)null;
                        RunOneShotIndicator(StatusSource.Volume, SystemMonitors.GetMasterVolume(), volColor);
                        return;

                    case "--cpu":
                        RgbColor? cpuColor = (args.Length > 1) ? RgbColor.FromHex(args[1]) : (RgbColor?)null;
                        RunOneShotIndicator(StatusSource.Cpu, SystemMonitors.GetCpuUsage(), cpuColor);
                        return;

                    case "--set-background":
                    case "--bg":
                        if (args.Length > 1)
                        {
                            var bg = RgbColor.FromHex(args[1]);
                            SetBackground(bg);
                        }
                        else
                        {
                            Console.WriteLine("[ERROR] Missing hex color argument. Example: --set-background \"#FFFFFF\"");
                        }
                        return;

                    case "--color":
                    case "-c":
                        if (args.Length > 1)
                        {
                            var color = RgbColor.FromHex(args[1]);
                            SetBottomBorderColor(color);
                        }
                        else
                        {
                            Console.WriteLine("[ERROR] Missing hex color argument. Example: --color \"#00FFCC\"");
                        }
                        return;

                    case "--clear-status":
                    case "--off-bottom":
                        ClearBottomStatusOnly();
                        return;

                    case "--off":
                        TurnOffAll();
                        return;

                    case "--test":
                        RunDiagnostics();
                        return;

                    case "--help":
                    case "-h":
                    case "/?":
                        PrintHelp();
                        return;

                    default:
                        // If the first argument is a number (e.g. `Everest60Rgb.exe 75 #00FFCC`), interpret as status
                        if (double.TryParse(args[0].TrimEnd('%'), NumberStyles.Any, CultureInfo.InvariantCulture, out _))
                        {
                            double progress = ParseProgress(args[0]);
                            RgbColor? customColor = (args.Length > 1) ? RgbColor.FromHex(args[1]) : (RgbColor?)null;
                            RunManualStatus(progress, customColor);
                            return;
                        }

                        Console.WriteLine($"[WARN] Unknown argument '{args[0]}'. Launching System Tray mode...");
                        break;
                }
            }

            // Default execution: Pure System Tray application
            RunTray();
        }

        private static void RunTray()
        {
            Console.WriteLine("==========================================================");
            Console.WriteLine("  Mountain Everest 60 - Bottom Perimeter Status Indicator ");
            Console.WriteLine("  Running in System Tray mode...                          ");
            Console.WriteLine("==========================================================");

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            var controller = new Everest60Controller();
            controller.Connect();

            var engine = new AnimationEngine(controller);
            var trayContext = new TrayApplicationContext(controller, engine);

            Application.Run(trayContext);
        }

        private static double ParseProgress(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return 0.0;
            string clean = input.Trim().TrimEnd('%');
            if (double.TryParse(clean, NumberStyles.Any, CultureInfo.InvariantCulture, out double val))
            {
                if (val > 1.0) val /= 100.0; // Convert 75 -> 0.75
                return Math.Max(0.0, Math.Min(1.0, val));
            }
            return 0.0;
        }

        private static void RunManualStatus(double progress, RgbColor? customColor)
        {
            var sw = Stopwatch.StartNew();
            using (var controller = new Everest60Controller())
            {
                if (!controller.Connect())
                {
                    Console.WriteLine("[ERROR] Mountain Everest 60 keyboard not found on USB.");
                    return;
                }

                int currentBrightness = controller.CachedBrightness;
                string colorDesc = customColor.HasValue ? customColor.Value.ToHex() : "Dynamic Presets";
                Console.WriteLine($"[INFO] Setting manual status bar to {progress:P1} (Color: {colorDesc}) on 15 bottom LEDs...");

                var colors = BottomRowIndicator.ComputeProgressColors(progress, StatusSource.Battery, customColor);
                controller.Framebuffer.OverwriteBottomBorder(colors);

                bool ok = controller.SendFullCustomMap(currentBrightness);
                sw.Stop();

                if (ok)
                {
                    Console.WriteLine($"[SUCCESS] Status applied and latched by Everest 60 hardware in {sw.ElapsedMilliseconds}ms (keys preserved, perimeter off).");
                }
                else
                {
                    Console.WriteLine("[ERROR] Failed to transmit USB reports.");
                }
            }
        }

        private static void RunOneShotIndicator(StatusSource source, double value, RgbColor? customStatusColor = null)
        {
            var sw = Stopwatch.StartNew();
            using (var controller = new Everest60Controller())
            {
                if (!controller.Connect())
                {
                    Console.WriteLine("[ERROR] Mountain Everest 60 keyboard not found on USB.");
                    return;
                }

                int currentBrightness = controller.CachedBrightness;
                string colorDesc = customStatusColor.HasValue ? customStatusColor.Value.ToHex() : "Dynamic Presets";
                Console.WriteLine($"[INFO] Applying {source} status ({value:P1}, Color: {colorDesc}) to 15 bottom perimeter LEDs...");

                var colors = BottomRowIndicator.ComputeProgressColors(value, source, customStatusColor);
                controller.Framebuffer.OverwriteBottomBorder(colors);

                bool ok = controller.SendFullCustomMap(currentBrightness);
                sw.Stop();

                if (ok)
                {
                    Console.WriteLine($"[SUCCESS] {source} status applied and latched in {sw.ElapsedMilliseconds}ms (keys preserved, perimeter off).");
                }
                else
                {
                    Console.WriteLine("[ERROR] Failed to transmit USB reports.");
                }
            }
        }

        private static void SetBackground(RgbColor color)
        {
            var sw = Stopwatch.StartNew();
            using (var controller = new Everest60Controller())
            {
                if (!controller.Connect())
                {
                    Console.WriteLine("[ERROR] Mountain Everest 60 keyboard not found on USB.");
                    return;
                }

                int currentBrightness = controller.CachedBrightness;
                Console.WriteLine($"[INFO] Setting key background to {color} and turning off all perimeter LEDs...");

                RgbProfileManager.SetBackground(controller.Framebuffer, color, saveToDisk: true);
                bool ok = controller.SendFullCustomMap(currentBrightness);
                sw.Stop();

                if (ok)
                {
                    Console.WriteLine($"[SUCCESS] Background applied and latched by hardware in {sw.ElapsedMilliseconds}ms (saved to rgb_map.json).");
                }
                else
                {
                    Console.WriteLine("[ERROR] Failed to transmit USB reports.");
                }
            }
        }

        private static void SetBottomBorderColor(RgbColor color)
        {
            var sw = Stopwatch.StartNew();
            using (var controller = new Everest60Controller())
            {
                if (!controller.Connect())
                {
                    Console.WriteLine("[ERROR] Mountain Everest 60 keyboard not found on USB.");
                    return;
                }

                int currentBrightness = controller.CachedBrightness;
                Console.WriteLine($"[INFO] Setting 15 bottom perimeter LEDs to {color}...");

                var colors = new RgbColor[Everest60Constants.BottomBorderLedCount];
                for (int i = 0; i < colors.Length; i++) colors[i] = color;

                controller.Framebuffer.OverwriteBottomBorder(colors);
                bool ok = controller.SendFullCustomMap(currentBrightness);
                sw.Stop();

                if (ok)
                {
                    Console.WriteLine($"[SUCCESS] Bottom perimeter color applied in {sw.ElapsedMilliseconds}ms.");
                }
                else
                {
                    Console.WriteLine("[ERROR] Failed to transmit USB reports.");
                }
            }
        }

        private static void ClearBottomStatusOnly()
        {
            var sw = Stopwatch.StartNew();
            using (var controller = new Everest60Controller())
            {
                if (!controller.Connect())
                {
                    Console.WriteLine("[ERROR] Mountain Everest 60 keyboard not found on USB.");
                    return;
                }

                Console.WriteLine("[INFO] Clearing 15 bottom status LEDs (keys remain lit)...");
                bool ok = controller.ClearBottomBorderOnly();
                sw.Stop();

                Console.WriteLine(ok ? $"[SUCCESS] Bottom LEDs cleared in {sw.ElapsedMilliseconds}ms." : "[ERROR] Failed to transmit USB reports.");
            }
        }

        private static void TurnOffAll()
        {
            using (var controller = new Everest60Controller())
            {
                if (!controller.Connect())
                {
                    Console.WriteLine("[ERROR] Mountain Everest 60 keyboard not found on USB.");
                    return;
                }

                Console.WriteLine("[INFO] Turning off all keyboard LEDs...");
                bool ok = controller.TurnOffAll();
                Console.WriteLine(ok ? "[SUCCESS] All LEDs turned off." : "[ERROR] Failed to transmit USB reports.");
            }
        }

        private static void RunDiagnostics()
        {
            Console.WriteLine("==========================================================");
            Console.WriteLine("  Everest 60 - Diagnostic Self-Test                       ");
            Console.WriteLine("==========================================================");

            Console.WriteLine("[INFO] Checking 15 Bottom Perimeter Hardware Indices:");
            Console.WriteLine($"  -> Indices (Left to Right): {string.Join(", ", Everest60Constants.BottomBorderLeds)}");
            Console.WriteLine($"  -> Active Profile Path: {RgbProfileManager.GetProfilePath()}");

            using (var controller = new Everest60Controller())
            {
                if (controller.Connect())
                {
                    Console.WriteLine($"[INFO] Connected to device: PID=0x{controller.ProductId:X4}");
                    Console.WriteLine($"  -> Device Path: {controller.DevicePath}");

                    int bri = controller.CachedBrightness;
                    Console.WriteLine($"  -> Hardware Brightness: {bri}%");

                    Console.WriteLine("[INFO] Transmitting test status frame...");
                    var colors = new RgbColor[Everest60Constants.BottomBorderLedCount];
                    for (int i = 0; i < colors.Length; i++) colors[i] = RgbColor.Cyan;
                    controller.Framebuffer.OverwriteBottomBorder(colors);
                    bool ok = controller.SendFullCustomMap(bri);

                    Console.WriteLine($"[INFO] Transmission result: {(ok ? "PASS" : "FAIL")}");
                }
                else
                {
                    Console.WriteLine("[ERROR] Mountain Everest 60 not detected on USB.");
                }
            }
            Console.WriteLine("==========================================================");
        }

        private static void PrintHelp()
        {
            Console.WriteLine("Everest 60 Status Indicator - Command Line Reference:");
            Console.WriteLine("  (No arguments)                      Launch background System Tray application (Default)");
            Console.WriteLine("  --status, -s <0-100> [hex]          Manually set status bar percentage and optional color");
            Console.WriteLine("  --battery, -b [hex]                 Show battery % on bottom perimeter (preset or custom color)");
            Console.WriteLine("  --volume, -v [hex]                  Show volume % on bottom perimeter (preset or custom color)");
            Console.WriteLine("  --cpu [hex]                         Show CPU load % on bottom perimeter (preset or custom color)");
            Console.WriteLine("  --set-background <hex>              Set key background color and turn off perimeter ring");
            Console.WriteLine("  --color, -c <hex>                   Set 15 bottom LEDs to a solid color");
            Console.WriteLine("  --clear-status                      Turn off bottom status LEDs (keys stay lit)");
            Console.WriteLine("  --off                               Turn off all keyboard LEDs completely");
            Console.WriteLine("  --test                              Run USB communication diagnostic test");
            Console.WriteLine("  --help, -h                          Show this reference information");
            Console.WriteLine();
            Console.WriteLine("Examples for External Automation:");
            Console.WriteLine("  Everest60Rgb.exe --status 75 \"#00FFCC\"");
            Console.WriteLine("  Everest60Rgb.exe --status 25 \"#FF2200\"");
            Console.WriteLine("  Everest60Rgb.exe -s 100 \"#00FF88\"");
            Console.WriteLine("  Everest60Rgb.exe -s 50");
        }
    }
}
