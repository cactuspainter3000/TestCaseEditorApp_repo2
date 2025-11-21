using System.Windows;

namespace TestCaseEditorApp.MVVM.Views
{
    public partial class RequirementDescriptionEditorWindow : Window
    {
        public bool ShouldReAnalyze { get; private set; }

        public RequirementDescriptionEditorWindow()
        {
            InitializeComponent();
            ShouldReAnalyze = false;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            ShouldReAnalyze = false;
            Close();
        }

        private async void ReAnalyze_Click(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("[EditorWindow] Re-Analyze clicked");
            
            // Get ViewModel and execute ReAnalyze command
            if (DataContext is TestCaseEditorApp.MVVM.ViewModels.TestCaseGenerator_AnalysisVM vm && vm.ReAnalyzeCommand != null)
            {
                System.Diagnostics.Debug.WriteLine("[EditorWindow] Invoking ReAnalyzeCommand");
                await vm.ReAnalyzeCommand.ExecuteAsync(null);
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("[EditorWindow] ERROR: Could not find ReAnalyzeCommand");
            }
        }
    }
}
