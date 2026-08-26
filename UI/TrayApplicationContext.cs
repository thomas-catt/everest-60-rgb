using System;
using System.Drawing;
using System.Windows.Forms;
using Everest60Rgb.Animations;
using Everest60Rgb.Core;
using Everest60Rgb.Indicators;
using Everest60Rgb.Protocol;

namespace Everest60Rgb.UI
{
    /// <summary>
    /// System tray application context for controlling the Mountain Everest 60 status indicators.
    /// Provides live mode selection, brightness adjustment, key background configuration,
    /// and custom/preset status bar color selection.
    /// </summary>
    public class TrayApplicationContext : ApplicationContext
    {
        private readonly NotifyIcon _notifyIcon;
        private readonly Everest60Controller _controller;
        private readonly AnimationEngine _engine;
        private readonly Timer _statusTimer;

        private ToolStripMenuItem _statusItem;
        private ToolStripMenuItem _batteryItem;
        private ToolStripMenuItem _volumeItem;
        private ToolStripMenuItem _cpuItem;
        private ToolStripMenuItem _offItem;

        private ToolStripMenuItem _dynamicPresetsItem;

        public TrayApplicationContext(Everest60Controller controller, AnimationEngine engine)
        {
            _controller = controller ?? throw new ArgumentNullException(nameof(controller));
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));

            Console.WriteLine("[INFO] Initializing System Tray interface...");

            var contextMenu = new ContextMenuStrip();

            _statusItem = new ToolStripMenuItem("Everest 60: Detecting...") { Enabled = false };
            contextMenu.Items.Add(_statusItem);
            contextMenu.Items.Add(new ToolStripSeparator());

            // 1. Status Indicator Mode Menu (Battery, Volume, CPU, Off)
            var statusMenu = new ToolStripMenuItem("📊 Status Indicator");
            _batteryItem = new ToolStripMenuItem("🔋 Battery Percentage", null, (s, e) => SelectStatusSource(StatusSource.Battery));
            _volumeItem  = new ToolStripMenuItem("🔊 Master Volume", null, (s, e) => SelectStatusSource(StatusSource.Volume));
            _cpuItem     = new ToolStripMenuItem("⚡ CPU Load", null, (s, e) => SelectStatusSource(StatusSource.Cpu));
            _offItem     = new ToolStripMenuItem("⏹️ Turn Off Bottom Status", null, (s, e) => SelectStatusSource(StatusSource.Off));

            statusMenu.DropDownItems.AddRange(new ToolStripItem[] {
                _batteryItem, _volumeItem, _cpuItem, new ToolStripSeparator(), _offItem
            });
            contextMenu.Items.Add(statusMenu);

            // 2. Status Bar Color Menu (Dynamic Presets vs Custom Color)
            var statusColorMenu = new ToolStripMenuItem("💡 Status Bar Color");
            _dynamicPresetsItem = new ToolStripMenuItem("🌈 Dynamic Presets (Default)", null, (s, e) => SetStatusBarColor(null)) { Checked = true };
            statusColorMenu.DropDownItems.Add(_dynamicPresetsItem);

            var pickStatusColorItem = new ToolStripMenuItem("🎨 Pick Custom Color...", null, (s, e) => ShowStatusBarColorPicker());
            statusColorMenu.DropDownItems.Add(pickStatusColorItem);
            statusColorMenu.DropDownItems.Add(new ToolStripSeparator());

            // Status bar color presets
            statusColorMenu.DropDownItems.Add(new ToolStripMenuItem("💎 Cyan (#00FFFF)", null, (s, e) => SetStatusBarColor(new RgbColor(0, 255, 255))));
            statusColorMenu.DropDownItems.Add(new ToolStripMenuItem("🔵 Ice Blue (#0099FF)", null, (s, e) => SetStatusBarColor(new RgbColor(0, 153, 255))));
            statusColorMenu.DropDownItems.Add(new ToolStripMenuItem("🟢 Neon Green (#00FF66)", null, (s, e) => SetStatusBarColor(new RgbColor(0, 255, 102))));
            statusColorMenu.DropDownItems.Add(new ToolStripMenuItem("🟣 Violet (#AA00FF)", null, (s, e) => SetStatusBarColor(new RgbColor(170, 0, 255))));
            statusColorMenu.DropDownItems.Add(new ToolStripMenuItem("🟡 Amber Yellow (#FFCC00)", null, (s, e) => SetStatusBarColor(new RgbColor(255, 204, 0))));
            statusColorMenu.DropDownItems.Add(new ToolStripMenuItem("🔴 Coral Red (#FF3344)", null, (s, e) => SetStatusBarColor(new RgbColor(255, 51, 68))));
            statusColorMenu.DropDownItems.Add(new ToolStripMenuItem("⚪ Crisp White (#FFFFFF)", null, (s, e) => SetStatusBarColor(RgbColor.White)));

