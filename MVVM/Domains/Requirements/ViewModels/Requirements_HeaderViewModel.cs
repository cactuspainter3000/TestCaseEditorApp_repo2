using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Mediators;
using TestCaseEditorApp.MVVM.Events;
using TestCaseEditorApp.MVVM.ViewModels;

namespace TestCaseEditorApp.MVVM.Domains.Requirements.ViewModels
{
    /// <summary>
    /// Header ViewModel for Requirements domain.
    /// Provides contextual header information and quick stats.
    /// Following architectural guide patterns for header ViewModels.
    /// </summary>
    public partial class Requirements_HeaderViewModel : BaseDomainViewModel
    {
        private new readonly ITestCaseGenerationMediator _mediator;

        [ObservableProperty]
        private int totalRequirements;

        [ObservableProperty]
        private int analyzedRequirements;

        [ObservableProperty]
        private int pendingRequirements;

        [ObservableProperty]
        private string importSource = "None";

        [ObservableProperty]
        private bool hasRequirements;

        [ObservableProperty]
        private string analysisProgress = "0%";

        [ObservableProperty]
        private string requirementDescription = "Requirements management workspace";

        // Workspace management properties
        [ObservableProperty]
        private string? workspaceFilePath;

        [ObservableProperty]
        private System.DateTime? lastSaveTimestamp;

        [ObservableProperty]
        private bool isDirty;

        [ObservableProperty]
        private bool canUndoLastSave;

        public ICommand SaveWorkspaceCommand { get; }
        public ICommand UndoLastSaveCommand { get; }

        public Requirements_HeaderViewModel(
            ITestCaseGenerationMediator mediator,
            ILogger<Requirements_HeaderViewModel> logger)
            : base(mediator, logger)
        {
            _mediator = mediator ?? throw new System.ArgumentNullException(nameof(mediator));

            // Initialize workspace commands
            SaveWorkspaceCommand = new RelayCommand(SaveWorkspace, () => IsDirty);
            UndoLastSaveCommand = new RelayCommand(UndoLastSave, () => CanUndoLastSave);

            // Subscribe to TestCaseGeneration events for real-time updates
            _mediator.Subscribe<TestCaseGenerationEvents.RequirementsImported>(OnRequirementsImported);
            _mediator.Subscribe<TestCaseGenerationEvents.RequirementsCollectionChanged>(OnRequirementsCollectionChanged);
            _mediator.Subscribe<TestCaseGenerationEvents.RequirementAnalyzed>(OnRequirementAnalyzed);
            _mediator.Subscribe<TestCaseGenerationEvents.RequirementSelected>(OnRequirementSelected);

            // Initialize values
            UpdateStatistics();

            _logger.LogDebug("Requirements_HeaderViewModel initialized");
        }

        private void OnRequirementsImported(TestCaseGenerationEvents.RequirementsImported e)
        {
            ImportSource = System.IO.Path.GetFileName(e.SourceFile) ?? "Unknown";
            UpdateStatistics();
        }

        private void OnRequirementsCollectionChanged(TestCaseGenerationEvents.RequirementsCollectionChanged e)
        {
            UpdateStatistics();
        }

        private void OnRequirementAnalyzed(TestCaseGenerationEvents.RequirementAnalyzed e)
        {
            UpdateStatistics();
        }

        private void OnRequirementSelected(TestCaseGenerationEvents.RequirementSelected e)
        {
            // Update header description based on selected requirement
            if (e.Requirement != null)
            {
                RequirementDescription = $"{e.Requirement.Description?.Substring(0, Math.Min(e.Requirement.Description?.Length ?? 0, 100))}{(e.Requirement.Description?.Length > 100 ? "..." : "")}";
            }
            else
            {
                RequirementDescription = "Requirements management workspace";
            }
        }

        private void UpdateStatistics()
        {
            TotalRequirements = _mediator.Requirements.Count;
            AnalyzedRequirements = _mediator.Requirements.Count(r => r.Analysis != null);
            PendingRequirements = TotalRequirements - AnalyzedRequirements;
            HasRequirements = TotalRequirements > 0;

            // Calculate analysis progress percentage
            if (TotalRequirements > 0)
            {
                var percentage = (double)AnalyzedRequirements / TotalRequirements * 100;
                AnalysisProgress = $"{percentage:F0}%";
            }
            else
            {
                AnalysisProgress = "0%";
            }

            _logger.LogDebug("Requirements statistics updated: {Total} total, {Analyzed} analyzed",
                TotalRequirements, AnalyzedRequirements);
        }

        // Computed properties for display
        public string RequirementsStatus =>
            TotalRequirements switch
            {
                0 => "No requirements loaded",
                1 => "1 requirement loaded",
                _ => $"{TotalRequirements} requirements loaded"
            };

        public string AnalysisStatus =>
            AnalyzedRequirements switch
            {
                0 when TotalRequirements > 0 => "Analysis not started",
                var count when count == TotalRequirements && TotalRequirements > 0 => "Analysis complete",
                var count when count > 0 => $"{count} of {TotalRequirements} analyzed",
                _ => ""
            };

        public List<(string Label, string Value)> GetQuickStats()
        {
            return new List<(string, string)>
            {
                ("Total", TotalRequirements.ToString()),
                ("Analyzed", AnalyzedRequirements.ToString()),
                ("Pending", PendingRequirements.ToString()),
                ("Progress", AnalysisProgress),
                ("Source", ImportSource)
            };
        }

        // Notify when computed properties change
        protected override void OnPropertyChanged(PropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);

            if (e.PropertyName == nameof(TotalRequirements) || 
                e.PropertyName == nameof(AnalyzedRequirements) || 
                e.PropertyName == nameof(PendingRequirements))
            {
                OnPropertyChanged(nameof(RequirementsStatus));
                OnPropertyChanged(nameof(AnalysisStatus));
            }
        }

        // Workspace management methods
        private void SaveWorkspace()
        {
            // TODO: Implement save workspace functionality
            LastSaveTimestamp = System.DateTime.Now;
            IsDirty = false;
            CanUndoLastSave = true;
        }

        private void UndoLastSave()
        {
            // TODO: Implement undo last save functionality
            CanUndoLastSave = false;
        }

        // Abstract method implementations from BaseDomainViewModel
        protected override async Task SaveAsync()
        {
            SaveWorkspace();
            await Task.CompletedTask;
        }

        protected override void Cancel()
        {
            // Reset any unsaved changes
            IsDirty = false;
        }

        protected override async Task RefreshAsync()
        {
            // Refresh requirements data
            await Task.CompletedTask;
        }

        protected override bool CanSave() => IsDirty && !IsBusy;
        protected override bool CanCancel() => IsDirty;
        protected override bool CanRefresh() => !IsBusy;
    }
}