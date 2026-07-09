using System.Windows.Controls;
using System.Windows.Threading;

namespace TestCaseEditorApp.MVVM.Domains.Requirements.Views
{
    /// <summary>
    /// Requirements Tab Selector View - displays tabs for Main, Cleanup, and Attachments workspaces
    /// </summary>
    public partial class RequirementsTabSelectorView : UserControl
    {
        public RequirementsTabSelectorView()
        {
            InitializeComponent();
            Loaded += RequirementsTabSelectorView_Loaded;
        }

        private void RequirementsTabSelectorView_Loaded(object sender, System.Windows.RoutedEventArgs e)
        {
            // Load the default Main tab asynchronously after the view has fully initialized
            // This prevents UI freeze by deferring heavy ViewModel creation
            Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
            {
                var viewModel = DataContext as MVVM.Domains.Requirements.ViewModels.RequirementsTabSelectorViewModel;
                if (viewModel != null && viewModel.CurrentContentViewModel == null)
                {
                    // Trigger Main tab selection, which will create the VM on-demand
                    viewModel.SelectMainTabCommand.Execute(null);
                }
            });
        }
    }
}