            contextMenu.Items.Add(statusColorMenu);

            // 3. Custom Key Background Color Menu
            var bgMenu = new ToolStripMenuItem("🎨 Set Key Background Color");
            var pickBgColorItem = new ToolStripMenuItem("🎨 Pick Custom Color...", null, (s, e) => ShowKeyBackgroundColorPicker());
            bgMenu.DropDownItems.Add(pickBgColorItem);
            bgMenu.DropDownItems.Add(new ToolStripSeparator());

            bgMenu.DropDownItems.Add(new ToolStripMenuItem("⚪ Crisp White (#FFFFFF)", null, (s, e) => ApplyKeyBackgroundColor(RgbColor.White)));
            bgMenu.DropDownItems.Add(new ToolStripMenuItem("🔵 Ice Blue (#00CCFF)", null, (s, e) => ApplyKeyBackgroundColor(new RgbColor(0, 204, 255))));
            bgMenu.DropDownItems.Add(new ToolStripMenuItem("🟢 Mint Green (#00FF88)", null, (s, e) => ApplyKeyBackgroundColor(new RgbColor(0, 255, 136))));
            bgMenu.DropDownItems.Add(new ToolStripMenuItem("🟣 Royal Purple (#9900FF)", null, (s, e) => ApplyKeyBackgroundColor(new RgbColor(153, 0, 255))));
            bgMenu.DropDownItems.Add(new ToolStripMenuItem("🔴 Crimson Red (#FF2233)", null, (s, e) => ApplyKeyBackgroundColor(new RgbColor(255, 34, 51))));
            bgMenu.DropDownItems.Add(new ToolStripMenuItem("🟠 Amber Orange (#FF8800)", null, (s, e) => ApplyKeyBackgroundColor(new RgbColor(255, 136, 0))));
            bgMenu.DropDownItems.Add(new ToolStripMenuItem("⚫ All Keys Dark (Off)", null, (s, e) => ApplyKeyBackgroundColor(RgbColor.Black)));

            contextMenu.Items.Add(bgMenu);

            // 4. Brightness Submenu
            var briMenu = new ToolStripMenuItem("🔆 Brightness");
            briMenu.DropDownItems.Add(new ToolStripMenuItem("100%", null, (s, e) => SetBrightness(100)));
            briMenu.DropDownItems.Add(new ToolStripMenuItem("75%", null, (s, e) => SetBrightness(75)));
            briMenu.DropDownItems.Add(new ToolStripMenuItem("50%", null, (s, e) => SetBrightness(50)));
            briMenu.DropDownItems.Add(new ToolStripMenuItem("25%", null, (s, e) => SetBrightness(25)));
            contextMenu.Items.Add(briMenu);

            // 5. Reconnect action
            var reconnectItem = new ToolStripMenuItem("🔄 Reconnect Device", null, (s, e) =>
            {
                Console.WriteLine("[INFO] Manual reconnect requested from tray.");
                _controller.Disconnect();
                _controller.Connect();
                UpdateConnectionStatusText();
            });
            contextMenu.Items.Add(reconnectItem);

            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add(new ToolStripMenuItem("❌ Exit", null, (s, e) => ExitApplication()));

            _notifyIcon = new NotifyIcon
            {
                Icon = SystemIcons.Application,
                ContextMenuStrip = contextMenu,
                Text = "Everest 60 - Status Indicator",
                Visible = true
            };

            // Periodic connection health-check timer (every 5s)
            _statusTimer = new Timer { Interval = 5000 };
            _statusTimer.Tick += (s, e) =>
            {
                if (!_controller.IsConnected)
                {
                    _controller.Connect();
                }
                UpdateConnectionStatusText();
            };
            _statusTimer.Start();

