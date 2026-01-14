using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.MVVM.ViewModels;
using TestCaseEditorApp.MVVM.Domains.Requirements.Mediators;
using TestCaseEditorApp.MVVM.Domains.Requirements.Events;

namespace TestCaseEditorApp.MVVM.Domains.Requirements.ViewModels
{
    /// <summary>
    /// Main ViewModel for Requirements domain.
    /// Provides comprehensive requirements management UI functionality.
    /// Following architectural guide patterns for domain ViewModels.
    /// </summary>
    public partial class Requirements_MainViewModel : BaseDomainViewModel, IDisposable
    {
        private new readonly IRequirementsMediator _mediator;
        private bool _disposed;

        [ObservableProperty]
        private string title = "Requirements Management";

        [ObservableProperty]
        private string description = "Import, analyze, and manage project requirements";

        [ObservableProperty]
        private Requirement? selectedRequirement;

        [ObservableProperty]
        private string searchText = string.Empty;

        [ObservableProperty]
        private bool showAnalyzedOnly;

        [ObservableProperty]
        private bool showUnanalyzedOnly;

        [ObservableProperty]
        private string statusMessage = "Ready";

        [ObservableProperty]
        private int totalRequirements;

        [ObservableProperty]
        private int analyzedRequirements;

        [ObservableProperty]
        private int currentIndex = -1;

        // Bindable collections
        public ObservableCollection<Requirement> Requirements => _mediator.Requirements;
        public ObservableCollection<Requirement> FilteredRequirements { get; } = new();

        // Commands
        public ICommand ImportCommand { get; }
        public ICommand ImportAdditionalCommand { get; }
        public ICommand ExportCommand { get; }
        public ICommand AnalyzeSelectedCommand { get; }
        public ICommand AnalyzeAllCommand { get; }
        public ICommand AnalyzeUnanalyzedCommand { get; }
        public ICommand ClearAllCommand { get; }
        public ICommand SearchCommand { get; }
        public ICommand NextRequirementCommand { get; }
        public ICommand PreviousRequirementCommand { get; }
        public new ICommand RefreshCommand { get; }

        public Requirements_MainViewModel(
            IRequirementsMediator mediator,
            ILogger<Requirements_MainViewModel> logger)
            : base(mediator, logger)
        {
            _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));

            // Initialize commands
            ImportCommand = new AsyncRelayCommand(ImportRequirementsAsync);
            ImportAdditionalCommand = new AsyncRelayCommand(ImportAdditionalRequirementsAsync);
            ExportCommand = new AsyncRelayCommand(ExportRequirementsAsync, () => Requirements.Count > 0);
            AnalyzeSelectedCommand = new AsyncRelayCommand(AnalyzeSelectedRequirementAsync, () => SelectedRequirement != null);
            AnalyzeAllCommand = new AsyncRelayCommand(AnalyzeAllRequirementsAsync, () => Requirements.Count > 0);
            AnalyzeUnanalyzedCommand = new AsyncRelayCommand(AnalyzeUnanalyzedRequirementsAsync, () => Requirements.Count > 0);
            ClearAllCommand = new RelayCommand(ClearAllRequirements, () => Requirements.Count > 0);
            SearchCommand = new RelayCommand(PerformSearch);
            NextRequirementCommand = new RelayCommand(NavigateToNext, () => CanNavigateNext());
            PreviousRequirementCommand = new RelayCommand(NavigateToPrevious, () => CanNavigatePrevious());
            RefreshCommand = new AsyncRelayCommand(RefreshAsync);

            // Subscribe to mediator events
            _mediator.Subscribe<RequirementsEvents.RequirementsImported>(OnRequirementsImported);
            _mediator.Subscribe<RequirementsEvents.RequirementsCollectionChanged>(OnRequirementsCollectionChanged);
            _mediator.Subscribe<RequirementsEvents.RequirementSelected>(OnRequirementSelected);
            _mediator.Subscribe<RequirementsEvents.RequirementAnalyzed>(OnRequirementAnalyzed);
            _mediator.Subscribe<RequirementsEvents.WorkflowStateChanged>(OnWorkflowStateChanged);

            // Initialize filtered collection
            RefreshFilteredRequirements();

