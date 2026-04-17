using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using RansomGuard.ViewModels;

namespace RansomGuard
{
    public partial class MainWindow : Window
    {
        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo lpmi);

        [StructLayout(LayoutKind.Sequential)]
        private struct Rect { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MonitorInfo
        {
            public int cbSize;
            public Rect rcMonitor;
            public Rect rcWork; // Work area (excludes taskbar)
            public uint dwFlags;
        }

        private const uint MONITOR_DEFAULTTONEAREST = 2;
        private IntPtr _lastMonitor = IntPtr.Zero;

        public MainWindow()
        {
            InitializeComponent();
            DataContext = new MainViewModel();
            Loaded += (s, e) => ApplyScreenPercentageSize();
            LocationChanged += OnLocationChanged;
        }

        private void OnLocationChanged(object? sender, EventArgs e)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);

            // Only resize if moved to a different monitor
            if (monitor != _lastMonitor)
            {
                _lastMonitor = monitor;
                ApplyScreenPercentageSize();
            }
        }

        private void ApplyScreenPercentageSize()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);

            var info = new MonitorInfo { cbSize = Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfo(monitor, ref info)) return;

            // Get DPI scale for this monitor
            var src = PresentationSource.FromVisual(this);
            double dpiX = src?.CompositionTarget?.TransformFromDevice.M11 ?? 1.0;
            double dpiY = src?.CompositionTarget?.TransformFromDevice.M22 ?? 1.0;

            // Convert pixel work area to WPF device-independent units
            double workW = (info.rcWork.Right  - info.rcWork.Left) * dpiX;
            double workH = (info.rcWork.Bottom - info.rcWork.Top)  * dpiY;
            double workX =  info.rcWork.Left * dpiX;
            double workY =  info.rcWork.Top  * dpiY;

            Width  = workW * 0.63;
            Height = workH * 0.72;
            Left   = workX + (workW - Width)  / 2;
            Top    = workY + (workH - Height) / 2;
        }

        private void MinButton_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void MaxButton_Click(object sender, RoutedEventArgs e)
        {
            if (this.WindowState == WindowState.Maximized)
                this.WindowState = WindowState.Normal;
            else
                this.WindowState = WindowState.Maximized;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
