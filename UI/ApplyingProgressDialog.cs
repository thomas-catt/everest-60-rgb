using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Everest60Rgb.UI
{
    /// <summary>
    /// A sleek, modern progress dialog displayed while waiting for the Everest 60 hardware
    /// to process and acknowledge RGB changes (matching the Base Camp hardware loader behavior).
    /// </summary>
    public class ApplyingProgressDialog : Form
    {
        private readonly ProgressBar _progressBar;
        private readonly Label _statusLabel;

        public ApplyingProgressDialog(string message = "Applying lighting configuration to Everest 60...")
        {
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(380, 140);
            Text = "Mountain Everest 60 - Syncing";
            BackColor = Color.FromArgb(32, 33, 36);
            ForeColor = Color.White;
            Font = new Font("Segoe UI", 9F, FontStyle.Regular);

            var iconLabel = new Label
            {
                Text = "⌨️",
                Font = new Font("Segoe UI Emoji", 20F),
                Location = new Point(16, 20),
                Size = new Size(40, 45),
                ForeColor = Color.White
            };
            Controls.Add(iconLabel);

            _statusLabel = new Label
            {
                Text = message,
                Location = new Point(65, 22),
                Size = new Size(290, 36),
                ForeColor = Color.FromArgb(230, 230, 230),
                Font = new Font("Segoe UI", 9.5F, FontStyle.Regular)
            };
            Controls.Add(_statusLabel);

            _progressBar = new ProgressBar
            {
                Style = ProgressBarStyle.Marquee,
                MarqueeAnimationSpeed = 25,
                Location = new Point(20, 68),
                Size = new Size(325, 14)
            };
            Controls.Add(_progressBar);
        }

        /// <summary>
        /// Runs an asynchronous operation while displaying the waiting dialog,
        /// then automatically closes the dialog once the keyboard hardware finishes.
        /// </summary>
        public static async Task ExecuteWithProgressAsync(Func<Task<bool>> action, string message = "Applying lighting to Everest 60...")
        {
            var dialog = new ApplyingProgressDialog(message);
            dialog.Show();
            dialog.Refresh();

            try
            {
                await Task.Run(action);
            }
            finally
            {
                if (!dialog.IsDisposed)
                {
                    dialog.Close();
                    dialog.Dispose();
                }
            }
        }

        /// <summary>
        /// Runs a synchronous action in the background while displaying the waiting dialog.
        /// </summary>
        public static bool ExecuteWithProgress(Func<bool> action, string message = "Applying lighting to Everest 60...")
        {
            var dialog = new ApplyingProgressDialog(message);
            bool result = false;

            dialog.Shown += async (s, e) =>
            {
                result = await Task.Run(action);
                dialog.Close();
            };

            dialog.ShowDialog();
            return result;
        }
    }
}
