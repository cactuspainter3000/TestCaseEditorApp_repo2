namespace TestCaseEditorApp.MVVM.ViewModels
{
    using System.Collections.ObjectModel;
    using System.Windows.Input;
    using System.Windows.Media;
    using TestCaseEditorApp.MVVM.Models;

    /// <summary>
    /// Single design-time stub that provides the properties MainWindow binds to.
    /// Add any additional properties here if designer output still reports missing bindings.
    /// </summary>
    public class MainViewModelDesignStub
    {
        private static readonly ICommand _noop = new RoutedCommand();

        public MainViewModelDesignStub()
        {
            DisplayName = "Test Case Editor (Design)";
            HotReloadTooltip = "Hot reload information (design)";
            IsHotReloadAvailable = false;

            TestCaseCreationSteps = new ObservableCollection<StepDescriptor>
            {
                new StepDescriptor { DisplayName = "Requirements", Badge = "" },
                new StepDescriptor { DisplayName = "Clarifying Questions", Badge = "" },
                new StepDescriptor { DisplayName = "Test Case Creation", Badge = "" }
            };

            RepairSteps = new ObservableCollection<StepDescriptor>
            {
                new StepDescriptor { DisplayName = "Repair A", Badge = "" },
                new StepDescriptor { DisplayName = "Repair B", Badge = "" }
            };

            ReportsSteps = new ObservableCollection<StepDescriptor>
            {
                new StepDescriptor { DisplayName = "Report 1", Badge = "" }
            };

            GeneralSteps = new ObservableCollection<StepDescriptor>
            {
                new StepDescriptor { DisplayName = "Settings", Badge = "" }
            };

            QuickLinksSteps = new ObservableCollection<StepDescriptor>
            {
                new StepDescriptor { DisplayName = "Help", Badge = "" }
            };

            SelectedStep = TestCaseCreationSteps.Count > 0 ? TestCaseCreationSteps[0] : null;
            SapStatus = "Disconnected (design)";
            SapForegroundStatus = new SolidColorBrush(Colors.Gray);

            // Provide a small placeholder object for the ContentControl's CurrentStepViewModel
            CurrentStepViewModel = new object();
        }

        // Bound from Window.Title
        public string DisplayName { get; }

        // HotReload area
        public string HotReloadTooltip { get; }
        public ICommand HotReloadHelpCommand => _noop;
        public bool IsHotReloadAvailable { get; }
        public ICommand NextExpandStateCommand => _noop;

        // File/menu commands (Popup)
        public ICommand ImportWordCommand => _noop;
        public ICommand LoadWorkspaceCommand => _noop;
        public ICommand SaveWorkspaceCommand => _noop;
        public ICommand ReloadCommand => _noop;
        public ICommand ExportAllToJamaCommand => _noop;
        public ICommand HelpCommand => _noop;

        // Step lists used by the left menu
        public ObservableCollection<StepDescriptor> TestCaseCreationSteps { get; }
        public ObservableCollection<StepDescriptor> RepairSteps { get; }
        public ObservableCollection<StepDescriptor> ReportsSteps { get; }
        public ObservableCollection<StepDescriptor> GeneralSteps { get; }
        public ObservableCollection<StepDescriptor> QuickLinksSteps { get; }

        public StepDescriptor? SelectedStep { get; set; }

        // Status area
        public string SapStatus { get; }
        public Brush SapForegroundStatus { get; }

        // ContentPresenter binding
        public object CurrentStepViewModel { get; }
    }
}