            // Start animation & status engine
            _engine.Start();
            SelectStatusSource(StatusSource.Battery);

            Console.WriteLine("[INFO] System Tray application running in background.");
        }

        private void UpdateConnectionStatusText()
        {
            if (_controller.IsConnected)
            {
                _statusItem.Text = $"Connected (PID: 0x{_controller.ProductId:X4})";
            }
            else
            {
                _statusItem.Text = "Keyboard Disconnected";
            }
        }

        private void SelectStatusSource(StatusSource source)
        {
            Console.WriteLine($"[INFO] Switching status source to: {source}");
            _engine.SetSource(source);

            // Update radio checks
            _batteryItem.Checked = (source == StatusSource.Battery);
            _volumeItem.Checked  = (source == StatusSource.Volume);
            _cpuItem.Checked     = (source == StatusSource.Cpu);
            _offItem.Checked     = (source == StatusSource.Off);

            _notifyIcon.ShowBalloonTip(1000, "Everest 60 Status", $"Active status: {source}", ToolTipIcon.Info);
        }

        private void SetStatusBarColor(RgbColor? customColor)
        {
            _engine.CustomStatusColor = customColor;
            _dynamicPresetsItem.Checked = !customColor.HasValue;

            if (customColor.HasValue)
            {
                Console.WriteLine($"[INFO] Status bar color set to: {customColor.Value}");
                _notifyIcon.ShowBalloonTip(1000, "Everest 60", $"Status LEDs set to {customColor.Value.ToHex()}", ToolTipIcon.Info);
            }
            else
            {
                Console.WriteLine("[INFO] Status bar color reset to Dynamic Presets.");
                _notifyIcon.ShowBalloonTip(1000, "Everest 60", "Status LEDs using Dynamic Presets", ToolTipIcon.Info);
            }
        }

        private void ShowStatusBarColorPicker()
        {
            using (var dlg = new ColorDialog())
            {
                dlg.FullOpen = true;
                dlg.Color = Color.Cyan;

                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    var rgb = new RgbColor(dlg.Color.R, dlg.Color.G, dlg.Color.B);
                    SetStatusBarColor(rgb);
                }
            }
        }

        private void ShowKeyBackgroundColorPicker()
        {
            using (var dlg = new ColorDialog())
            {
                dlg.FullOpen = true;
                dlg.Color = Color.White;

                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    var rgb = new RgbColor(dlg.Color.R, dlg.Color.G, dlg.Color.B);
                    ApplyKeyBackgroundColor(rgb);
                }
            }
        }

        private void ApplyKeyBackgroundColor(RgbColor keyColor)
        {
            Console.WriteLine($"[INFO] Setting key background color to {keyColor} and turning off perimeter LEDs...");
            
            // 1. Set all keys to the chosen color, turn OFF all 44 perimeter LEDs, and save to disk
            RgbProfileManager.SetBackground(_controller.Framebuffer, keyColor, saveToDisk: true);

            // 2. Trigger a full sync so keys immediately light up and perimeter is dark before status loop continues
            _engine.SetSource(_engine.CurrentSource);

            _notifyIcon.ShowBalloonTip(1200, "Everest 60", $"Key background set to {keyColor.ToHex()} (Perimeter OFF)", ToolTipIcon.Info);
        }

        private void SetBrightness(int brightness)
        {
            Console.WriteLine($"[INFO] Setting brightness to: {brightness}%");
            _engine.BrightnessPercent = brightness;
            _notifyIcon.ShowBalloonTip(1000, "Everest 60", $"Brightness: {brightness}%", ToolTipIcon.Info);
        }

        private void ExitApplication()
        {
            Console.WriteLine("[INFO] Exiting application (clearing bottom status LEDs only)...");
            _statusTimer.Stop();
            _notifyIcon.Visible = false;
            _engine.Stop();

            // Clear ONLY the 15 bottom LEDs, leaving all keys illuminated with their background color
            _controller.ClearBottomBorderOnly();
            _controller.Disconnect();
            Application.Exit();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _statusTimer?.Dispose();
                _notifyIcon?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
