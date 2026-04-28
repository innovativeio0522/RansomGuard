using System;
using System.Windows;
using System.Windows.Threading;
using RansomGuard.Core.Models;
using System.Windows.Input;

namespace RansomGuard.Views
{
    public partial class ShieldAlertWindow : Window
    {
        public bool Result { get; private set; }
        private readonly DispatcherTimer _timer;
        private int _secondsRemaining = 5;
        private const int TickFrequencyMs = 100;
        private int _totalTicks = 50; // 5 seconds at 100ms interval

        public ShieldAlertWindow(Threat threat)
        {
            InitializeComponent();
            
            ProcessText.Text = $"{threat.ProcessName} (PID: {threat.ProcessId})";
            FilesText.Text = $"{threat.AffectedFiles.Count} Files";

            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(TickFrequencyMs)
            };
            _timer.Tick += Timer_Tick;
            _timer.Start();

            // Allow dragging the window
            this.MouseDown += (s, e) => {
                if (e.ChangedButton == MouseButton.Left) this.DragMove();
            };
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            _totalTicks--;
            
            if (_totalTicks % 10 == 0)
            {
                _secondsRemaining = _totalTicks / 10;
                TimerText.Text = $"AUTOMATIC MITIGATION IN {_secondsRemaining} SECONDS";
            }

            CountdownProgress.Value = _totalTicks * 10;

            if (_totalTicks <= 0)
            {
                _timer.Stop();
                Result = true; // Auto-mitigate on timeout
                this.DialogResult = true;
                this.Close();
            }
        }

        private void YesButton_Click(object sender, RoutedEventArgs e)
        {
            _timer.Stop();
            Result = true;
            this.DialogResult = true;
            this.Close();
        }

        private void NoButton_Click(object sender, RoutedEventArgs e)
        {
            _timer.Stop();
            Result = false;
            this.DialogResult = false;
            this.Close();
        }
    }
}
