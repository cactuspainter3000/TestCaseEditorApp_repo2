using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.MVVM.Domains.Requirements.Mediators;
using TestCaseEditorApp.MVVM.Domains.Requirements.Events;
using TestCaseEditorApp.MVVM.ViewModels;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.MVVM.Domains.Requirements.ViewModels
{
    /// <summary>
    /// ViewModel for Jama-optimized Requirements main view.
    /// Designed specifically for displaying rich, structured Jama requirement data.
    /// Features tabbed content views and integrated analysis panel.
    /// </summary>
    public partial class JamaRequirementsMainViewModel : BaseDomainViewModel
    {
        private new readonly IRequirementsMediator _mediator;

        [ObservableProperty]
        private bool isRichContentSelected = true; // Default to Rich Content tab

        [ObservableProperty]
        private bool isAnalysisSelected;

        [ObservableProperty]
        private bool isMetadataSelected;

        [ObservableProperty]
        private Requirement? currentRequirement;

        [ObservableProperty]
        private bool hasCurrentRequirement;

        [ObservableProperty]
        private bool hasAnalysis;

        [ObservableProperty]
        private bool canGenerateTests;

        [ObservableProperty]
        private string qualityScoreDisplay = "Not analyzed";

        [ObservableProperty]
        private string analysisSummary = "No analysis performed yet.";

        [ObservableProperty]
        private bool isAnalyzing;

        [ObservableProperty]
        private string analysisElapsedTime = "";

        // Analysis ViewModel for the Requirements_AnalysisControl
        public RequirementAnalysisViewModel RequirementAnalysisVM { get; }

        // Analysis timer
        private System.Timers.Timer? _analysisTimer;
        private DateTime _analysisStartTime;

        // Tab selection commands
        public ICommand SelectRichContentCommand { get; }
        public ICommand SelectAnalysisCommand { get; }

        public ICommand SelectMetadataCommand { get; }

        // Action commands
        public ICommand QuickAnalyzeCommand { get; }
        public ICommand GenerateTestsCommand { get; }
        public ICommand ViewInTestGenCommand { get; }

        public JamaRequirementsMainViewModel(
            IRequirementsMediator mediator,
            ILogger<JamaRequirementsMainViewModel> logger,
            RequirementAnalysisViewModel requirementAnalysisVM)
            : base(mediator, logger)
        {
            _mediator = mediator;
            RequirementAnalysisVM = requirementAnalysisVM ?? throw new ArgumentNullException(nameof(requirementAnalysisVM));

            // Initialize commands
            SelectRichContentCommand = new RelayCommand(() => SelectTab("RichContent"));
            SelectAnalysisCommand = new RelayCommand(() => SelectTab("Analysis"));

            SelectMetadataCommand = new RelayCommand(() => SelectTab("Metadata"));

            QuickAnalyzeCommand = new RelayCommand(ExecuteQuickAnalyze, () => HasCurrentRequirement);
            GenerateTestsCommand = new RelayCommand(ExecuteGenerateTests, () => CanGenerateTests);
            ViewInTestGenCommand = new RelayCommand(ExecuteViewInTestGen, () => HasCurrentRequirement);

            // Subscribe to mediator events
            if (_mediator is RequirementsMediator concreteMediator)
            {
                concreteMediator.Subscribe<RequirementsEvents.RequirementSelected>(OnRequirementSelected);
                concreteMediator.Subscribe<RequirementsEvents.RequirementAnalyzed>(OnRequirementAnalyzed);
                
                // Initialize with current requirement if available
                CurrentRequirement = concreteMediator.CurrentRequirement;
                UpdateRequirementState();
            }

            // Ensure only one tab is selected at a time
            PropertyChanged += OnTabSelectionChanged;
        }

        #region BaseDomainViewModel Implementation

        protected override async Task SaveAsync()
        {
            // Jama requirements are read-only for display purposes
            await Task.CompletedTask;
        }

        protected override void Cancel()
        {
            // Nothing to cancel for display-only view
        }

        protected override async Task RefreshAsync()
        {
            try
            {
                // Refresh current requirement data from mediator
                if (CurrentRequirement != null)
                {
                    UpdateRequirementState();
                }
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[JamaRequirementsMainVM] Error during refresh");
            }
        }

        protected override bool CanSave() => false; // Read-only view
        protected override bool CanCancel() => false; // Nothing to cancel
        protected override bool CanRefresh() => HasCurrentRequirement;

        #endregion

        private void OnTabSelectionChanged(object? sender, PropertyChangedEventArgs e)
        {
            // Ensure mutual exclusivity of tab selections
            switch (e.PropertyName)
            {
                case nameof(IsRichContentSelected) when IsRichContentSelected:
                    IsAnalysisSelected = IsMetadataSelected = false;
                    break;
                case nameof(IsAnalysisSelected) when IsAnalysisSelected:
                    IsRichContentSelected = IsMetadataSelected = false;
                    break;
                case nameof(IsMetadataSelected) when IsMetadataSelected:
                    IsRichContentSelected = IsAnalysisSelected = false;
                    break;
            }
        }

        private void SelectTab(string tabName)
        {
            IsRichContentSelected = tabName == "RichContent";
            IsAnalysisSelected = tabName == "Analysis";
            IsMetadataSelected = tabName == "Metadata";

            _logger.LogDebug("[JamaRequirementsMainVM] Selected tab: {TabName}", tabName);
        }

        private void OnRequirementSelected(RequirementsEvents.RequirementSelected eventData)
        {
            try
            {
                CurrentRequirement = eventData.Requirement;
                UpdateRequirementState();

                // Update the RequirementAnalysisVM with the current requirement
                RequirementAnalysisVM.CurrentRequirement = eventData.Requirement;

                _logger.LogDebug("[JamaRequirementsMainVM] Requirement selected: {RequirementId}", 
                    eventData.Requirement?.GlobalId ?? "null");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[JamaRequirementsMainVM] Error handling RequirementSelected event");
            }
        }

        private void OnRequirementAnalyzed(RequirementsEvents.RequirementAnalyzed eventData)
        {
            try
            {
                if (eventData.Requirement?.GlobalId == CurrentRequirement?.GlobalId)
                {
                    UpdateAnalysisState();
                    
                    // Update the RequirementAnalysisVM with the current requirement so it shows the analysis
                    RequirementAnalysisVM.CurrentRequirement = eventData.Requirement;
                    
                    _logger.LogDebug("[JamaRequirementsMainVM] Analysis updated for requirement: {RequirementId}", 
                        eventData.Requirement?.GlobalId ?? "unknown");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[JamaRequirementsMainVM] Error handling RequirementAnalyzed event");
            }
        }

        private void UpdateRequirementState()
        {
            HasCurrentRequirement = CurrentRequirement != null;
            
            if (CurrentRequirement != null)
            {
                // Update analysis state
                UpdateAnalysisState();

                // Update workflow capabilities
                CanGenerateTests = HasCurrentRequirement && 
                    (!HasAnalysis || (CurrentRequirement.Analysis?.OriginalQualityScore ?? 0) > 60);
            }
            else
            {
                HasAnalysis = false;
                CanGenerateTests = false;
                QualityScoreDisplay = "Not analyzed";
                AnalysisSummary = "No requirement selected.";
            }

            // Update command states
            ((RelayCommand)QuickAnalyzeCommand).NotifyCanExecuteChanged();
            ((RelayCommand)GenerateTestsCommand).NotifyCanExecuteChanged();
            ((RelayCommand)ViewInTestGenCommand).NotifyCanExecuteChanged();
        }

        private void UpdateAnalysisState()
        {
            if (CurrentRequirement?.Analysis != null)
            {
                HasAnalysis = true;
                var score = CurrentRequirement.Analysis.OriginalQualityScore;
                QualityScoreDisplay = score > 0 ? $"{score:F0}%" : "No score";

                // Show per-requirement completion time if available
                if (CurrentRequirement.Analysis.AnalysisDurationSeconds > 0)
                {
                    AnalysisElapsedTime = $"Completed in {CurrentRequirement.Analysis.AnalysisDurationSeconds:F0}s";
                }
                else
                {
                    AnalysisElapsedTime = "";
                }

                // Create analysis summary
                var issueCount = CurrentRequirement.Analysis.Issues?.Count ?? 0;
                AnalysisSummary = issueCount switch
                {
                    0 => "No issues found. Requirement looks good!",
                    1 => "1 issue identified. Review recommended.",
                    _ => $"{issueCount} issues identified. Review recommended."
                };
            }
            else
            {
                HasAnalysis = false;
                QualityScoreDisplay = "Not analyzed";
                AnalysisSummary = "No analysis performed yet.";
                AnalysisElapsedTime = "";
            }
        }

        private async void ExecuteQuickAnalyze()
        {
            try
            {
                if (CurrentRequirement == null) return;

                _logger.LogInformation("[JamaRequirementsMainVM] Starting quick analysis for requirement: {RequirementId}", 
                    CurrentRequirement.GlobalId);

                // Start analysis timer
                StartAnalysisTimer();
                IsAnalyzing = true;

                // Trigger analysis through mediator
                bool success = await _mediator.AnalyzeRequirementAsync(CurrentRequirement);
                
                // Stop analysis timer
                StopAnalysisTimer();
                IsAnalyzing = false;
                
                if (success)
                {
                    _logger.LogDebug("[JamaRequirementsMainVM] Quick analyze completed successfully");
                    // Analysis results will be updated via the RequirementAnalyzed event
                    // which is already handled by OnRequirementAnalyzed method
                }
                else
                {
                    _logger.LogWarning("[JamaRequirementsMainVM] Quick analyze failed");
                }
            }
            catch (Exception ex)
            {
                StopAnalysisTimer();
                IsAnalyzing = false;
                _logger.LogError(ex, "[JamaRequirementsMainVM] Error executing quick analyze");
            }
        }

        private void StartAnalysisTimer()
        {
            _analysisStartTime = DateTime.Now;
            AnalysisElapsedTime = "";
            
            _analysisTimer?.Stop();
            _analysisTimer = new System.Timers.Timer(1000); // Update every second
            _analysisTimer.Elapsed += UpdateAnalysisTimer;
            _analysisTimer.AutoReset = true;
            _analysisTimer.Start();
        }

        private void UpdateAnalysisTimer(object? sender, System.Timers.ElapsedEventArgs e)
        {
            var elapsed = DateTime.Now - _analysisStartTime;
            
            // Dispatch to UI thread
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                AnalysisElapsedTime = $"{elapsed.TotalSeconds:F0}s";
            });
        }

        private void StopAnalysisTimer()
        {
            _analysisTimer?.Stop();
            var totalElapsed = DateTime.Now - _analysisStartTime;
            if (totalElapsed.TotalSeconds > 0)
            {
                AnalysisElapsedTime = $"Completed in {totalElapsed.TotalSeconds:F0}s";
            }
            else
            {
                AnalysisElapsedTime = "";
            }
        }

        private void ExecuteGenerateTests()
        {
            try
            {
                if (CurrentRequirement == null) return;

                _logger.LogInformation("[JamaRequirementsMainVM] Starting test generation for requirement: {RequirementId}", 
                    CurrentRequirement.GlobalId);

                // TODO: Navigate to TestCaseGeneration domain with this requirement
                _logger.LogDebug("[JamaRequirementsMainVM] Test generation requested - should navigate to TestGen domain");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[JamaRequirementsMainVM] Error executing generate tests");
            }
        }

        private void ExecuteViewInTestGen()
        {
            try
            {
                if (CurrentRequirement == null) return;

                _logger.LogInformation("[JamaRequirementsMainVM] Opening requirement in TestGen: {RequirementId}", 
                    CurrentRequirement.GlobalId);

                // TODO: Navigate to TestCaseGeneration domain and select this requirement
                _logger.LogDebug("[JamaRequirementsMainVM] View in TestGen requested - should navigate and select requirement");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[JamaRequirementsMainVM] Error executing view in test generation");
            }
        }

        /// <summary>
        /// Get display-friendly content for rich text rendering
        /// In the future, this could integrate with HTML rendering controls
        /// </summary>
        public string GetRichContentHtml()
        {
            if (CurrentRequirement?.Description == null)
                return "<p>No content available</p>";

            // For now, return clean text
            // TODO: Integrate with HTML rendering component for rich display
            return CurrentRequirement.Description;
        }

        public override void Dispose()
        {
            _analysisTimer?.Stop();
            _analysisTimer?.Dispose();
            PropertyChanged -= OnTabSelectionChanged;
            base.Dispose();
        }
    }
}