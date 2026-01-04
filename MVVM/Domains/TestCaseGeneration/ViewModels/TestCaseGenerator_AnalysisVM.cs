using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.MVVM.Utils;
using TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Services;
using TestCaseEditorApp.Services;
using TestCaseEditorApp.Services.Prompts;

using TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Mediators;
using TestCaseEditorApp.MVVM.Events;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.MVVM.ViewModels;

namespace TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels
{
    /// <summary>
    /// ViewModel for displaying and managing LLM-powered requirement analysis.
    /// Refactored to use domain mediator instead of bridge interface.
    /// </summary>
    public partial class TestCaseGenerator_AnalysisVM : BaseDomainViewModel
    {
        private new readonly ITestCaseGenerationMediator _mediator;
        private readonly ITextGenerationService? _llmService;
        private readonly RequirementAnalysisService _analysisService;
        private Requirement? _currentRequirement;
        
        // Track if edit window is currently open to prevent multiple instances
        [ObservableProperty]
        private bool _isEditWindowOpen = false;
        
        // Expose batch analyzing state for UI binding
        public bool IsBatchAnalyzing => false; // TODO: Get from mediator state when available

        /// <summary>
        /// Gets the quality score of the current requirement's analysis
        /// </summary>
        public int AnalysisQualityScore => CurrentRequirement?.Analysis?.QualityScore ?? 0;

        /// <summary>
        /// Current requirement for analysis
        /// </summary>
        public Requirement? CurrentRequirement
        {
            get => _currentRequirement;
            private set => SetProperty(ref _currentRequirement, value);
        }

        /// <summary>
        /// Current health status of the LLM service
        /// </summary>
        public LlmServiceHealthMonitor.HealthReport? ServiceHealth => _analysisService.ServiceHealth;

        /// <summary>
        /// Whether the LLM service is currently using fallback mode
        /// </summary>
        public bool IsUsingFallback => _analysisService.IsUsingFallback;

        /// <summary>
        /// Text description of the current LLM service status
        /// </summary>
        public string ServiceStatusText
        {
            get
            {
                var health = ServiceHealth;
                if (health == null) return "LLM Status: Unknown";

                return health.Status switch
                {
                    LlmServiceHealthMonitor.HealthStatus.Healthy => $"LLM Status: Healthy ({health.ServiceType})",
                    LlmServiceHealthMonitor.HealthStatus.Degraded => $"LLM Status: Slow ({health.ServiceType})",
                    LlmServiceHealthMonitor.HealthStatus.Unavailable when IsUsingFallback => 
                        $"LLM Status: Using Fallback ({health.ServiceType} unavailable)",
                    LlmServiceHealthMonitor.HealthStatus.Unavailable => $"LLM Status: Unavailable ({health.ServiceType})",
                    _ => "LLM Status: Unknown"
                };
            }
        }

        public TestCaseGenerator_AnalysisVM(ITestCaseGenerationMediator mediator, ILogger<TestCaseGenerator_AnalysisVM> logger, RequirementAnalysisService analysisService, ITextGenerationService? llmService = null)
            : base(mediator, logger)
        {
            _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
            _analysisService = analysisService ?? throw new ArgumentNullException(nameof(analysisService));
            _llmService = llmService;

            // Subscribe to domain events
            _mediator.Subscribe<TestCaseGenerationEvents.RequirementSelected>(OnRequirementSelected);
            _mediator.Subscribe<TestCaseGenerationEvents.RequirementAnalyzed>(OnRequirementAnalyzed);

            // Subscribe to mediator for analysis updates
            AnalysisMediator.AnalysisUpdated += OnAnalysisUpdated;

            AnalyzeRequirementCommand = new AsyncRelayCommand(AnalyzeRequirementAsync, CanAnalyzeRequirement);
            EditRequirementCommand = new RelayCommand(EditRequirement, CanEditRequirement);

            Title = "Requirement Analysis";
            // Initial load
            RefreshAnalysisDisplay();
        }

        // ===== DOMAIN EVENT HANDLERS =====
        
