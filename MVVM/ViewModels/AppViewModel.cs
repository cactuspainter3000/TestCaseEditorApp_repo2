using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.ObjectModel;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.Services;

namespace TestCaseEditorApp.MVVM.ViewModels
{
    public class AppViewModel : ObservableObject
    {
        private readonly IServiceProvider _services;
        private readonly IPersistenceService _persistence;
        private const string SelectedStepKey = "lastSelectedStep";

        public AppViewModel(IServiceProvider services, IPersistenceService persistence)
        {
            _services = services ?? throw new ArgumentNullException(nameof(services));
            _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));

            DisplayName = "Test Case Editor";
            MenuButtonEnable = true;

            // Create the actual viewmodel instances for the three Test Case workspaces now, so we can read their counts
            var requirementsVm = _services.GetRequiredService<RequirementsViewModel>();
            var clarifyingVm = _services.GetRequiredService<ClarifyingQuestionsViewModel>();
            var testCaseVm = _services.GetRequiredService<TestCaseCreationViewModel>();

            // Populate TestCaseCreationSteps and hook badges to the underlying collection counts
            TestCaseCreationSteps = new ObservableCollection<StepDescriptor>
            {
                new StepDescriptor
                {
                    Id = "requirements",
                    DisplayName = "Requirements",
                    CreateViewModel = sp => requirementsVm,
                    Badge = requirementsVm.Requirements.Count
                },
                new StepDescriptor
                {
                    Id = "clarifying",
                    DisplayName = "Clarifying Questions",
                    CreateViewModel = sp => clarifyingVm,
                    Badge = clarifyingVm.Questions.Count
                },
                new StepDescriptor
                {
                    Id = "testcase_creation",
                    DisplayName = "Test Case Creation",
                    CreateViewModel = sp => testCaseVm,
                    Badge = testCaseVm.TestCases.Count
                },
            };

            // subscribe to collection changes for live badges
            requirementsVm.Requirements.CollectionChanged += (s, e) =>
            {
                TestCaseCreationSteps[0].Badge = requirementsVm.Requirements.Count;
            };
            clarifyingVm.Questions.CollectionChanged += (s, e) =>
            {
                TestCaseCreationSteps[1].Badge = clarifyingVm.Questions.Count;
            };
            testCaseVm.TestCases.CollectionChanged += (s, e) =>
            {
                TestCaseCreationSteps[2].Badge = testCaseVm.TestCases.Count;
            };

            // Repair group
            RepairSteps = new ObservableCollection<StepDescriptor>
            {
                new StepDescriptor { Id = "repair_notes", DisplayName = "Notes", CreateViewModel = sp => new PlaceholderViewModel("Repair Notes") },
                new StepDescriptor { Id = "module_stock", DisplayName = "Module Stock", CreateViewModel = sp => new PlaceholderViewModel("Module Stock") },
            };

            // Reports group
            ReportsSteps = new ObservableCollection<StepDescriptor>
            {
                new StepDescriptor { Id = "tl_inventory", DisplayName = "TL Inventory", CreateViewModel = sp => new PlaceholderViewModel("TL Inventory") },
                new StepDescriptor { Id = "product_location", DisplayName = "Product Location", CreateViewModel = sp => new PlaceholderViewModel("Product Location") },
            };

            // General group
            GeneralSteps = new ObservableCollection<StepDescriptor>
            {
                new StepDescriptor { Id = "ocrrh", DisplayName = "OCR | Run & Hold", CreateViewModel = sp => new PlaceholderViewModel("OCR | Run & Hold") },
            };

            // Quick Links group
            QuickLinksSteps = new ObservableCollection<StepDescriptor>
            {
                new StepDescriptor { Id = "trutest", DisplayName = "TruTest", CreateViewModel = sp => new PlaceholderViewModel("TruTest") },
                new StepDescriptor { Id = "shared", DisplayName = "Shared$", CreateViewModel = sp => new PlaceholderViewModel("Shared$") },
                new StepDescriptor { Id = "drawings", DisplayName = "Drawings", CreateViewModel = sp => new PlaceholderViewModel("Drawings") },
                new StepDescriptor { Id = "production_floor", DisplayName = "Production Floor", CreateViewModel = sp => new PlaceholderViewModel("Production Floor") },
                new StepDescriptor { Id = "mee", DisplayName = "MEE", CreateViewModel = sp => new PlaceholderViewModel("MEE") },
            };

            // restore last selected step if present
            var last = _persistence.Load<string>(SelectedStepKey);
            if (!string.IsNullOrEmpty(last))
            {
                // try find in TestCaseCreationSteps first, then other groups
                SelectedStep = FindById(last) ?? TestCaseCreationSteps[0];
            }
            else
            {
                // Select the first Test Case item by default
                if (TestCaseCreationSteps.Count > 0)
                {
                    SelectedStep = TestCaseCreationSteps[0];
                }
            }
        }

        private StepDescriptor? FindById(string id)
        {
            foreach (var s in TestCaseCreationSteps)
                if (s.Id == id) return s;
            foreach (var s in RepairSteps)
                if (s.Id == id) return s;
            foreach (var s in ReportsSteps)
                if (s.Id == id) return s;
            foreach (var s in GeneralSteps)
                if (s.Id == id) return s;
            foreach (var s in QuickLinksSteps)
                if (s.Id == id) return s;
            return null;
        }

        // Groups mapped to corresponding left-menu sections
        public ObservableCollection<StepDescriptor> TestCaseCreationSteps { get; }
        public ObservableCollection<StepDescriptor> RepairSteps { get; }
        public ObservableCollection<StepDescriptor> ReportsSteps { get; }
        public ObservableCollection<StepDescriptor> GeneralSteps { get; }
        public ObservableCollection<StepDescriptor> QuickLinksSteps { get; }

        private StepDescriptor? _selectedStep;
        public StepDescriptor? SelectedStep
        {
            get => _selectedStep;
            set
            {
                if (SetProperty(ref _selectedStep, value) && value?.CreateViewModel != null)
                {
                    CurrentStepViewModel = value.CreateViewModel(_services);
                    // persist selection
                    _persistence.Save(SelectedStepKey, value.Id);
                }
            }
        }

        private object? _currentStepViewModel;
        public object? CurrentStepViewModel
        {
            get => _currentStepViewModel;
            set => SetProperty(ref _currentStepViewModel, value);
        }

        public string DisplayName { get; set; }
        public bool MenuButtonEnable { get; set; }

        public string SapStatus { get; set; } = "SAP: Connected";
        public System.Windows.Media.Brush SapForegroundStatus { get; set; } = System.Windows.Media.Brushes.LightGreen;

        public System.Windows.DataTemplate? CommandsTemplate
            => System.Windows.Application.Current?.TryFindResource("CommandsTemplate") as System.Windows.DataTemplate;

        // NEW: WorkspaceHeaderViewModel property so MainWindow can inject a header VM that wraps the real window.
        private WorkspaceHeaderViewModel? _workspaceHeaderViewModel;
        public WorkspaceHeaderViewModel? WorkspaceHeaderViewModel
        {
            get => _workspaceHeaderViewModel;
            set => SetProperty(ref _workspaceHeaderViewModel, value);
        }
    }
}