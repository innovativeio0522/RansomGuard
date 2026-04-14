using System.Windows.Controls;
namespace RansomGuard.Views { public partial class QuarantineView : UserControl { public QuarantineView() { InitializeComponent(); }
        private void DismissBanner_Click(object sender, System.Windows.RoutedEventArgs e) 
        { 
            if (DataContext is RansomGuard.ViewModels.QuarantineViewModel vm) vm.IsScanSummaryVisible = false; 
        }
    } 
}
