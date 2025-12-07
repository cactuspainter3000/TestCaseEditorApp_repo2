using System.Windows;
using System.Windows.Controls;
using TestCaseEditorApp.MVVM.ViewModels;

namespace TestCaseEditorApp.MVVM.Views
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
