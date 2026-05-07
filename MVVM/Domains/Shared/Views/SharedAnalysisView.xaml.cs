using System.Windows.Controls;

namespace TestCaseEditorApp.MVVM.Domains.Shared.Views
{
    /// <summary>
    /// Shared analysis view that consolidates requirement analysis UI across domains.
    /// Replaces duplicate TestCaseGenerator_AnalysisControl and Requirements_AnalysisControl.
    /// </summary>
    public partial class SharedAnalysisView : UserControl
    {
        public SharedAnalysisView()
        {
            InitializeComponent();
        }
    }
}