        /// <summary>
        /// Handle requirement selection from mediator
        /// </summary>
        private void OnRequirementSelected(TestCaseGenerationEvents.RequirementSelected e)
        {
            CurrentRequirement = e.Requirement;
            RefreshAnalysisDisplay();
            OnPropertyChanged(nameof(HasAnalysis));
            OnPropertyChanged(nameof(AnalysisQualityScore));
            
            // Update command states
            ((AsyncRelayCommand)AnalyzeRequirementCommand).NotifyCanExecuteChanged();
            ((RelayCommand)EditRequirementCommand).NotifyCanExecuteChanged();
        }
        
        /// <summary>
        /// Handle requirement analysis completion from mediator
        /// </summary>
        private void OnRequirementAnalyzed(TestCaseGenerationEvents.RequirementAnalyzed e)
        {
            if (ReferenceEquals(CurrentRequirement, e.Requirement))
            {
                RefreshAnalysisDisplay();
                OnPropertyChanged(nameof(HasAnalysis));
                OnPropertyChanged(nameof(AnalysisQualityScore));
            }
        }

        private void OnAnalysisUpdated(Requirement requirement)
        {
            _logger.LogDebug("OnAnalysisUpdated fired for: {RequirementId}", requirement.Item);
            _logger.LogDebug("Current requirement: {CurrentId}", CurrentRequirement?.Item);
            _logger.LogDebug("Analysis IsAnalyzed: {IsAnalyzed}, Score: {Score}", 
                requirement.Analysis?.IsAnalyzed, requirement.Analysis?.QualityScore);
            
            // Only refresh if this is the currently displayed requirement
            if (CurrentRequirement?.Item == requirement.Item)
            {
                _logger.LogDebug("Match found! Refreshing display for {RequirementId}", requirement.Item);
                System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                {
                    RefreshAnalysisDisplay();
                });
            }
            else
            {
                TestCaseEditorApp.Services.Logging.Log.Info($"[AnalysisVM] No match - not refreshing (user viewing different requirement)");
            }
        }

        public ICommand AnalyzeRequirementCommand { get; }
        public ICommand EditRequirementCommand { get; }
        public IAsyncRelayCommand? ReAnalyzeCommand { get; private set; }

        private bool CanAnalyzeRequirement()
        {
            return CurrentRequirement != null && !IsAnalyzing && !IsBatchAnalyzing;
        }

        private bool CanEditRequirement()
        {
            return CurrentRequirement != null && !IsAnalyzing && !IsEditWindowOpen;
        }

