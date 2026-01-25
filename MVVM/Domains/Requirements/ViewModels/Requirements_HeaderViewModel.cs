using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.MVVM.Domains.Requirements.Mediators;
using RequirementsMediator = TestCaseEditorApp.MVVM.Domains.Requirements.Mediators.RequirementsMediator;
using TestCaseEditorApp.MVVM.Domains.Requirements.Events;
using TestCaseEditorApp.MVVM.Events;
using TestCaseEditorApp.MVVM.ViewModels;
using TestCaseEditorApp.Services;

namespace TestCaseEditorApp.MVVM.Domains.Requirements.ViewModels
{
    /// <summary>
    /// Header ViewModel for Requirements domain.
    /// Provides contextual header information and quick stats.
    /// Following architectural guide patterns for header ViewModels.
    /// </summary>
    public partial class Requirements_HeaderViewModel : BaseDomainViewModel
    {
        private new readonly IRequirementsMediator _mediator;
        private readonly IWorkspaceContext _workspaceContext;

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
            IRequirementsMediator mediator,
            IWorkspaceContext workspaceContext,
            ILogger<Requirements_HeaderViewModel> logger)
            : base(mediator, logger)
        {
            _mediator = mediator ?? throw new System.ArgumentNullException(nameof(mediator));
            _workspaceContext = workspaceContext ?? throw new System.ArgumentNullException(nameof(workspaceContext));

            // Initialize workspace commands
            SaveWorkspaceCommand = new RelayCommand(SaveWorkspace, () => IsDirty);
            UndoLastSaveCommand = new RelayCommand(UndoLastSave, () => CanUndoLastSave);

            // Subscribe to Requirements events for real-time updates
            if (_mediator is RequirementsMediator concreteMediator)
            {
                concreteMediator.Subscribe<RequirementsEvents.RequirementsImported>(OnRequirementsImported);
                concreteMediator.Subscribe<RequirementsEvents.RequirementsCollectionChanged>(OnRequirementsCollectionChanged);
                concreteMediator.Subscribe<RequirementsEvents.RequirementAnalyzed>(OnRequirementAnalyzed);
                concreteMediator.Subscribe<RequirementsEvents.RequirementSelected>(OnRequirementSelected);
            }

            // Initialize values
            UpdateStatistics();

            _logger.LogDebug("Requirements_HeaderViewModel initialized");
        }

        private void OnRequirementsImported(RequirementsEvents.RequirementsImported e)
        {
            ImportSource = System.IO.Path.GetFileName(e.SourceFile) ?? "Unknown";
            UpdateStatistics();
        }

        private void OnRequirementsCollectionChanged(RequirementsEvents.RequirementsCollectionChanged e)
        {
            // If requirements are being cleared (e.g., project close), reset all header state
            if (e.Action == "Clear" && e.NewCount == 0)
            {
                _logger.LogDebug("Requirements cleared - resetting header state");
                
                // Reset all project-specific properties
                ImportSource = "None";
                WorkspaceFilePath = null;
                LastSaveTimestamp = null;
                RequirementDescription = ""; // Empty instead of default text
                
                _logger.LogDebug("Header state reset: ImportSource={ImportSource}, WorkspaceFilePath={WorkspaceFilePath}", 
                    ImportSource, WorkspaceFilePath ?? "null");
            }
            
            UpdateStatistics();
        }

        private void OnRequirementAnalyzed(RequirementsEvents.RequirementAnalyzed e)
        {
            UpdateStatistics();
        }

        private void OnRequirementSelected(RequirementsEvents.RequirementSelected e)
        {
            // Update header description based on selected requirement and ImportSource
            if (e.Requirement != null)
            {
                var workspace = _workspaceContext.CurrentWorkspace;
                var isJamaImport = !string.IsNullOrEmpty(workspace?.ImportSource) && 
                                 string.Equals(workspace.ImportSource, "Jama", System.StringComparison.OrdinalIgnoreCase);
                
                if (isJamaImport)
                {
                    // Jama Import: Show requirement name only (until better use is determined)
                    RequirementDescription = e.Requirement.Name ?? "Unnamed requirement";
                }
                else
                {
                    // Document Import: Show filtered requirement details (remove supplemental/table data)
                    var idPart = !string.IsNullOrEmpty(e.Requirement.Item) ? e.Requirement.Item : e.Requirement.GlobalId ?? "Unknown";
                    var namePart = e.Requirement.Name ?? "Unnamed requirement";
                    
                    // Filter out supplemental and table data - show clean description
                    var description = FilterDocumentRequirementDetails(e.Requirement);
                    RequirementDescription = !string.IsNullOrEmpty(description) ? 
                        $"{idPart}: {description}" : 
                        $"{idPart}: {namePart}";
                }
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

        /// <summary>
        /// Filter requirement details for document imports - remove supplemental and table data
        /// </summary>
        private string FilterDocumentRequirementDetails(TestCaseEditorApp.MVVM.Models.Requirement requirement)
        {
            // For document imports, show clean description by filtering out supplemental/table data
            var description = requirement.Description;
            
            if (string.IsNullOrEmpty(description))
            {
                return requirement.Name ?? "No description available";
            }
            
            // Filter logic: Remove common supplemental content patterns
            var filtered = description;
            
            // Remove table-like content (lines with multiple pipe characters)
            var lines = filtered.Split('\n', '\r').Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
            var cleanLines = lines.Where(line => 
                !line.Contains("|") || // Remove table rows
                line.Split('|').Length < 3 // Keep lines that aren't table-formatted
            ).ToArray();
            
            // Remove supplemental markers and metadata
            cleanLines = cleanLines.Where(line =>
                !line.TrimStart().StartsWith("Supplemental:", System.StringComparison.OrdinalIgnoreCase) &&
                !line.TrimStart().StartsWith("Table:", System.StringComparison.OrdinalIgnoreCase) &&
                !line.TrimStart().StartsWith("Figure:", System.StringComparison.OrdinalIgnoreCase) &&
                !line.TrimStart().StartsWith("Note:", System.StringComparison.OrdinalIgnoreCase)
            ).ToArray();
            
            // Take first meaningful sentence/paragraph for header display
            var cleanDescription = string.Join(" ", cleanLines).Trim();
            
            // Limit length for header display
            if (cleanDescription.Length > 150)
            {
                cleanDescription = cleanDescription.Substring(0, 147) + "...";
            }
            
            return string.IsNullOrEmpty(cleanDescription) ? requirement.Name ?? "No description available" : cleanDescription;
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
            // Refresh requirements data and statistics when activated
            UpdateStatistics();
            await Task.CompletedTask;
        }

        protected override bool CanSave() => IsDirty && !IsBusy;
        protected override bool CanCancel() => IsDirty;
        protected override bool CanRefresh() => !IsBusy;
    }
}