            _logger.LogDebug("Requirements_MainViewModel initialized");
        }

        // ===== IMPORT/EXPORT OPERATIONS =====

        private async Task ImportRequirementsAsync()
        {
            try
            {
                // TODO: Show file dialog to select import file
                var filePath = await ShowFileDialogAsync("Select Requirements File", "Word Documents|*.docx|JSON Files|*.json|All Files|*.*");
                if (string.IsNullOrEmpty(filePath)) return;

                StatusMessage = "Importing requirements...";
                var success = await _mediator.ImportRequirementsAsync(filePath);
                
                StatusMessage = success ? "Requirements imported successfully" : "Import failed";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Import failed: {ex.Message}";
                _logger.LogError(ex, "Failed to import requirements");
            }
        }

        private async Task ImportAdditionalRequirementsAsync()
        {
            try
            {
                var filePath = await ShowFileDialogAsync("Select Additional Requirements File", "Word Documents|*.docx|JSON Files|*.json|All Files|*.*");
                if (string.IsNullOrEmpty(filePath)) return;

                StatusMessage = "Importing additional requirements...";
                var success = await _mediator.ImportAdditionalRequirementsAsync(filePath);
                
                StatusMessage = success ? "Additional requirements imported successfully" : "Import failed";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Import failed: {ex.Message}";
                _logger.LogError(ex, "Failed to import additional requirements");
            }
        }

        private async Task ExportRequirementsAsync()
        {
            try
            {
                var filePath = await ShowSaveFileDialogAsync("Export Requirements", "Excel Files|*.xlsx|CSV Files|*.csv|JSON Files|*.json");
                if (string.IsNullOrEmpty(filePath)) return;

                StatusMessage = "Exporting requirements...";
                var exportType = System.IO.Path.GetExtension(filePath).ToUpperInvariant() switch
                {
                    ".XLSX" => "Excel",
                    ".CSV" => "CSV", 
                    ".JSON" => "JSON",
                    _ => "Auto"
                };

                var success = await _mediator.ExportRequirementsAsync(Requirements.ToList().AsReadOnly(), exportType, filePath);
                StatusMessage = success ? "Requirements exported successfully" : "Export failed";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Export failed: {ex.Message}";
                _logger.LogError(ex, "Failed to export requirements");
            }
        }

        // ===== ANALYSIS OPERATIONS =====

        private async Task AnalyzeSelectedRequirementAsync()
        {
            if (SelectedRequirement == null) return;

            try
            {
                StatusMessage = $"Analyzing {SelectedRequirement.GlobalId}...";
                var success = await _mediator.AnalyzeRequirementAsync(SelectedRequirement);
                StatusMessage = success ? "Analysis completed" : "Analysis failed";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Analysis failed: {ex.Message}";
                _logger.LogError(ex, "Failed to analyze requirement {RequirementId}", SelectedRequirement.GlobalId);
            }
        }

        private async Task AnalyzeAllRequirementsAsync()
        {
            try
            {
                StatusMessage = "Analyzing all requirements...";
                var success = await _mediator.AnalyzeBatchRequirementsAsync(Requirements.ToList().AsReadOnly());
                StatusMessage = success ? "Batch analysis completed" : "Batch analysis had failures";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Batch analysis failed: {ex.Message}";
                _logger.LogError(ex, "Failed to analyze all requirements");
            }
        }

        private async Task AnalyzeUnanalyzedRequirementsAsync()
        {
            try
            {
                StatusMessage = "Analyzing unanalyzed requirements...";
                var success = await _mediator.AnalyzeUnanalyzedRequirementsAsync();
                StatusMessage = success ? "Analysis completed" : "Analysis had failures";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Analysis failed: {ex.Message}";
                _logger.LogError(ex, "Failed to analyze unanalyzed requirements");
            }
        }

        // ===== NAVIGATION OPERATIONS =====

        private void NavigateToNext()
        {
            if (_mediator.NavigateToNext())
            {
                SelectedRequirement = _mediator.CurrentRequirement;
                UpdateCurrentIndex();
            }
        }

        private void NavigateToPrevious()
        {
            if (_mediator.NavigateToPrevious())
            {
                SelectedRequirement = _mediator.CurrentRequirement;
                UpdateCurrentIndex();
            }
        }

        private bool CanNavigateNext()
        {
            var index = _mediator.GetCurrentRequirementIndex();
            return index >= 0 && index < Requirements.Count - 1;
        }

        private bool CanNavigatePrevious()
        {
            var index = _mediator.GetCurrentRequirementIndex();
            return index > 0;
        }

        private void UpdateCurrentIndex()
        {
            CurrentIndex = _mediator.GetCurrentRequirementIndex();
        }

        // ===== SEARCH & FILTERING =====

        private void PerformSearch()
        {
            RefreshFilteredRequirements();
        }

        private void RefreshFilteredRequirements()
        {
            FilteredRequirements.Clear();

            var results = Requirements.AsEnumerable();

            // Apply text search
            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                results = results.Where(r =>
                    r.Name?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) == true ||
                    r.Description?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) == true ||
                    r.GlobalId?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) == true);
            }

            // Apply analysis status filters
            if (ShowAnalyzedOnly)
            {
                results = results.Where(r => r.Analysis != null);
            }
            else if (ShowUnanalyzedOnly)
            {
                results = results.Where(r => r.Analysis == null);
            }

            foreach (var requirement in results)
            {
                FilteredRequirements.Add(requirement);
            }

            _logger.LogDebug("Filtered requirements: {Count} of {Total}", FilteredRequirements.Count, Requirements.Count);
        }

        // ===== OTHER OPERATIONS =====

        private void ClearAllRequirements()
        {
            _mediator.ClearRequirements();
            StatusMessage = "All requirements cleared";
        }

        // ===== EVENT HANDLERS =====

        private void OnRequirementsImported(RequirementsEvents.RequirementsImported e)
        {
            UpdateStatistics();
            RefreshFilteredRequirements();
            StatusMessage = $"Imported {e.Requirements.Count} requirements from {System.IO.Path.GetFileName(e.SourceFile)}";
        }

        private void OnRequirementsCollectionChanged(RequirementsEvents.RequirementsCollectionChanged e)
        {
            UpdateStatistics();
            RefreshFilteredRequirements();
            
            // Refresh command states
            ((RelayCommand)ClearAllCommand).NotifyCanExecuteChanged();
            ((AsyncRelayCommand)ExportCommand).NotifyCanExecuteChanged();
            ((AsyncRelayCommand)AnalyzeAllCommand).NotifyCanExecuteChanged();
            ((AsyncRelayCommand)AnalyzeUnanalyzedCommand).NotifyCanExecuteChanged();
        }

        private void OnRequirementSelected(RequirementsEvents.RequirementSelected e)
        {
            if (SelectedRequirement != e.Requirement)
            {
                SelectedRequirement = e.Requirement;
                UpdateCurrentIndex();
                
                // Refresh navigation command states
                ((RelayCommand)NextRequirementCommand).NotifyCanExecuteChanged();
                ((RelayCommand)PreviousRequirementCommand).NotifyCanExecuteChanged();
                ((AsyncRelayCommand)AnalyzeSelectedCommand).NotifyCanExecuteChanged();
            }
        }

        private void OnRequirementAnalyzed(RequirementsEvents.RequirementAnalyzed e)
        {
            UpdateStatistics();
            
            if (e.Success)
            {
                StatusMessage = $"Analysis completed for {e.Requirement.GlobalId}";
            }
            else
            {
                StatusMessage = $"Analysis failed for {e.Requirement.GlobalId}";
            }
        }

        private void OnWorkflowStateChanged(RequirementsEvents.WorkflowStateChanged e)
        {
            if (e.PropertyName == nameof(IRequirementsMediator.IsAnalyzing) && e.NewValue is bool isAnalyzing)
            {
                if (isAnalyzing)
                {
                    StatusMessage = "Analysis in progress...";
                }
            }
            else if (e.PropertyName == nameof(IRequirementsMediator.IsImporting) && e.NewValue is bool isImporting)
            {
                if (isImporting)
                {
                    StatusMessage = "Import in progress...";
                }
            }
        }

        private void UpdateStatistics()
        {
            TotalRequirements = Requirements.Count;
            AnalyzedRequirements = Requirements.Count(r => r.Analysis != null);
        }

        // ===== PROPERTY CHANGE HANDLERS =====

        partial void OnSearchTextChanged(string value)
        {
            PerformSearch();
        }

        partial void OnShowAnalyzedOnlyChanged(bool value)
        {
            if (value) ShowUnanalyzedOnly = false;
            RefreshFilteredRequirements();
        }

        partial void OnShowUnanalyzedOnlyChanged(bool value)
        {
            if (value) ShowAnalyzedOnly = false;
            RefreshFilteredRequirements();
        }

        partial void OnSelectedRequirementChanged(Requirement? value)
        {
            if (value != null && value != _mediator.CurrentRequirement)
            {
                _mediator.SelectRequirement(value);
            }
        }

        // ===== HELPER METHODS =====

        private Task<string?> ShowFileDialogAsync(string title, string filter)
        {
            // TODO: Implement file dialog service integration
            return Task.FromResult<string?>(null);
        }

        private Task<string?> ShowSaveFileDialogAsync(string title, string filter)
        {
            // TODO: Implement save file dialog service integration
            return Task.FromResult<string?>(null);
        }

        // ===== BASE DOMAIN VIEWMODEL IMPLEMENTATION =====

        protected override async Task SaveAsync()
        {
            await _mediator.SaveToProjectAsync();
        }

        protected override void Cancel()
        {
            SearchText = string.Empty;
            ShowAnalyzedOnly = false;
            ShowUnanalyzedOnly = false;
            RefreshFilteredRequirements();
        }

        protected override async Task RefreshAsync()
        {
            RefreshFilteredRequirements();
            UpdateStatistics();
            StatusMessage = "View refreshed";
            await Task.CompletedTask;
        }

        protected override bool CanSave() => _mediator.IsDirty;
        protected override bool CanCancel() => !string.IsNullOrEmpty(SearchText) || ShowAnalyzedOnly || ShowUnanalyzedOnly;
        protected override bool CanRefresh() => true;

        // ===== DISPOSAL =====

        public new void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                // Dispose managed resources
                _disposed = true;
            }
        }
    }
}