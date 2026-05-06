using System.Windows;
using System.Windows.Controls;
using TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels;

namespace TestCaseEditorApp.MVVM.Domains.TestCaseGenerator_Mode.Views
{
    public partial class TestCaseGenerator_AnalysisControl : UserControl
    {
        public TestCaseGenerator_AnalysisControl()
        {
            InitializeComponent();
        }

        private void InspectResult_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is TestCaseGenerator_AnalysisVM analysisVm)
            {
                var result = analysisVm.InspectLastAnalysisResult();
                MessageBox.Show(result, "Analysis Result Inspection", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }
}
