using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.MVVM.Domains.Requirements.Services;

namespace TestCaseEditorApp.MVVM.Domains.Requirements.ViewModels
{
    /// <summary>
    /// Focused ViewModel for requirement analysis UI concerns in the Requirements domain.
    /// 
    /// This replaces the monolithic TestCaseGenerator_AnalysisVM with a clean, focused implementation
    /// that follows proper MVVM principles:
    /// - UI state management only
    /// - Business logic delegated to services
    /// - Proper dependency injection
    /// - Clear separation of concerns
    /// 
    /// Business logic is handled by IRequirementAnalysisEngine, making this ViewModel lightweight and testable.
    /// </summary>
    public partial class RequirementAnalysisViewModel : ObservableObject
    {
        private readonly IRequirementAnalysisEngine _analysisEngine;
        private readonly ILogger<RequirementAnalysisViewModel> _logger;
        private CancellationTokenSource? _analysisCancellation;

        // UI State Properties  
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasNoAnalysis))]
        private bool hasAnalysis;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasNoAnalysis))]
        private bool isAnalyzing;

        [ObservableProperty]
        private string analysisStatusMessage = string.Empty;

        [ObservableProperty]
        private int qualityScore;

        [ObservableProperty]
        private List<AnalysisIssue> issues = new();

        [ObservableProperty]
        private List<AnalysisRecommendation> recommendations = new();

        [ObservableProperty]
        private string freeformFeedback = string.Empty;

        [ObservableProperty]
        private string analysisTimestamp = string.Empty;

        [ObservableProperty]
        private string? improvedRequirement;

        [ObservableProperty]
        private bool hasImprovedRequirement;

        [ObservableProperty]
        private string engineStatusText = string.Empty;

        [ObservableProperty] 
        private bool isEditingRequirement;

        // Computed properties for UI binding
        public bool HasNoAnalysis => !HasAnalysis && !IsAnalyzing;
        public bool HasFreeformFeedback => !string.IsNullOrWhiteSpace(FreeformFeedback);
        
        // Override property change notifications to trigger HasNoAnalysis updates
        partial void OnHasAnalysisChanged(bool value)
        {
            OnPropertyChanged(nameof(HasNoAnalysis));
        }
        
        partial void OnIsAnalyzingChanged(bool value)
        {
            OnPropertyChanged(nameof(HasNoAnalysis));
        }

        // Current requirement being analyzed
        private Requirement? _currentRequirement;
        public Requirement? CurrentRequirement 
        { 
            get => _currentRequirement;
            set
            {
                if (SetProperty(ref _currentRequirement, value))
                {
                    RefreshAnalysisDisplay();
                    ((RelayCommand)AnalyzeRequirementCommand).NotifyCanExecuteChanged();
                }
            }
        }

        // Commands
        public ICommand AnalyzeRequirementCommand { get; }
        public ICommand CancelAnalysisCommand { get; }
        public ICommand RefreshEngineStatusCommand { get; }
        public ICommand EditRequirementCommand { get; }
        public ICommand CancelEditRequirementCommand { get; }

        public RequirementAnalysisViewModel(
            IRequirementAnalysisEngine analysisEngine,
            ILogger<RequirementAnalysisViewModel> logger)
        {
            _analysisEngine = analysisEngine ?? throw new ArgumentNullException(nameof(analysisEngine));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            // Initialize commands
            AnalyzeRequirementCommand = new AsyncRelayCommand(AnalyzeRequirementAsync, CanAnalyzeRequirement);
            CancelAnalysisCommand = new RelayCommand(CancelAnalysis, () => IsAnalyzing);
            RefreshEngineStatusCommand = new RelayCommand(RefreshEngineStatus);
            EditRequirementCommand = new RelayCommand(StartEditingRequirement, CanEditRequirement);
            CancelEditRequirementCommand = new RelayCommand(CancelEditingRequirement, () => IsEditingRequirement);

            // Initialize engine status
            RefreshEngineStatus();
        }

        /// <summary>
        /// Analyzes the current requirement using the analysis engine.
        /// Demonstrates proper separation: ViewModel handles UI state, engine handles business logic.
        /// </summary>
        private async Task AnalyzeRequirementAsync()
        {
            if (CurrentRequirement == null) return;

            _logger.LogDebug("[RequirementAnalysisVM] Starting analysis for {RequirementId}", CurrentRequirement.Item);

            try
            {
                // Create cancellation token for this analysis
                _analysisCancellation?.Cancel();
                _analysisCancellation = new CancellationTokenSource();

                // Update UI state
                IsAnalyzing = true;
                HasAnalysis = false;
                AnalysisStatusMessage = "Initializing analysis...";

                // Delegate business logic to the analysis engine
                var analysis = await _analysisEngine.AnalyzeRequirementAsync(
                    CurrentRequirement,
                    progressMessage => 
                    {
                        // Update UI with progress from the engine
                        AnalysisStatusMessage = progressMessage;
                    },
                    _analysisCancellation.Token);

                // Update UI with results
                if (analysis.IsAnalyzed)
                {
                    _logger.LogDebug("[RequirementAnalysisVM] Analysis completed for {RequirementId}", CurrentRequirement.Item);
                    UpdateUIFromAnalysis(analysis);
                    AnalysisStatusMessage = string.Empty; // Clear status on success
                }
                else
                {
                    _logger.LogWarning("[RequirementAnalysisVM] Analysis failed for {RequirementId}: {Error}", 
                        CurrentRequirement.Item, analysis.ErrorMessage);
                    AnalysisStatusMessage = analysis.ErrorMessage ?? "Analysis failed";
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("[RequirementAnalysisVM] Analysis cancelled for {RequirementId}", CurrentRequirement?.Item);
                AnalysisStatusMessage = "Analysis cancelled";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RequirementAnalysisVM] Unexpected error during analysis");
                AnalysisStatusMessage = "Analysis failed due to unexpected error";
            }
            finally
            {
                IsAnalyzing = false;
                ((RelayCommand)CancelAnalysisCommand).NotifyCanExecuteChanged();
                RefreshEngineStatus(); // Update engine status after analysis
            }
        }

        /// <summary>
        /// Updates the UI properties from an analysis result.
        /// Pure UI logic - no business rules here.
        /// </summary>
        private void UpdateUIFromAnalysis(RequirementAnalysis analysis)
        {
            HasAnalysis = true;
            QualityScore = analysis.OriginalQualityScore; // Show user's original requirement quality
            Issues = analysis.Issues ?? new List<AnalysisIssue>();
            Recommendations = analysis.Recommendations ?? new List<AnalysisRecommendation>();
            FreeformFeedback = analysis.FreeformFeedback ?? string.Empty;
            ImprovedRequirement = analysis.ImprovedRequirement;
            HasImprovedRequirement = !string.IsNullOrWhiteSpace(analysis.ImprovedRequirement);
            AnalysisTimestamp = $"Analyzed on {analysis.Timestamp:MMM d, yyyy 'at' h:mm tt}";

            _logger.LogDebug("[RequirementAnalysisVM] UI updated with analysis results");
        }

        /// <summary>
        /// Refreshes the analysis display when the current requirement changes.
        /// </summary>
        private void RefreshAnalysisDisplay()
        {
            var analysis = CurrentRequirement?.Analysis;

            if (analysis?.IsAnalyzed == true)
            {
                UpdateUIFromAnalysis(analysis);
                _logger.LogDebug("[RequirementAnalysisVM] Displayed existing analysis for {RequirementId}", CurrentRequirement?.Item);
            }
            else
            {
                // Clear display for requirements without analysis - ensure complete state reset
                HasAnalysis = false;
                QualityScore = 0;
                Issues = new List<AnalysisIssue>();
                Recommendations = new List<AnalysisRecommendation>();
                FreeformFeedback = string.Empty;
                ImprovedRequirement = null;
                HasImprovedRequirement = false;
                AnalysisTimestamp = string.Empty;
                AnalysisStatusMessage = string.Empty; // Critical: Clear any stale error messages
                
                // Clear any stale analysis data from the requirement to prevent UI inconsistencies
                if (CurrentRequirement?.Analysis != null && !CurrentRequirement.Analysis.IsAnalyzed)
                {
                    CurrentRequirement.Analysis.ErrorMessage = null; // Clear persisted error state
                }
                
                _logger.LogDebug("[RequirementAnalysisVM] Cleared display state for {RequirementId}", CurrentRequirement?.Item);
            }
        }

        /// <summary>
        /// Cancels the current analysis operation.
        /// </summary>
        private void CancelAnalysis()
        {
            _logger.LogDebug("[RequirementAnalysisVM] User requested analysis cancellation");
            _analysisCancellation?.Cancel();
        }

        /// <summary>
        /// Updates the engine status display.
        /// </summary>
        private void RefreshEngineStatus()
        {
            try
            {
                var status = _analysisEngine.GetEngineStatus();
                EngineStatusText = status.IsHealthy ? status.StatusMessage : $"⚠️ {status.StatusMessage}";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RequirementAnalysisVM] Failed to refresh engine status");
                EngineStatusText = "Engine status unavailable";
            }
        }

        /// <summary>
        /// Determines if analysis can be performed.
        /// </summary>
        private bool CanAnalyzeRequirement()
        {
            return CurrentRequirement != null && !IsAnalyzing;
        }

        /// <summary>
        /// Gets diagnostic information for the current requirement.
        /// Useful for debugging and troubleshooting.
        /// </summary>
        public string GetDiagnosticInfo()
        {
            if (CurrentRequirement == null)
                return "No requirement selected";

            try
            {
                return _analysisEngine.GeneratePromptForInspection(CurrentRequirement);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RequirementAnalysisVM] Failed to generate diagnostic info");
                return $"Diagnostic generation failed: {ex.Message}";
            }
        }

        /// <summary>
        /// Starts editing the current requirement.
        /// Puts the UI into edit mode for requirement refinement.
        /// </summary>
        private void StartEditingRequirement()
        {
            IsEditingRequirement = true;
            _logger.LogDebug("[RequirementAnalysisVM] Started editing requirement");
        }

        /// <summary>
        /// Cancels requirement editing and returns to read-only mode.
        /// </summary>
        private void CancelEditingRequirement()
        {
            IsEditingRequirement = false;
            _logger.LogDebug("[RequirementAnalysisVM] Cancelled requirement editing");
        }

        /// <summary>
        /// Determines if requirement editing is allowed.
        /// </summary>
        private bool CanEditRequirement()
        {
            return HasImprovedRequirement && !IsAnalyzing;
        }

        protected override void OnPropertyChanged(PropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);
            
            // Update command states when relevant properties change
            if (e.PropertyName == nameof(IsAnalyzing))
            {
                ((RelayCommand)CancelAnalysisCommand).NotifyCanExecuteChanged();
                ((RelayCommand)AnalyzeRequirementCommand).NotifyCanExecuteChanged();
                ((RelayCommand)EditRequirementCommand).NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(HasNoAnalysis)); // Computed property depends on IsAnalyzing
            }
            else if (e.PropertyName == nameof(HasAnalysis))
            {
                OnPropertyChanged(nameof(HasNoAnalysis)); // Computed property depends on HasAnalysis
            }
            else if (e.PropertyName == nameof(HasImprovedRequirement))
            {
                ((RelayCommand)EditRequirementCommand).NotifyCanExecuteChanged();
            }
            else if (e.PropertyName == nameof(IsEditingRequirement))
            {
                ((RelayCommand)CancelEditRequirementCommand).NotifyCanExecuteChanged();
            }
            else if (e.PropertyName == nameof(FreeformFeedback))
            {
                OnPropertyChanged(nameof(HasFreeformFeedback)); // Computed property depends on FreeformFeedback
            }
        }

        public void Dispose()
        {
            _analysisCancellation?.Cancel();
            _analysisCancellation?.Dispose();
        }
    }
}