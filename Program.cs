using System;
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
    /// Runs directly in System Tray mode by default with console logging.
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

        private static void RunOneShotIndicator(StatusSource source, double value, RgbColor? customStatusColor = null)
        {
            using (var controller = new Everest60Controller())
            {
                if (!controller.Connect())
                {
                    Console.WriteLine("[ERROR] Mountain Everest 60 keyboard not found on USB.");
                    return;
                }

                int currentBrightness = controller.CachedBrightness;
                Console.WriteLine($"[INFO] Device brightness: {currentBrightness}%");
                string colorDesc = customStatusColor.HasValue ? customStatusColor.Value.ToHex() : "Dynamic Presets";
                Console.WriteLine($"[INFO] Applying {source} status ({value:P1}, Color: {colorDesc}) to 15 bottom perimeter LEDs...");

                var colors = BottomRowIndicator.ComputeProgressColors(value, source, customStatusColor);
                controller.Framebuffer.OverwriteBottomBorder(colors);

                bool ok = controller.SendFullCustomMap(currentBrightness);
                if (ok)
                {
                    Console.WriteLine("[INFO] Status update succeeded (keys preserved, perimeter off, bottom active).");
                }
                else
                {
                    Console.WriteLine("[ERROR] Failed to transmit USB reports.");
                }
            }
        }

        private static void SetBackground(RgbColor color)
        {
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

                if (ok)
                {
                    Console.WriteLine("[INFO] Background applied and saved to rgb_map.json.");
                }
                else
                {
                    Console.WriteLine("[ERROR] Failed to transmit USB reports.");
                }
            }
        }

        private static void SetBottomBorderColor(RgbColor color)
        {
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

                if (ok)
                {
                    Console.WriteLine("[INFO] Bottom perimeter color updated.");
                }
                else
                {
                    Console.WriteLine("[ERROR] Failed to transmit USB reports.");
                }
            }
        }

        private static void ClearBottomStatusOnly()
        {
            using (var controller = new Everest60Controller())
            {
                if (!controller.Connect())
                {
                    Console.WriteLine("[ERROR] Mountain Everest 60 keyboard not found on USB.");
                    return;
                }

                Console.WriteLine("[INFO] Clearing 15 bottom status LEDs (keys remain lit)...");
                bool ok = controller.ClearBottomBorderOnly();
                Console.WriteLine(ok ? "[INFO] Bottom LEDs cleared." : "[ERROR] Failed to transmit USB reports.");
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
                Console.WriteLine(ok ? "[INFO] All LEDs turned off." : "[ERROR] Failed to transmit USB reports.");
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
            Console.WriteLine("  (No arguments)             Launch background System Tray application (Default)");
            Console.WriteLine("  --battery, -b [hex]        Show battery % on bottom perimeter (preset or custom color)");
            Console.WriteLine("  --volume, -v [hex]         Show volume % on bottom perimeter (preset or custom color)");
            Console.WriteLine("  --cpu [hex]                Show CPU load % on bottom perimeter (preset or custom color)");
            Console.WriteLine("  --set-background <hex>     Set key background color and turn off perimeter ring");
            Console.WriteLine("  --color, -c <hex>          Set 15 bottom LEDs to a solid color");
            Console.WriteLine("  --clear-status             Turn off bottom status LEDs (keys stay lit)");
            Console.WriteLine("  --off                      Turn off all keyboard LEDs completely");
            Console.WriteLine("  --test                     Run USB communication diagnostic test");
            Console.WriteLine("  --help, -h                 Show this reference information");
        }
    }
}
