using System;
using System.Drawing;
using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Forms;

namespace RansomGuard.Services
{
    /// <summary>
    /// Manages the system tray icon, context menu, and balloon notifications.
    /// Owns the NotifyIcon lifetime — call Dispose() when the app exits.
    /// </summary>
    public class TrayIconService : IDisposable
    {
        private readonly NotifyIcon _notifyIcon;
        private bool _disposed;

        public TrayIconService()
        {
            _notifyIcon = new NotifyIcon
            {
                Text    = "RansomGuard — Active Protection",
                Visible = true,
                Icon    = LoadIcon()
            };

            _notifyIcon.ContextMenuStrip = BuildContextMenu();

            // Double-click tray icon → restore window
            _notifyIcon.DoubleClick += (_, _) => ShowMainWindow();
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>Shows a balloon notification from the tray icon.</summary>
        public void ShowBalloon(string title, string message,
            ToolTipIcon icon = ToolTipIcon.Info, int durationMs = 3000)
        {
            _notifyIcon.ShowBalloonTip(durationMs, title, message, icon);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private ContextMenuStrip BuildContextMenu()
        {
            var menu = new ContextMenuStrip();

            var openItem = new ToolStripMenuItem("Open RansomGuard");
            openItem.Font = new System.Drawing.Font(openItem.Font, System.Drawing.FontStyle.Bold);
            openItem.Click += (_, _) => ShowMainWindow();

            var exitItem = new ToolStripMenuItem("Exit");
            exitItem.Click += (_, _) =>
            {
                _notifyIcon.Visible = false;
                System.Windows.Application.Current.Shutdown();
            };

            menu.Items.Add(openItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(exitItem);

            return menu;
        }

        private static void ShowMainWindow()
        {
            var main = System.Windows.Application.Current.MainWindow;
            if (main == null) return;

            main.Show();
            main.WindowState = WindowState.Normal;
            main.Activate();
        }

        private static Icon LoadIcon()
        {
            try
            {
                // Try to find the PNG asset shipped with the app
                var pngPath = Path.Combine(
                    AppContext.BaseDirectory,
                    "Assets", "Icons", "RansomGuard.png");

                if (File.Exists(pngPath))
                {
                    using var bmp    = new System.Drawing.Bitmap(pngPath);
                    using var resized = new System.Drawing.Bitmap(bmp, new System.Drawing.Size(16, 16));
                    var iconHandle   = resized.GetHicon();
                    return System.Drawing.Icon.FromHandle(iconHandle);
                }
            }
            catch { /* fall through to system default */ }

            // Fallback: use the executable's embedded icon
            return SystemIcons.Shield;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
        }
    }
}