        private void EditRequirement()
        {
            var requirement = CurrentRequirement;
            if (requirement == null || IsEditWindowOpen) return;

            try
            {
                TestCaseEditorApp.Services.Logging.Log.Debug("[AnalysisVM] EditRequirement called");
                
                // Mark that edit window is open and refresh command states
                IsEditWindowOpen = true;
                ((RelayCommand)EditRequirementCommand).NotifyCanExecuteChanged();

                // Note: Requirement editor functionality should be accessed via UI coordinator/mediator
                TestCaseEditorApp.Services.Logging.Log.Info($"[AnalysisVM] Edit requirement requested for: {requirement.Item}");
                
                TestCaseEditorApp.Services.Logging.Log.Debug("[AnalysisVM] ShowRequirementEditor completed");
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, "[AnalysisVM] Error in EditRequirement");
                
                // Reset state on error
                IsEditWindowOpen = false;
                ((RelayCommand)EditRequirementCommand).NotifyCanExecuteChanged();
                
                // Don't re-throw to prevent app crash
            }
        }

        /// <summary>
        /// Debug method to inspect what's being sent to the LLM for analysis.
        /// Call this method to see the exact prompt and data being analyzed.
        /// </summary>
        public string InspectAnalysisPrompt()
        {
            var requirement = CurrentRequirement;
            if (requirement == null)
                return "No current requirement selected.";

            try
            {
                var prompt = _analysisService.GeneratePromptForInspection(requirement);
                
                var summary = $"=== REQUIREMENT DATA INSPECTION ===\n" +
                             $"ID: {requirement.Item}\n" +
                             $"Name: {requirement.Name}\n" +
                             $"Description Length: {requirement.Description?.Length ?? 0} chars\n" +
                             $"Tables Count: {requirement.Tables?.Count ?? 0}\n" +
                             $"Loose Content Paragraphs: {requirement.LooseContent?.Paragraphs?.Count ?? 0}\n" +
                             $"Loose Content Tables: {requirement.LooseContent?.Tables?.Count ?? 0}\n\n" +
                             $"=== FULL LLM PROMPT ===\n" +
                             $"{prompt}\n" +
                             $"=== END PROMPT ===";
                             
                return summary;
            }
            catch (Exception ex)
            {
                return $"Error inspecting prompt: {ex.Message}";
            }
        }

        /// <summary>
        /// Debug method to see the actual LLM response that was parsed.
        /// </summary>
        public string InspectLastAnalysisResult()
        {
            var requirement = CurrentRequirement;
            if (requirement?.Analysis == null)
                return "No analysis result available.";

            var analysis = requirement.Analysis;
            var summary = $"=== ANALYSIS RESULT INSPECTION ===\n" +
                         $"IsAnalyzed: {analysis.IsAnalyzed}\n" +
                         $"QualityScore: {analysis.QualityScore}\n" +
                         $"Issues Count: {analysis.Issues?.Count ?? 0}\n" +
                         $"Recommendations Count: {analysis.Recommendations?.Count ?? 0}\n" +
                         $"Has Freeform Feedback: {!string.IsNullOrEmpty(analysis.FreeformFeedback)}\n" +
                         $"Timestamp: {analysis.Timestamp}\n";

            if (analysis.Recommendations?.Any() == true)
            {
                summary += "\n=== RECOMMENDATIONS DETAIL ===\n";
                for (int i = 0; i < analysis.Recommendations.Count; i++)
                {
                    var rec = analysis.Recommendations[i];
                    summary += $"Recommendation {i + 1}:\n" +
                              $"  Category: {rec.Category}\n" +
                              $"  Description: {rec.Description}\n" +
                              $"  SuggestedEdit: {rec.SuggestedEdit ?? "(none)"}\n" +
                              $"  Has SuggestedEdit: {!string.IsNullOrEmpty(rec.SuggestedEdit)}\n\n";
                }
            }

            if (!string.IsNullOrEmpty(analysis.ErrorMessage))
            {
                summary += $"\n=== ERROR MESSAGE ===\n{analysis.ErrorMessage}\n";
            }

            return summary;
        }

        private async Task AnalyzeRequirementAsync()
        {
            var requirement = CurrentRequirement;
            if (requirement == null) return;

            TestCaseEditorApp.Services.Logging.Log.Debug($"[AnalysisVM] AnalyzeRequirementAsync started");
            TestCaseEditorApp.Services.Logging.Log.Debug($"[AnalysisVM] Requirement Description (first 200 chars): '{requirement.Description?.Substring(0, Math.Min(200, requirement.Description?.Length ?? 0))}'");

            try
            {
                // Check service health before starting analysis
                var serviceHealth = await _analysisService.GetDetailedHealthAsync();
                if (serviceHealth?.Status == LlmServiceHealthMonitor.HealthStatus.Unavailable && !_analysisService.IsUsingFallback)
                {
                    AnalysisStatusMessage = $"LLM service unavailable ({serviceHealth.ServiceType}). Please check connection.";
                    OnPropertyChanged(nameof(ServiceStatusText));
                    return;
                }

                // Set busy flag to prevent navigation
                TestCaseEditorApp.Services.Logging.Log.Debug("[AnalysisVM] Setting IsLlmBusy = true");
                // Note: LLM busy state should be managed via mediator events

                // Clear existing analysis and show spinner
                HasAnalysis = false;
                IsAnalyzing = true;
                
                // Update status message based on service health
                if (_analysisService.IsUsingFallback)
                {
                    AnalysisStatusMessage = "Analyzing requirement quality (using fallback mode)...";
                }
                else if (serviceHealth?.Status == LlmServiceHealthMonitor.HealthStatus.Degraded)
                {
                    AnalysisStatusMessage = "Analyzing requirement quality (service responding slowly)...";
                }
                else
                {
                    AnalysisStatusMessage = "Analyzing requirement quality...";
                }

                TestCaseEditorApp.Services.Logging.Log.Debug($"[AnalysisVM] About to call AnalyzeRequirementAsync on service");

                // Perform analysis
                var analysis = await _analysisService.AnalyzeRequirementAsync(requirement);

                TestCaseEditorApp.Services.Logging.Log.Debug($"[AnalysisVM] Analysis complete. IsAnalyzed: {analysis.IsAnalyzed}, QualityScore: {analysis.QualityScore}");

                // Store result
                requirement.Analysis = analysis;

                // Mark workspace dirty when analysis is completed
                // Note: Workspace dirty state should be managed via mediator events
                TestCaseEditorApp.Services.Logging.Log.Debug("[AnalysisVM] Analysis completed - should mark workspace dirty");

                // Refresh display
                RefreshAnalysisDisplay();

                // Single UI refresh to avoid duplicate displays
                OnPropertyChanged(nameof(Recommendations));
                OnPropertyChanged(nameof(HasAnalysis));
                OnPropertyChanged(nameof(ServiceStatusText)); // Update service status display
                TestCaseEditorApp.Services.Logging.Log.Debug("[AnalysisVM] UI refresh for analysis results");
                
                if (analysis.IsAnalyzed)
                {
                    var statusSuffix = _analysisService.IsUsingFallback ? " (fallback mode)" : "";
                    AnalysisStatusMessage = $"Analysis complete. Quality score: {analysis.QualityScore}/10{statusSuffix}";
                }
                else
                {
                    var fallbackInfo = _analysisService.IsUsingFallback ? " (Note: LLM service unavailable, using fallback)" : "";
                    AnalysisStatusMessage = $"Analysis failed: {analysis.ErrorMessage}{fallbackInfo}";
                }
            }
            catch (OperationCanceledException)
            {
                AnalysisStatusMessage = "Analysis was cancelled";
                TestCaseEditorApp.Services.Logging.Log.Debug("[AnalysisVM] Analysis cancelled");
            }
            catch (Exception ex)
            {
                var fallbackInfo = _analysisService.IsUsingFallback ? " (fallback mode active)" : "";
                AnalysisStatusMessage = $"Analysis error: {ex.Message}{fallbackInfo}";
                TestCaseEditorApp.Services.Logging.Log.Error(ex, "[AnalysisVM] Analysis error");
            }
            finally
            {
                IsAnalyzing = false;
                
                // Clear busy flag
                TestCaseEditorApp.Services.Logging.Log.Debug("[AnalysisVM] Setting IsLlmBusy = false");
                // Note: LLM busy state should be managed via mediator events

                // Clear status message after a delay
                await Task.Delay(3000);
                AnalysisStatusMessage = string.Empty;
            }
        }

        private void RefreshAnalysisDisplay()
        {
            var analysis = CurrentRequirement?.Analysis;
            var isBatchRunning = IsBatchAnalyzing;
            
            TestCaseEditorApp.Services.Logging.Log.Info($"[AnalysisVM] RefreshAnalysisDisplay called. CurrentReq: {CurrentRequirement?.Item}, HasAnalysis: {analysis?.IsAnalyzed}, Score: {analysis?.QualityScore}, IsBatchAnalyzing: {isBatchRunning}");

            if (analysis?.IsAnalyzed == true)
            {
                HasAnalysis = true;
                IsAnalyzing = false; // Clear spinner for analyzed requirements
                QualityScore = analysis.QualityScore;
                Issues = analysis.Issues;
                
                // Force a fresh list assignment to ensure UI updates properly
                var newRecommendations = analysis.Recommendations?.ToList() ?? new List<AnalysisRecommendation>();
                Recommendations = newRecommendations;
                
                // Log SuggestedEdit status for debugging
                TestCaseEditorApp.Services.Logging.Log.Debug($"[AnalysisVM] Loaded {newRecommendations.Count} recommendations");
                for (int i = 0; i < newRecommendations.Count; i++)
                {
                    var rec = newRecommendations[i];
                    var hasEdit = !string.IsNullOrEmpty(rec.SuggestedEdit);
                    var editPreview = hasEdit ? rec.SuggestedEdit?.Substring(0, Math.Min(50, rec.SuggestedEdit?.Length ?? 0)) + "..." : "<MISSING>";
                    TestCaseEditorApp.Services.Logging.Log.Debug($"[AnalysisVM] Rec {i+1} '{rec.Category}': SuggestedEdit='{editPreview}', HasSuggestedEdit={hasEdit}, Length={rec.SuggestedEdit?.Length ?? 0}");
                    
                    if (!hasEdit)
                    {
                        TestCaseEditorApp.Services.Logging.Log.Warn($"[AnalysisVM] WARNING: Recommendation '{rec.Category}' is missing SuggestedEdit - blue border will not appear!");
                    }
                }
                
                FreeformFeedback = analysis.FreeformFeedback ?? string.Empty;
                AnalysisTimestamp = $"Analyzed on {analysis.Timestamp:MMM d, yyyy 'at' h:mm tt}";
            }
            else
            {
                HasAnalysis = false;
                QualityScore = 0;
                Issues = new List<AnalysisIssue>();
                Recommendations = new List<AnalysisRecommendation>();
                FreeformFeedback = string.Empty;
                AnalysisTimestamp = string.Empty;
                
                // Show spinner during batch analysis for requirements without analysis
                if (isBatchRunning && !IsAnalyzing)
                {
                    IsAnalyzing = true;
                    TestCaseEditorApp.Services.Logging.Log.Info($"[AnalysisVM] Setting IsAnalyzing=true for batch analysis on requirement without analysis: {CurrentRequirement?.Item}");
                }
            }

            // Ensure all analysis properties are notified for UI binding
            OnPropertyChanged(nameof(QualityScore));
            OnPropertyChanged(nameof(HasIssues));
            OnPropertyChanged(nameof(HasRecommendations));
            OnPropertyChanged(nameof(HasFreeformFeedback));
            OnPropertyChanged(nameof(HasNoAnalysis));
            OnPropertyChanged(nameof(IsAnalyzing));
            
            TestCaseEditorApp.Services.Logging.Log.Debug($"[AnalysisVM] RefreshAnalysisDisplay complete - QualityScore: {QualityScore}, HasAnalysis: {HasAnalysis}, IsAnalyzing: {IsAnalyzing}");
        }

        [ObservableProperty]
        private bool hasAnalysis;

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
        private bool isAnalyzing;

        [ObservableProperty]
        private string analysisStatusMessage = string.Empty;

        [ObservableProperty]
        private string _editedDescription = string.Empty;

        [ObservableProperty]
        private string _windowTitle = "Edit Requirement Description";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsNotAnalyzing))]
        private bool _isAnalyzingInEditor;

        public bool IsNotAnalyzing => !IsAnalyzingInEditor;

        partial void OnIsAnalyzingChanged(bool value)
        {
            ((AsyncRelayCommand)AnalyzeRequirementCommand).NotifyCanExecuteChanged();
            ((RelayCommand)EditRequirementCommand).NotifyCanExecuteChanged();
        }

        public bool HasIssues => Issues?.Any() == true;
        public bool HasRecommendations => Recommendations?.Any() == true;
        public bool HasFreeformFeedback => !string.IsNullOrWhiteSpace(FreeformFeedback) && 
                                           !IsNoFeedbackPlaceholder(FreeformFeedback);
        
        private static bool IsNoFeedbackPlaceholder(string? feedback)
        {
            if (string.IsNullOrWhiteSpace(feedback)) return true;
            
            var normalized = feedback.Trim().ToLowerInvariant();
            return normalized.Contains("no additional insights") ||
                   normalized.Contains("no additional feedback") ||
                   normalized.Contains("not necessary") ||
                   normalized.StartsWith("<") && normalized.EndsWith(">") || // Template placeholders
                   normalized == "none" ||
                   normalized == "n/a";
        }
        public bool HasNoAnalysis => !HasAnalysis;

        // ===== ABSTRACT METHOD IMPLEMENTATIONS =====
        
        protected override bool CanSave() => false; // Analysis doesn't save directly
        protected override async Task SaveAsync() => await Task.CompletedTask;
        protected override bool CanRefresh() => true;
        protected override async Task RefreshAsync()
        {
            RefreshAnalysisDisplay();
            await Task.CompletedTask;
        }
        protected override bool CanCancel() => IsAnalyzing;
        protected override void Cancel()
        {
            // TODO: Cancel analysis operation if running
        }
    }
}