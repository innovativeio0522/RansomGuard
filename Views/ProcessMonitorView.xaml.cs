using System.Windows;
using System.Windows.Controls;
using System.IO;

namespace RansomGuard.Views
{
    public partial class ProcessMonitorView : UserControl
    {
        public ProcessMonitorView()
        {
            InitializeComponent();
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            var vm = DataContext;
            var msg = $"[ProcessMonitorView Loaded] DataContext type: {vm?.GetType().Name ?? "NULL"}";
            System.Diagnostics.Debug.WriteLine(msg);
            
            if (vm is RansomGuard.ViewModels.ProcessMonitorViewModel processVM)
            {
                var countMsg = $"[ProcessMonitorView Loaded] ActiveProcesses.Count: {processVM.ActiveProcesses.Count}";
                System.Diagnostics.Debug.WriteLine(countMsg);
            }
        }

        private void ListBoxItem_RequestBringIntoView(object sender, RequestBringIntoViewEventArgs e)
        {
            // Suppress the auto-scroll jump when a ListBox item is selected via keyboard/mouse focus
            e.Handled = true;
        }
    }
}
