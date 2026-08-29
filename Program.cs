using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
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
    /// Runs silently in System Tray mode by default (no command prompt window spawned).
    /// If launched from an existing terminal (PowerShell/CMD), attaches to it to print logs seamlessly.
    /// CLI commands execute synchronously and exit immediately with an appropriate return code.
    /// </summary>
    internal static class Program
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(uint dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AllocConsole();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeConsole();

        private const uint ATTACH_PARENT_PROCESS = 0xFFFFFFFF;
        private static bool _attachedConsole = false;

        [STAThread]
        private static void Main(string[] args)
        {
            SetupConsole(args);

            try
            {
                // If command-line arguments are provided, execute one-shot CLI commands and exit immediately
                if (args != null && args.Length > 0)
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
                                bool ok = RunManualStatus(progress, customColor);
                                ExitProcess(ok ? 0 : 1);
                            }
                            else
                            {
                                Console.WriteLine("[ERROR] Missing percentage argument. Example: --status 75 \"#00FFCC\" or -s 50");
                                ExitProcess(1);
                            }
                            return;

                        case "--battery":
                        case "-b":
                            RgbColor? batColor = (args.Length > 1) ? RgbColor.FromHex(args[1]) : (RgbColor?)null;
                            bool batOk = RunOneShotIndicator(StatusSource.Battery, SystemMonitors.GetBatteryPercentage(), batColor);
                            ExitProcess(batOk ? 0 : 1);
                            return;

                        case "--volume":
                        case "-v":
                            RgbColor? volColor = (args.Length > 1) ? RgbColor.FromHex(args[1]) : (RgbColor?)null;
                            bool volOk = RunOneShotIndicator(StatusSource.Volume, SystemMonitors.GetMasterVolume(), volColor);
                            ExitProcess(volOk ? 0 : 1);
                            return;

                        case "--cpu":
                            RgbColor? cpuColor = (args.Length > 1) ? RgbColor.FromHex(args[1]) : (RgbColor?)null;
                            bool cpuOk = RunOneShotIndicator(StatusSource.Cpu, SystemMonitors.GetCpuUsage(), cpuColor);
                            ExitProcess(cpuOk ? 0 : 1);
                            return;

                        case "--set-background":
                        case "--bg":
                            if (args.Length > 1)
                            {
                                var bg = RgbColor.FromHex(args[1]);
                                bool bgOk = SetBackground(bg);
                                ExitProcess(bgOk ? 0 : 1);
                            }
                            else
                            {
                                Console.WriteLine("[ERROR] Missing hex color argument. Example: --set-background \"#FFFFFF\"");
                                ExitProcess(1);
                            }
                            return;

                        case "--color":
                        case "-c":
                            if (args.Length > 1)
                            {
                                var color = RgbColor.FromHex(args[1]);
                                bool cOk = SetBottomBorderColor(color);
                                ExitProcess(cOk ? 0 : 1);
                            }
                            else
                            {
                                Console.WriteLine("[ERROR] Missing hex color argument. Example: --color \"#00FFCC\"");
                                ExitProcess(1);
                            }
                            return;

                        case "--clear-status":
                        case "--off-bottom":
                            bool clearOk = ClearBottomStatusOnly();
                            ExitProcess(clearOk ? 0 : 1);
                            return;

                        case "--off":
                            bool offOk = TurnOffAll();
                            ExitProcess(offOk ? 0 : 1);
                            return;

                        case "--test":
                            bool testOk = RunDiagnostics();
                            ExitProcess(testOk ? 0 : 1);
                            return;

                        case "--console":
                        case "--debug":
                        case "-d":
                            // Launch tray app with console enabled
                            RunTray();
                            return;

                        case "--tray":
                        case "-t":
                            RunTray();
                            return;

                        case "--help":
                        case "-h":
                        case "/?":
                            PrintHelp();
                            ExitProcess(0);
                            return;

                        default:
                            // If the first argument is a number (e.g. `Everest60Rgb.exe 75 #00FFCC`), interpret as status
                            if (double.TryParse(args[0].TrimEnd('%'), NumberStyles.Any, CultureInfo.InvariantCulture, out _))
                            {
                                double progress = ParseProgress(args[0]);
                                RgbColor? customColor = (args.Length > 1) ? RgbColor.FromHex(args[1]) : (RgbColor?)null;
                                bool ok = RunManualStatus(progress, customColor);
                                ExitProcess(ok ? 0 : 1);
                                return;
                            }

                            Console.WriteLine($"[ERROR] Unknown argument '{args[0]}'.");
                            PrintHelp();
                            ExitProcess(1);
                            return;
                    }
                }

                // Default execution: Pure silent System Tray application
                RunTray();
            }
            finally
            {
                CleanupConsole();
            }
        }

        private static void ExitProcess(int exitCode)
        {
            CleanupConsole();
            Environment.Exit(exitCode);
        }

        private static void SetupConsole(string[] args)
        {
            // 1. Try to attach to the calling parent console (PowerShell, CMD, Windows Terminal)
            if (AttachConsole(ATTACH_PARENT_PROCESS))
            {
                RedirectConsoleStreams();
                _attachedConsole = true;
            }
            // 2. If launched from GUI/Explorer and user passed --console or --debug, open a new console window
            else if (args != null && Array.Exists(args, a => a.Equals("--console", StringComparison.OrdinalIgnoreCase) ||
                                                            a.Equals("--debug", StringComparison.OrdinalIgnoreCase) ||
                                                            a.Equals("-d", StringComparison.OrdinalIgnoreCase) ||
                                                            a.Equals("--log", StringComparison.OrdinalIgnoreCase)))
            {
                if (AllocConsole())
                {
                    RedirectConsoleStreams();
                    _attachedConsole = true;
                    try { Console.Title = "Everest 60 Status Indicator - Logs"; } catch { }
                }
            }
        }

        private static void RedirectConsoleStreams()
        {
            try
            {
                var conOut = Win32Hid.CreateFileW(
                    "CONOUT$",
                    Win32Hid.GENERIC_READ | Win32Hid.GENERIC_WRITE,
                    Win32Hid.FILE_SHARE_READ | Win32Hid.FILE_SHARE_WRITE,
                    IntPtr.Zero,
                    Win32Hid.OPEN_EXISTING,
                    0,
                    IntPtr.Zero);

                if (!conOut.IsInvalid)
                {
                    var fsOut = new FileStream(conOut, FileAccess.Write);
                    var writerOut = new StreamWriter(fsOut, Console.OutputEncoding) { AutoFlush = true };
                    Console.SetOut(writerOut);
                    Console.SetError(writerOut);
                }
            }
            catch
            {
                // Redirection fallback
            }
        }

        private static void CleanupConsole()
        {
            if (_attachedConsole)
            {
                _attachedConsole = false;
                try
                {
                    FreeConsole();
                }
                catch { }
            }
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
                // 1. If input explicitly has a '%' sign (e.g. "1%", "50%", "100%", "0.5%")
                if (input.Contains("%"))
                {
                    return Math.Max(0.0, Math.Min(1.0, val / 100.0));
                }

                // 2. If entered as 0..100 percentage integer/decimal > 1.0 (e.g. 50, 75, 100)
                if (val > 1.0)
                {
                    return Math.Max(0.0, Math.Min(1.0, val / 100.0));
                }

                // 3. If entered as "1" or "1.0" for 1%
                if (clean == "1" || clean == "1.0" || clean == "1.00")
                {
                    return 0.01;
                }

                // 4. If entered as a decimal fraction like 0.75 (75%) or 0.25 (25%) or 0 (0%)
                return Math.Max(0.0, Math.Min(1.0, val));
            }
            return 0.0;
        }

        private static bool RunManualStatus(double progress, RgbColor? customColor)
        {
            var sw = Stopwatch.StartNew();
            using (var controller = new Everest60Controller())
            {
                if (!controller.Connect())
                {
                    Console.WriteLine("[ERROR] Mountain Everest 60 keyboard not found on USB.");
                    return false;
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
                    return true;
                }
                else
                {
                    Console.WriteLine("[ERROR] Failed to transmit USB reports.");
                    return false;
                }
            }
        }

        private static bool RunOneShotIndicator(StatusSource source, double value, RgbColor? customStatusColor = null)
        {
            var sw = Stopwatch.StartNew();
            using (var controller = new Everest60Controller())
            {
                if (!controller.Connect())
                {
                    Console.WriteLine("[ERROR] Mountain Everest 60 keyboard not found on USB.");
                    return false;
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
                    return true;
                }
                else
                {
                    Console.WriteLine("[ERROR] Failed to transmit USB reports.");
                    return false;
                }
            }
        }

        private static bool SetBackground(RgbColor color)
        {
            var sw = Stopwatch.StartNew();
            using (var controller = new Everest60Controller())
            {
                if (!controller.Connect())
                {
                    Console.WriteLine("[ERROR] Mountain Everest 60 keyboard not found on USB.");
                    return false;
                }

                int currentBrightness = controller.CachedBrightness;
                Console.WriteLine($"[INFO] Setting key background to {color} and turning off all perimeter LEDs...");

                RgbProfileManager.SetBackground(controller.Framebuffer, color, saveToDisk: true);
                bool ok = controller.SendFullCustomMap(currentBrightness);
                sw.Stop();

                if (ok)
                {
                    Console.WriteLine($"[SUCCESS] Background applied and latched by hardware in {sw.ElapsedMilliseconds}ms (saved to rgb_map.json).");
                    return true;
                }
                else
                {
                    Console.WriteLine("[ERROR] Failed to transmit USB reports.");
                    return false;
                }
            }
        }

        private static bool SetBottomBorderColor(RgbColor color)
        {
            var sw = Stopwatch.StartNew();
            using (var controller = new Everest60Controller())
            {
                if (!controller.Connect())
                {
                    Console.WriteLine("[ERROR] Mountain Everest 60 keyboard not found on USB.");
                    return false;
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
                    return true;
                }
                else
                {
                    Console.WriteLine("[ERROR] Failed to transmit USB reports.");
                    return false;
                }
            }
        }

        private static bool ClearBottomStatusOnly()
        {
            var sw = Stopwatch.StartNew();
            using (var controller = new Everest60Controller())
            {
                if (!controller.Connect())
                {
                    Console.WriteLine("[ERROR] Mountain Everest 60 keyboard not found on USB.");
                    return false;
                }

                Console.WriteLine("[INFO] Clearing 15 bottom status LEDs (keys remain lit)...");
                bool ok = controller.ClearBottomBorderOnly();
                sw.Stop();

                if (ok)
                {
                    Console.WriteLine($"[SUCCESS] Bottom LEDs cleared in {sw.ElapsedMilliseconds}ms.");
                    return true;
                }
                else
                {
                    Console.WriteLine("[ERROR] Failed to transmit USB reports.");
                    return false;
                }
            }
        }

        private static bool TurnOffAll()
        {
            using (var controller = new Everest60Controller())
            {
                if (!controller.Connect())
                {
                    Console.WriteLine("[ERROR] Mountain Everest 60 keyboard not found on USB.");
                    return false;
                }

                Console.WriteLine("[INFO] Turning off all keyboard LEDs...");
                bool ok = controller.TurnOffAll();
                if (ok)
                {
                    Console.WriteLine("[SUCCESS] All LEDs turned off.");
                    return true;
                }
                else
                {
                    Console.WriteLine("[ERROR] Failed to transmit USB reports.");
                    return false;
                }
            }
        }

        private static bool RunDiagnostics()
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
                    return ok;
                }
                else
                {
                    Console.WriteLine("[ERROR] Mountain Everest 60 not detected on USB.");
                    return false;
                }
            }
        }

        private static void PrintHelp()
        {
            Console.WriteLine("Everest 60 Status Indicator - Command Line Reference:");
            Console.WriteLine("  (No arguments)                      Launch silent background System Tray application (Default)");
            Console.WriteLine("  --status, -s <0-100> [hex]          Manually set status bar percentage and optional color");
            Console.WriteLine("  --battery, -b [hex]                 Show battery % on bottom perimeter (preset or custom color)");
            Console.WriteLine("  --volume, -v [hex]                  Show volume % on bottom perimeter (preset or custom color)");
            Console.WriteLine("  --cpu [hex]                         Show CPU load % on bottom perimeter (preset or custom color)");
            Console.WriteLine("  --set-background <hex>              Set key background color and turn off perimeter ring");
            Console.WriteLine("  --color, -c <hex>                   Set 15 bottom LEDs to a solid color");
            Console.WriteLine("  --clear-status                      Turn off bottom status LEDs (keys stay lit)");
            Console.WriteLine("  --off                               Turn off all keyboard LEDs completely");
            Console.WriteLine("  --console, --debug, -d              Launch with a standalone console window for live logs");
            Console.WriteLine("  --test                              Run USB communication diagnostic test");
            Console.WriteLine("  --help, -h                          Show this reference information");
            Console.WriteLine();
            Console.WriteLine("Examples for External Automation:");
            Console.WriteLine("  Everest60Rgb.exe -s 1               (Sets 1% progress bar)");
            Console.WriteLine("  Everest60Rgb.exe --status 75 \"#00FFCC\"");
            Console.WriteLine("  Everest60Rgb.exe --status 25 \"#FF2200\"");
            Console.WriteLine("  Everest60Rgb.exe -s 100 \"#00FF88\"");
            Console.WriteLine("  Everest60Rgb.exe -s 50");
        }
    }
}
