namespace TestCaseEditorApp.MVVM.ViewModels
{
    using System;
    using System.Collections.ObjectModel;
    using System.Windows.Input;
    using System.Windows.Media;
    using TestCaseEditorApp.MVVM.Models;

    /// <summary>
    /// Design-time stub that provides the properties MainWindow binds to.
    /// Reduced to only include the main step list placeholders requested by design.
    /// </summary>
    public class MainViewModelDesignStub
    {
        private static readonly ICommand _noop = new RoutedCommand();

        public MainViewModelDesignStub()
        {
            DisplayName = "Test Case Editor (Design)";
            HotReloadTooltip = "Hot reload information (design)";
            IsHotReloadAvailable = false;

            // Only the two placeholders requested:
            TestCaseCreationSteps = new ObservableCollection<StepDescriptor>
            {
                new StepDescriptor { DisplayName = "Test Case Generator", Badge = "" },
                new StepDescriptor { DisplayName = "Test Flow Generator", Badge = "" }
            };

            SelectedStep = TestCaseCreationSteps.Count > 0 ? TestCaseCreationSteps[0] : null;

            SapStatus = "Disconnected (design)";
            SapForegroundStatus = new SolidColorBrush(Colors.Gray);

            // Provide a small placeholder object for the ContentControl's CurrentStepViewModel
            CurrentStepViewModel = new object();

            // Ensure Navigation is available for designer bindings (Navigation.RequirementsNav etc.)
            Navigation = new NavigationViewModel();

            // Try to create a design-time RequirementsNav instance without requiring a parameterless ctor.
            // Use Activator.CreateInstance to avoid compile-time constructor matching errors.
            try
            {
                // Pass a single null argument (will be mapped to the IRequirementsNavigator parameter if present).
                var created = Activator.CreateInstance(typeof(RequirementsNavigationViewModel), new object[] { null }) as RequirementsNavigationViewModel;
                Navigation.RequirementsNav = created;
            }
            catch
            {
                // ignore if RequirementsNavigationViewModel requires non-null services or throws in ctor
            }
        }

        // Bound from Window.Title
        public string DisplayName { get; }

        // Expose Navigation so bindings like Navigation.RequirementsNav resolve at design time
        public NavigationViewModel Navigation { get; }

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

        // Reduced step list used by the left menu (only the two placeholders)
        public ObservableCollection<StepDescriptor> TestCaseCreationSteps { get; }

        public StepDescriptor? SelectedStep { get; set; }

        // Status area
        public string SapStatus { get; }
        public Brush SapForegroundStatus { get; }

        // ContentPresenter binding
        public object CurrentStepViewModel { get; }
    }
}