using System.Windows.Controls;
using TestCaseEditorApp.MVVM.Domains.Requirements.ViewModels;

namespace TestCaseEditorApp.MVVM.Domains.Requirements.Views
{
    /// <summary>
    /// Interaction logic for UnifiedRequirementsMainView.xaml
    /// Unified Requirements view supporting all data sources with adaptive UI.
    /// </summary>
    public partial class UnifiedRequirementsMainView : UserControl
    {
        public UnifiedRequirementsMainView()
        {
            InitializeComponent();
        }

        public UnifiedRequirementsMainView(UnifiedRequirementsMainViewModel viewModel) : this()
        {
            DataContext = viewModel;
        }
    }
}