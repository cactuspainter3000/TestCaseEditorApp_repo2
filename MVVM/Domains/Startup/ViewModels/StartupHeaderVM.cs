using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Windows.Input;
using System.Windows.Media;

namespace TestCaseEditorApp.MVVM.Domains.Startup.ViewModels
{
    /// <summary>
    /// Header workspace content for application startup state
    /// </summary>
    public partial class StartupHeaderVM : ObservableObject
    {
        [ObservableProperty]
        private string text = "Application Loading...";
        
        // Properties that MainWindow window controls expect
        public ICommand? UndoLastSaveCommand => null; // Not applicable during startup
        public bool CanUndoLastSave => false; // Not applicable during startup
        
        // Property that HeaderDescriptionBorderStyle might expect
        public Brush? RequirementDescriptionHighlight => null; // Not applicable during startup
    }
}