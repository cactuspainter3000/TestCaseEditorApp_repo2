using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Text;
using System.Windows;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.MVVM.Utils;
using TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Services; // For LlmServiceHealthMonitor, RequirementAnalysisCache, IRequirementAnalysisService
using TestCaseEditorApp.MVVM.Domains.Requirements.Services; // Requirements domain for analysis engine
using TestCaseEditorApp.Services;
using TestCaseEditorApp.Services.Prompts;
using TestCaseEditorApp.MVVM.ViewModels;
using TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Mediators;
using TestCaseEditorApp.MVVM.Events;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels
{
    /// <summary>
    /// ViewModel for displaying and managing LLM-powered requirement analysis.
    /// ARCHITECTURAL MIGRATION: Now delegates to Requirements domain analysis engine.
    /// This eliminates duplication and makes Requirements domain the single source of truth.
    /// </summary>
    public partial class TestCaseGenerator_AnalysisVM : BaseDomainViewModel
    {
        private new readonly ITestCaseGenerationMediator _mediator;
        private readonly ITextGenerationService? _llmService;
        private readonly TestCaseEditorApp.MVVM.Domains.Requirements.Services.IRequirementAnalysisService _analysisService; // Legacy - will be replaced
        private readonly IRequirementAnalysisEngine? _requirementsAnalysisEngine; // NEW: Delegate to Requirements domain
        private readonly IEditDetectionService? _editDetectionService;
        private readonly ILLMLearningService? _learningService;
        private Requirement? _currentRequirement;
        
        // Analysis timer tracking
        private DateTime _analysisStartTime;
        private System.Windows.Threading.DispatcherTimer? _timerUpdateTimer;
        
        // Track if edit window is currently open to prevent multiple instances
        [ObservableProperty]
        private bool _isEditWindowOpen = false;
        
        // Track if inline requirement editing is active
        [ObservableProperty]
        private bool _isEditingRequirement = false;
        
        // Text being edited for requirement description
        [ObservableProperty]
        private string _editingRequirementText = string.Empty;
        
        /// <summary>
        /// Whether the copy button should be hidden (reflects mediator state)
        /// </summary>
        public bool ShouldHideCopyButton => _mediator.HasUnsavedEditingChanges;
        
        [ObservableProperty]
        private string _copyAnalysisButtonText = "Copy Analysis Prompt to Clipboard";
        
        [ObservableProperty]
        private string _analysisElapsedTime = "";
        
        // Analysis state is managed by mediator, not individual ViewModels

        /// <summary>
        /// Gets the quality score of the current requirement's analysis
        /// </summary>
        public int AnalysisQualityScore => CurrentRequirement?.Analysis?.OriginalQualityScore ?? 0;

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

        // Cache functionality moved to Requirements domain

        public TestCaseGenerator_AnalysisVM(ITestCaseGenerationMediator mediator, ILogger<TestCaseGenerator_AnalysisVM> logger, TestCaseEditorApp.MVVM.Domains.Requirements.Services.IRequirementAnalysisService analysisService, IEditDetectionService? editDetectionService = null, ITextGenerationService? llmService = null, ILLMLearningService? learningService = null)
            : base(mediator, logger)
        {
            _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
            _analysisService = analysisService ?? throw new ArgumentNullException(nameof(analysisService));
            _editDetectionService = editDetectionService;
            _llmService = llmService;
            _learningService = learningService;

            // ARCHITECTURAL MIGRATION: Get Requirements domain analysis engine
            _requirementsAnalysisEngine = App.ServiceProvider?.GetService<IRequirementAnalysisEngine>();
            if (_requirementsAnalysisEngine == null)
            {
                logger.LogWarning("[TestCaseGenerator_AnalysisVM] Requirements domain analysis engine not available - falling back to legacy service");
            }
            else
            {
                logger.LogInformation("[TestCaseGenerator_AnalysisVM] Successfully connected to Requirements domain analysis engine");
            }

            // Subscribe to domain events
            _mediator.Subscribe<TestCaseGenerationEvents.RequirementSelected>(OnRequirementSelected);
            _mediator.Subscribe<TestCaseGenerationEvents.RequirementAnalyzed>(OnRequirementAnalyzed);
            _mediator.Subscribe<TestCaseGenerationEvents.WorkflowStateChanged>(OnWorkflowStateChanged);
            _mediator.Subscribe<TestCaseGenerationEvents.RequirementEditStateChanged>(OnEditStateChanged);

            // Subscribe to mediator for analysis updates
            AnalysisMediator.AnalysisUpdated += OnAnalysisUpdated;

            AnalyzeRequirementCommand = new AsyncRelayCommand(AnalyzeRequirementAsync, CanAnalyzeRequirement);
            EditRequirementCommand = new RelayCommand(EditRequirement, CanEditRequirement);
            SaveRequirementCommand = new RelayCommand(SaveRequirementEdit, CanSaveRequirement);
            CancelEditRequirementCommand = new RelayCommand(CancelRequirementEdit);
            CopyAnalysisPromptCommand = new RelayCommand(CopyAnalysisPromptToClipboard, CanExecuteSmartClipboardAction);
            
            // Note: Cache management moved to Requirements domain
            // Note: Clipboard monitoring now handled by RequirementAnalysisViewModel

            Title = "Requirement Analysis";
            // Initial state managed by event handlers when requirements are loaded
        }
        
        // ===== DOMAIN EVENT HANDLERS =====
        
        /// <summary>
        /// Handle requirement selection from mediator
        /// </summary>
        private void OnRequirementSelected(TestCaseGenerationEvents.RequirementSelected e)
        {
            _logger.LogInformation("[AnalysisVM] OnRequirementSelected - Requirement: {Item}, HasAnalysis: {HasAnalysis}, IsAnalyzed: {IsAnalyzed}",
                e.Requirement?.Item ?? "null",
                e.Requirement?.Analysis != null ? "true" : "false",
                e.Requirement?.Analysis?.IsAnalyzed ?? false);
            CurrentRequirement = e.Requirement;
            // Update analysis display from current requirement's analysis state
            UpdateAnalysisPropertiesFromEvent(e.Requirement?.Analysis);
            OnPropertyChanged(nameof(HasAnalysis));
            OnPropertyChanged(nameof(AnalysisQualityScore));
            OnPropertyChanged(nameof(ServiceStatusText));
            
            // Update command states
            ((AsyncRelayCommand)AnalyzeRequirementCommand).NotifyCanExecuteChanged();
            ((RelayCommand)EditRequirementCommand).NotifyCanExecuteChanged();
        }
        
        /// <summary>
        /// Handle requirement analysis completion from mediator
        /// FIXED ARCHITECTURE: Update ViewModel state directly from event data, don't read model state
        /// </summary>
        private void OnRequirementAnalyzed(TestCaseGenerationEvents.RequirementAnalyzed e)
        {
            if (ReferenceEquals(CurrentRequirement, e.Requirement))
            {
                // Ensure we're not in editing mode after analysis completes
                if (IsEditingRequirement)
                {
                    _mediator.CancelEditingRequirement();
                    IsEditingRequirement = false;
                    EditingRequirementText = string.Empty;
                }
                
                // ARCHITECTURAL FIX: Update properties directly from event data
                var analysis = e.Analysis ?? e.Requirement?.Analysis;
                UpdateAnalysisPropertiesFromEvent(analysis);
            }
        }
        
        private void OnEditStateChanged(TestCaseGenerationEvents.RequirementEditStateChanged e)
        {
            // Update copy button visibility when edit state changes
            OnPropertyChanged(nameof(ShouldHideCopyButton));
        }

        private void OnAnalysisUpdated(Requirement requirement)
        {
            _logger.LogDebug("OnAnalysisUpdated fired for: {RequirementId}", requirement.Item);
            _logger.LogDebug("Current requirement: {CurrentId}", CurrentRequirement?.Item);
            _logger.LogDebug("Analysis IsAnalyzed: {IsAnalyzed}, Score: {Score}", 
                requirement.Analysis?.IsAnalyzed, requirement.Analysis?.OriginalQualityScore);
            
            // Only refresh if this is the currently displayed requirement
            if (CurrentRequirement?.Item == requirement.Item)
            {
                _logger.LogDebug("Match found! Updating analysis display for {RequirementId}", requirement.Item);
                System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                {
                    // ARCHITECTURAL FIX: Update properties directly from event data
                    UpdateAnalysisPropertiesFromEvent(requirement.Analysis);
                });
            }
        }

        /// <summary>
        /// ARCHITECTURAL FIX: Update ViewModel properties from event data instead of reading model state.
        /// This implements proper mediator pattern where ViewModels respond to events, not poll state.
        /// </summary>
        private void UpdateAnalysisPropertiesFromEvent(RequirementAnalysis? analysis)
        {
            _logger.LogDebug("[AnalysisVM] Updating analysis properties from event data");

            // Reset editing state when refreshing analysis - default should be read-only view
            if (IsEditingRequirement)
            {
                IsEditingRequirement = false;
                EditingRequirementText = string.Empty;
            }

            if (analysis?.IsAnalyzed == true)
            {
                HasAnalysis = true;
                QualityScore = analysis.OriginalQualityScore; // Show user's original requirement quality
                Issues = analysis.Issues ?? new List<AnalysisIssue>();
                
                // Force a fresh list assignment to ensure UI updates properly
                var newRecommendations = analysis.Recommendations?.ToList() ?? new List<AnalysisRecommendation>();
                Recommendations = newRecommendations;
                
                // Log SuggestedEdit status for debugging
                _logger.LogDebug("[AnalysisVM] Loaded {Count} recommendations", newRecommendations.Count);
                for (int i = 0; i < newRecommendations.Count; i++)
                {
                    var rec = newRecommendations[i];
                    var hasEdit = !string.IsNullOrEmpty(rec.SuggestedEdit);
                    if (!hasEdit)
                    {
                        _logger.LogWarning("[AnalysisVM] Recommendation '{Category}' is missing SuggestedEdit - blue border will not appear!", rec.Category);
                    }
                }
                
                FreeformFeedback = analysis.FreeformFeedback ?? string.Empty;
                AnalysisTimestamp = $"Analyzed on {analysis.Timestamp:MMM d, yyyy 'at' h:mm tt}";
                
                // Check if analysis contains error state
                if (!string.IsNullOrEmpty(analysis.ErrorMessage))
                {
                    _logger.LogWarning("[AnalysisVM] Analysis has ErrorMessage: '{ErrorMessage}' - setting status message", analysis.ErrorMessage);
                    AnalysisStatusMessage = analysis.ErrorMessage;
                }
                else
                {
                    // Clear status message for successful analysis
                    _logger.LogDebug("[AnalysisVM] Analysis successful - clearing status message");
                    AnalysisStatusMessage = string.Empty;
                }
                
                _logger.LogDebug("[AnalysisVM] Analysis properties updated from event - Quality: {Score}, Issues: {IssueCount}", 
                    analysis.OriginalQualityScore, Issues.Count);
            }
            else
            {
                // No analysis or failed analysis
                HasAnalysis = false;
                QualityScore = 0;
                Issues = new List<AnalysisIssue>();
                Recommendations = new List<AnalysisRecommendation>();
                FreeformFeedback = string.Empty;
                AnalysisTimestamp = string.Empty;
                
                // CRITICAL: Always clear status message for no analysis - show clean "Analyze Now" interface
                AnalysisStatusMessage = string.Empty;
                
                _logger.LogDebug("[AnalysisVM] Cleared analysis properties - no valid analysis in event");
            }

            // Notify UI of all relevant property changes
            OnPropertyChanged(nameof(HasAnalysis));
            OnPropertyChanged(nameof(QualityScore));
            OnPropertyChanged(nameof(HasIssues));
            OnPropertyChanged(nameof(HasRecommendations));
            OnPropertyChanged(nameof(HasFreeformFeedback));
            OnPropertyChanged(nameof(HasImprovedRequirement));
            OnPropertyChanged(nameof(ImprovedRequirement));
            OnPropertyChanged(nameof(HasNoAnalysis));
            OnPropertyChanged(nameof(AnalysisStatusMessage));
            
            _logger.LogDebug("[AnalysisVM] UpdateAnalysisPropertiesFromEvent complete - QualityScore: {QualityScore}, HasAnalysis: {HasAnalysis}", QualityScore, HasAnalysis);
        }

        public ICommand AnalyzeRequirementCommand { get; }
        public ICommand EditRequirementCommand { get; }
        public ICommand SaveRequirementCommand { get; }
        public ICommand CancelEditRequirementCommand { get; }
        public ICommand CopyAnalysisPromptCommand { get; }
        public IAsyncRelayCommand? ReAnalyzeCommand { get; private set; }
        
        // Cache management commands moved to Requirements domain

        private bool CanAnalyzeRequirement()
        {
            // Analysis state is now managed by mediator - check mediator state
            return CurrentRequirement != null && !_mediator.IsAnalyzing;
        }

        private bool CanEditRequirement()
        {
            // Edit is disabled during any analysis managed by mediator
            return CurrentRequirement != null && !_mediator.IsAnalyzing && !IsEditWindowOpen;
        }

        private void EditRequirement()
        {
            var requirement = CurrentRequirement;
            if (requirement == null || IsEditingRequirement) return;

            try
            {
                TestCaseEditorApp.Services.Logging.Log.Debug("[AnalysisVM] EditRequirement called - switching to inline edit mode for improved requirement");
                
                // Switch to inline editing mode using the improved requirement text
                // Let mediator handle editing state
                _mediator.StartEditingRequirement(requirement, requirement.Analysis?.ImprovedRequirement ?? string.Empty);
                EditingRequirementText = requirement.Analysis?.ImprovedRequirement ?? string.Empty;
                IsEditingRequirement = true;
                
                // Refresh command states
                ((RelayCommand)EditRequirementCommand).NotifyCanExecuteChanged();
                ((RelayCommand)SaveRequirementCommand).NotifyCanExecuteChanged();
                
                TestCaseEditorApp.Services.Logging.Log.Debug("[AnalysisVM] Switched to inline editing mode for improved requirement");
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, "[AnalysisVM] Error in EditRequirement");
                
                // Reset state on error
                IsEditingRequirement = false;
                ((RelayCommand)EditRequirementCommand).NotifyCanExecuteChanged();
            }
        }

        private void SaveRequirementEdit()
        {
            var requirement = CurrentRequirement;
            if (requirement == null || !IsEditingRequirement || requirement.Analysis == null) return;

            try
            {
                TestCaseEditorApp.Services.Logging.Log.Debug("[AnalysisVM] SaveRequirementEdit called for improved requirement");

                // Update the improved requirement text if it changed
                var newImprovedText = EditingRequirementText?.Trim() ?? string.Empty;
                var originalImprovedText = requirement.Analysis.ImprovedRequirement ?? string.Empty;
                
                if (newImprovedText != originalImprovedText)
                {
                    requirement.Analysis.ImprovedRequirement = newImprovedText;
                    
                    // Publish event that requirement was updated
                    _mediator.PublishEvent(new TestCaseGenerationEvents.RequirementSelected 
                    { 
                        Requirement = requirement, 
                        SelectedBy = "ImprovedRequirementEditor" 
                    });
                    
                    // Mark workspace as dirty
                    _mediator.PublishEvent(new TestCaseGenerationEvents.WorkflowStateChanged 
                    { 
                        PropertyName = "IsDirty", 
                        NewValue = true 
                    });
                    
                    TestCaseEditorApp.Services.Logging.Log.Info($"[AnalysisVM] Improved requirement for {requirement.GlobalId} updated successfully");
                    
                    // Refresh UI properties
                    OnPropertyChanged(nameof(ImprovedRequirement));
                    OnPropertyChanged(nameof(HasImprovedRequirement));
                }
                
                // Let mediator handle editing state
                _mediator.EndEditingRequirement();
                // Exit edit mode
                IsEditingRequirement = false;
                EditingRequirementText = string.Empty;
                
                // Refresh command states
                ((RelayCommand)EditRequirementCommand).NotifyCanExecuteChanged();
                ((RelayCommand)SaveRequirementCommand).NotifyCanExecuteChanged();
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, "[AnalysisVM] Error in SaveRequirementEdit");
            }
        }

        private bool CanSaveRequirement()
        {
            return IsEditingRequirement && !string.IsNullOrWhiteSpace(EditingRequirementText);
        }

        private void CancelRequirementEdit()
        {
            try
            {
                TestCaseEditorApp.Services.Logging.Log.Debug("[AnalysisVM] CancelRequirementEdit called");
                
                // Let mediator handle editing state
                _mediator.CancelEditingRequirement();
                // Exit edit mode without saving
                IsEditingRequirement = false;
                EditingRequirementText = string.Empty;
                
                // Refresh command states
                ((RelayCommand)EditRequirementCommand).NotifyCanExecuteChanged();
                ((RelayCommand)SaveRequirementCommand).NotifyCanExecuteChanged();
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, "[AnalysisVM] Error in CancelRequirementEdit");
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

        // Debug inspection methods removed - functionality moved to Requirements domain

        private async Task AnalyzeRequirementAsync()
        {
            var requirement = CurrentRequirement;
            if (requirement == null) return;

            TestCaseEditorApp.Services.Logging.Log.Debug($"[AnalysisVM] AnalyzeRequirementAsync started");

            try
            {
                // Use Requirements domain analysis engine
                if (_requirementsAnalysisEngine == null)
                {
                    TestCaseEditorApp.Services.Logging.Log.Error("[AnalysisVM] Requirements analysis engine not available");
                    AnalysisStatusMessage = "Analysis service not available";
                    return;
                }
                
                TestCaseEditorApp.Services.Logging.Log.Debug($"[AnalysisVM] Using Requirements domain analysis engine");
                
                var analysis = await _requirementsAnalysisEngine.AnalyzeRequirementAsync(
                    requirement, 
                    progressMessage => {
                        AnalysisStatusMessage = progressMessage;
                        TestCaseEditorApp.Services.Logging.Log.Debug($"[AnalysisVM] Progress: {progressMessage}");
                    });
                    
                requirement.Analysis = analysis;
                bool success = analysis.IsAnalyzed;
                
                if (success)
                {
                    TestCaseEditorApp.Services.Logging.Log.Debug($"[AnalysisVM] Requirements domain analysis completed successfully");
                }
                else
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[AnalysisVM] Requirements domain analysis failed: {analysis.ErrorMessage}");
                }
                
                // Update analysis display from newly updated requirement analysis
                UpdateAnalysisPropertiesFromEvent(requirement.Analysis);

                // Single UI refresh to avoid duplicate displays
                OnPropertyChanged(nameof(Recommendations));
                OnPropertyChanged(nameof(HasAnalysis));
                OnPropertyChanged(nameof(ServiceStatusText)); // Update service status display
                TestCaseEditorApp.Services.Logging.Log.Debug("[AnalysisVM] UI refresh for analysis results");
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, "[AnalysisVM] Error during analysis");
                AnalysisStatusMessage = $"Analysis error: {ex.Message}";
            }
        }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasNoAnalysis))]
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
            
            // Note: Timer management is handled by OnWorkflowStateChanged from mediator
            // to avoid conflicts between local property changes and mediator state
        }

        partial void OnIsEditingRequirementChanged(bool value)
        {
            // Update command states when editing mode changes
            ((RelayCommand)CopyAnalysisPromptCommand).NotifyCanExecuteChanged();
        }

        partial void OnEditingRequirementTextChanged(string value)
        {
            // Notify mediator of text changes - it manages the state
            _mediator.UpdateEditingText(value);
            // Update command states when editing text changes
            ((RelayCommand)CopyAnalysisPromptCommand).NotifyCanExecuteChanged();
        }

        public bool HasIssues => Issues?.Any() == true;
        public bool HasRecommendations => Recommendations?.Any() == true;
        public bool HasFreeformFeedback => !string.IsNullOrWhiteSpace(FreeformFeedback) && 
                                           !IsNoFeedbackPlaceholder(FreeformFeedback);
        
        /// <summary>
        /// Gets the improved requirement text from the analysis
        /// </summary>
        public string? ImprovedRequirement => CurrentRequirement?.Analysis?.ImprovedRequirement;
        
        /// <summary>
        /// Gets whether the analysis contains an improved requirement
        /// </summary>
        public bool HasImprovedRequirement => !string.IsNullOrWhiteSpace(ImprovedRequirement);
        
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

        // Cache management moved to Requirements domain

        // ===== ABSTRACT METHOD IMPLEMENTATIONS =====
        
        protected override bool CanSave() => false; // Analysis doesn't save directly
        protected override async Task SaveAsync() => await Task.CompletedTask;
        protected override bool CanRefresh() => true;
        protected override async Task RefreshAsync()
        {
            // Refresh current requirement analysis from current state
            UpdateAnalysisPropertiesFromEvent(CurrentRequirement?.Analysis);
            await Task.CompletedTask;
        }
        protected override bool CanCancel() => IsAnalyzing;
        
        // ===== TIMER MANAGEMENT =====
        
        private void OnWorkflowStateChanged(TestCaseGenerationEvents.WorkflowStateChanged e)
        {
            if (e.PropertyName == nameof(IsAnalyzing))
            {
                if (e.NewValue is bool isAnalyzingValue)
                {
                    // Update our local IsAnalyzing field to sync with mediator state
                    IsAnalyzing = isAnalyzingValue;
                    
                    if (isAnalyzingValue)
                    {
                        StartAnalysisTimer();
                    }
                    else
                    {
                        StopAnalysisTimer();
                    }
                }
            }
        }
        
        private void StartAnalysisTimer()
        {
            _analysisStartTime = DateTime.Now;
            AnalysisElapsedTime = "";
            
            _timerUpdateTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500) // Update every 500ms for smooth display
            };
            
            _timerUpdateTimer.Tick += (s, e) =>
            {
                var elapsed = DateTime.Now - _analysisStartTime;
                AnalysisElapsedTime = $"{elapsed.TotalSeconds:F0}s";
            };
            
            _timerUpdateTimer.Start();
        }
        
        private void StopAnalysisTimer()
        {
            if (_timerUpdateTimer != null)
            {
                _timerUpdateTimer.Stop();
                _timerUpdateTimer = null;
            }
            
            // Show final elapsed time for a moment
            if (_analysisStartTime != default)
            {
                var totalElapsed = DateTime.Now - _analysisStartTime;
                AnalysisElapsedTime = $"Completed in {totalElapsed.TotalSeconds:F0}s";
                
                // Clear after 3 seconds
                var clearTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(3)
                };
                clearTimer.Tick += (s, e) =>
                {
                    AnalysisElapsedTime = "";
                    clearTimer.Stop();
                };
                clearTimer.Start();
            }
        }

        /// <summary>
        /// Determines if the Copy Analysis Prompt command can execute
        /// </summary>
        private bool CanExecuteSmartClipboardAction()
        {
            return CurrentRequirement != null;
        }

        // Clipboard monitoring functionality moved to RequirementAnalysisViewModel

        // External analysis processing moved to RequirementAnalysisViewModel

        // Text extraction methods moved to RequirementAnalysisViewModel

        /// <summary>
        /// Extract and update analysis results from external LLM response
        /// </summary>
        private void UpdateAnalysisFromExternalResponse(string response)
        {
            try
            {
                _logger.LogInformation("[AnalysisVM] Extracting analysis results from external LLM response");

                // Extract quality score
                var qualityScore = ExtractQualityScore(response);
                if (qualityScore.HasValue)
                {
                    QualityScore = qualityScore.Value;
                    _logger.LogInformation("[AnalysisVM] Updated quality score from external LLM: {Score}", qualityScore.Value);
                }

                // Extract issues
                var extractedIssues = ExtractIssues(response);
                if (extractedIssues.Count > 0)
                {
                    Issues = extractedIssues;
                    _logger.LogInformation("[AnalysisVM] Successfully extracted {Count} issues from external LLM", extractedIssues.Count);
                }
                else
                {
                    _logger.LogInformation("[AnalysisVM] No valid issues found in external LLM response (all issues must have actionable fixes)");
                }

                // Extract recommendations
                var extractedRecommendations = ExtractRecommendations(response);
                if (extractedRecommendations.Count > 0)
                {
                    Recommendations = extractedRecommendations;
                    _logger.LogInformation("[AnalysisVM] Updated {Count} recommendations from external LLM", extractedRecommendations.Count);
                }

                // Extract overall assessment as freeform feedback
                var assessment = ExtractOverallAssessment(response);
                if (!string.IsNullOrWhiteSpace(assessment))
                {
                    FreeformFeedback = $"External LLM Analysis:\n\n{assessment}";
                    _logger.LogInformation("[AnalysisVM] Updated freeform feedback from external LLM");
                }

                // Update UI state
                HasAnalysis = true;
                OnPropertyChanged(nameof(HasIssues));
                OnPropertyChanged(nameof(HasRecommendations));
                OnPropertyChanged(nameof(HasFreeformFeedback));
                
                System.Diagnostics.Debug.WriteLine($"[AnalysisVM] ✅ Analysis updated - Score: {QualityScore}, Issues: {Issues.Count}, Recs: {Recommendations.Count}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[AnalysisVM] Failed to extract analysis from external LLM response");
                System.Diagnostics.Debug.WriteLine($"[AnalysisVM] ❌ Error extracting analysis: {ex.Message}");
            }
        }

        /// <summary>
        /// Extract quality score from external LLM response
        /// </summary>
        private int? ExtractQualityScore(string response)
        {
            try
            {
                var lines = response.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    var upper = trimmed.ToUpperInvariant();
                    
                    if (upper.StartsWith("QUALITY SCORE"))
                    {
                        // Look for number pattern
                        var match = System.Text.RegularExpressions.Regex.Match(trimmed, @"\b(\d+)\b");
                        if (match.Success && int.TryParse(match.Groups[1].Value, out var score))
                        {
                            return Math.Clamp(score, 0, 10); // Normalize to 0-10 scale
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[AnalysisVM] Error extracting quality score");
            }
            return null;
        }

        /// <summary>
        /// Extract issues from external LLM response
        /// </summary>
        private List<AnalysisIssue> ExtractIssues(string response)
        {
            var issues = new List<AnalysisIssue>();
            var failedLines = new List<string>();
            
            try
            {
                var lines = response.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                bool inIssuesSection = false;
                
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    var upper = trimmed.ToUpperInvariant();
                    
                    // Check if we're entering issues section
                    if (upper.StartsWith("ISSUES FOUND") || upper.StartsWith("ISSUES:"))
                    {
                        inIssuesSection = true;
                        continue;
                    }
                    
                    // Check if we're leaving issues section
                    if (inIssuesSection && (upper.StartsWith("STRENGTHS") || upper.StartsWith("IMPROVED") || 
                                          upper.StartsWith("RECOMMENDATIONS") || upper.StartsWith("OVERALL")))
                    {
                        break;
                    }
                    
                    // Extract issue if we're in the section
                    if (inIssuesSection && (trimmed.StartsWith("*") || trimmed.StartsWith("-") || trimmed.StartsWith("•")))
                    {
                        var issue = ParseIssueFromLine(trimmed);
                        if (issue != null)
                        {
                            issues.Add(issue);
                        }
                        else
                        {
                            // Track lines that looked like issues but couldn't be parsed
                            failedLines.Add(trimmed);
                        }
                    }
                }
                
                // Report parsing results to user
                if (failedLines.Count > 0)
                {
                    var failedText = string.Join("\n", failedLines.Take(3)); // Show first 3 failed lines
                    var moreCount = failedLines.Count > 3 ? $" (and {failedLines.Count - 3} more)" : "";
                    
                    _logger.LogWarning("[AnalysisVM] Failed to parse {Count} issue lines from external LLM response. " +
                                      "Lines must contain actionable fixes to be processed. Failed lines: {FailedLines}{More}", 
                                      failedLines.Count, failedText, moreCount);
                    
                    // Add to freeform feedback so user sees the issue
                    var existingFeedback = FreeformFeedback ?? "";
                    var parseWarning = $"\n⚠️ External LLM Parser Note: {failedLines.Count} issue line(s) were skipped because they didn't contain actionable fixes. " +
                                      $"External LLMs must provide specific 'Fix:' descriptions for each issue.\n\nSkipped lines:\n{failedText}{moreCount}";
                    
                    FreeformFeedback = (existingFeedback + parseWarning).Trim();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[AnalysisVM] Error extracting issues");
            }
            
            return issues;
        }

        /// <summary>
        /// Parse individual issue from line
        /// </summary>
        private AnalysisIssue? ParseIssueFromLine(string line)
        {
            try
            {
                // Remove leading asterisk and trim
                var content = line.TrimStart('*', '-', '•').Trim();
                
                // Try multiple parsing patterns for external LLM flexibility
                
                // Pattern 1: "Category Issue (Priority): Description | Fix: Solution"
                var match1 = System.Text.RegularExpressions.Regex.Match(content, 
                    @"^(.+?)\s+Issue\s*\((.+?)\):\s*(.+?)(?:\s*\|\s*Fix:\s*(.+))?$", 
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                
                if (match1.Success && !string.IsNullOrWhiteSpace(match1.Groups[4].Value))
                {
                    return CreateIssue(match1.Groups[1].Value, match1.Groups[2].Value, 
                                     match1.Groups[3].Value, match1.Groups[4].Value);
                }
                
                // Pattern 2: "Category (Priority): Description. Fix: Solution" 
                var match2 = System.Text.RegularExpressions.Regex.Match(content,
                    @"^(.+?)\s*\((.+?)\):\s*(.+?)\.?\s*(?:Fix|Fixed|Resolution|Resolved):\s*(.+)$",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                
                if (match2.Success && !string.IsNullOrWhiteSpace(match2.Groups[4].Value))
                {
                    return CreateIssue(match2.Groups[1].Value, match2.Groups[2].Value,
                                     match2.Groups[3].Value, match2.Groups[4].Value);
                }
                
                // Pattern 3: "Description - Fixed: Solution (Category)"
                var match3 = System.Text.RegularExpressions.Regex.Match(content,
                    @"^(.+?)\s*-\s*(?:Fixed|Resolved|Fix):\s*(.+?)\s*\((.+?)\)$",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                
                if (match3.Success && !string.IsNullOrWhiteSpace(match3.Groups[2].Value))
                {
                    return CreateIssue(match3.Groups[3].Value, "Medium", 
                                     match3.Groups[1].Value, match3.Groups[2].Value);
                }
                
                // Pattern 4: Look for any "Fix:" or "Fixed:" anywhere in the line
                var fixMatch = System.Text.RegularExpressions.Regex.Match(content,
                    @"(.+?)(?:Fix|Fixed|Resolution|Resolved):\s*(.+?)(?:\s*\((.+?)\))?$",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                
                if (fixMatch.Success && !string.IsNullOrWhiteSpace(fixMatch.Groups[2].Value))
                {
                    var category = fixMatch.Groups[3].Value;
                    if (string.IsNullOrWhiteSpace(category))
                    {
                        // Try to extract category from description
                        var desc = fixMatch.Groups[1].Value.Trim();
                        category = ExtractCategoryFromDescription(desc);
                    }
                    return CreateIssue(category, "Medium", fixMatch.Groups[1].Value, fixMatch.Groups[2].Value);
                }
                
                // If none of the patterns match or no fix is found, reject the line entirely
                // LLM must provide actionable fixes in recognizable format
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[AnalysisVM] Error parsing issue from line: {Line}", line);
            }
            
            return null;
        }

        /// <summary>
        /// Helper to create AnalysisIssue with consistent formatting
        /// </summary>
        private AnalysisIssue CreateIssue(string category, string priority, string description, string fix)
        {
            return new AnalysisIssue
            {
                Category = category.Trim(),
                Severity = ParseSeverityString(priority.Trim()),
                Description = $"{category.Trim()} Issue: {description.Trim()}",
                Fix = fix.Trim()
            };
        }

        /// <summary>
        /// Extract likely category from description text when not explicitly provided
        /// </summary>
        private string ExtractCategoryFromDescription(string description)
        {
            var lowerDesc = description.ToLowerInvariant();
            
            if (lowerDesc.Contains("unclear") || lowerDesc.Contains("ambiguous") || lowerDesc.Contains("vague"))
                return "Clarity";
            if (lowerDesc.Contains("test") || lowerDesc.Contains("verify") || lowerDesc.Contains("measurable"))
                return "Testability";
            if (lowerDesc.Contains("missing") || lowerDesc.Contains("incomplete") || lowerDesc.Contains("specify"))
                return "Completeness";
            if (lowerDesc.Contains("multiple") || lowerDesc.Contains("combined") || lowerDesc.Contains("split"))
                return "Atomicity";
            if (lowerDesc.Contains("implement") || lowerDesc.Contains("actionable") || lowerDesc.Contains("abstract"))
                return "Actionability";
            if (lowerDesc.Contains("consistent") || lowerDesc.Contains("terminology") || lowerDesc.Contains("format"))
                return "Consistency";
            
            return "General";
        }

        /// <summary>
        /// Parse severity from priority text
        /// </summary>
        private string ParseSeverityString(string priority)
        {
            var upper = priority.ToUpperInvariant();
            return upper switch
            {
                var p when p.Contains("HIGH") || p.Contains("CRITICAL") => "High",
                var p when p.Contains("MEDIUM") || p.Contains("MODERATE") => "Medium",
                var p when p.Contains("LOW") || p.Contains("MINOR") => "Low",
                _ => "Medium"
            };
        }

        /// <summary>
        /// Extract recommendations from external LLM response
        /// </summary>
        private List<AnalysisRecommendation> ExtractRecommendations(string response)
        {
            var recommendations = new List<AnalysisRecommendation>();
            
            try
            {
                var lines = response.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                bool inRecsSection = false;
                
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    var upper = trimmed.ToUpperInvariant();
                    
                    // Check if we're entering recommendations section
                    if (upper.StartsWith("RECOMMENDATIONS") || upper.StartsWith("RECOMMENDATIONS:"))
                    {
                        inRecsSection = true;
                        continue;
                    }
                    
                    // Check if we're leaving recommendations section
                    if (inRecsSection && (upper.StartsWith("HALLUCINATION") || upper.StartsWith("OVERALL") || 
                                        upper.StartsWith("ASSESSMENT")))
                    {
                        break;
                    }
                    
                    // Extract recommendation if we're in the section
                    if (inRecsSection && trimmed.StartsWith("*"))
                    {
                        var recommendation = ParseRecommendationFromLine(trimmed);
                        if (recommendation != null)
                        {
                            recommendations.Add(recommendation);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[AnalysisVM] Error extracting recommendations");
            }
            
            return recommendations;
        }

        /// <summary>
        /// Parse individual recommendation from line
        /// </summary>
        private AnalysisRecommendation? ParseRecommendationFromLine(string line)
        {
            try
            {
                // Remove leading asterisk and trim
                var content = line.TrimStart('*').Trim();
                
                // Look for pattern: "Category: Description | Rationale: Explanation"
                var match = System.Text.RegularExpressions.Regex.Match(content, 
                    @"^Category:\s*(.+?)\s*\|\s*Description:\s*(.+?)(?:\s*\|\s*Rationale:\s*(.+))?$", 
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                
                if (match.Success)
                {
                    var category = match.Groups[1].Value.Trim();
                    var description = match.Groups[2].Value.Trim();
                    var rationale = match.Groups[3].Value.Trim();
                    
                    return new AnalysisRecommendation
                    {
                        Category = category,
                        Description = $"{description}. {rationale}".Trim(' ', '.'),
                        SuggestedEdit = string.IsNullOrWhiteSpace(rationale) ? null : rationale
                    };
                }
                
                // Fallback: treat entire line as description
                if (content.Length > 10)
                {
                    return new AnalysisRecommendation
                    {
                        Category = "General",
                        Description = content,
                        SuggestedEdit = null
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[AnalysisVM] Error parsing recommendation from line: {Line}", line);
            }
            
            return null;
        }

        /// <summary>
        /// Extract overall assessment from external LLM response
        /// </summary>
        private string ExtractOverallAssessment(string response)
        {
            try
            {
                var lines = response.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                bool inAssessmentSection = false;
                var assessmentLines = new List<string>();
                
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    var upper = trimmed.ToUpperInvariant();
                    
                    // Check if we're entering assessment section
                    if (upper.StartsWith("OVERALL ASSESSMENT") || upper.StartsWith("OVERALL:"))
                    {
                        inAssessmentSection = true;
                        
                        // Check if assessment is on same line after colon
                        var colonIndex = trimmed.IndexOf(':');
                        if (colonIndex >= 0 && colonIndex < trimmed.Length - 1)
                        {
                            var sameLine = trimmed.Substring(colonIndex + 1).Trim();
                            if (!string.IsNullOrWhiteSpace(sameLine))
                            {
                                assessmentLines.Add(sameLine);
                            }
                        }
                        continue;
                    }
                    
                    // Check if we're leaving assessment section
                    if (inAssessmentSection && (upper.StartsWith("HALLUCINATION") || 
                                              string.IsNullOrWhiteSpace(trimmed)))
                    {
                        break;
                    }
                    
                    // Add assessment content
                    if (inAssessmentSection && !string.IsNullOrWhiteSpace(trimmed))
                    {
                        assessmentLines.Add(trimmed);
                    }
                }
                
                return assessmentLines.Count > 0 ? string.Join(" ", assessmentLines).Trim() : string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[AnalysisVM] Error extracting overall assessment");
                return string.Empty;
            }
        }

        /// <summary>
        /// Build learning prompt from external LLM interaction
        /// </summary>
        private string BuildLearningPrompt(Requirement requirement, string externalResponse)
        {
            var learningBuilder = new StringBuilder();
            
            learningBuilder.AppendLine("=== EXTERNAL LLM LEARNING DATA ===");
            learningBuilder.AppendLine($"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            learningBuilder.AppendLine($"Requirement ID: {requirement.Item}");
            learningBuilder.AppendLine();
            
            learningBuilder.AppendLine("ORIGINAL REQUIREMENT:");
            learningBuilder.AppendLine(requirement.Description);
            learningBuilder.AppendLine();
            
            learningBuilder.AppendLine("ANYTHINGLM ANALYSIS:");
            learningBuilder.AppendLine(EditingRequirementText);
            learningBuilder.AppendLine();
            
            learningBuilder.AppendLine("EXTERNAL LLM ANALYSIS:");
            learningBuilder.AppendLine(externalResponse);
            learningBuilder.AppendLine();
            
            learningBuilder.AppendLine("LEARNING CONTEXT:");
            learningBuilder.AppendLine("This represents a comparison between AnythingLLM analysis and external LLM analysis.");
            learningBuilder.AppendLine("Use this to improve future requirement analysis accuracy and consistency.");
            
            return learningBuilder.ToString();
        }

        /// <summary>
        /// Creates comprehensive analysis prompt for external LLM, including:
        /// - Original requirement text
        /// - AnythingLLM's refined version
        /// - Complete system instructions
        /// - Context for external analysis
        /// </summary>
        private void CopyAnalysisPromptToClipboard()
        {
            try
            {
                var requirement = CurrentRequirement;
                if (requirement == null)
                {
                    _logger.LogWarning("Cannot copy analysis prompt - no current requirement");
                    return;
                }

                var sb = new StringBuilder();
                
                // Header
                sb.AppendLine("=== EXTERNAL LLM ANALYSIS REQUEST ===");
                sb.AppendLine();
                sb.AppendLine("Please analyze this requirement using the methodology shown below.");
                sb.AppendLine("Compare your analysis with the AnythingLLM refined version included.");
                sb.AppendLine();
                
                // Original requirement
                sb.AppendLine("ORIGINAL REQUIREMENT:");
                sb.AppendLine("=" + new string('=', 50));
                sb.AppendLine($"ID: {requirement.Item}");
                sb.AppendLine($"Name: {requirement.Name}");
                sb.AppendLine($"Description: {requirement.Description}");
                
                // Add supplemental tables if available
                if (requirement.Tables?.Count > 0)
                {
                    sb.AppendLine("Supplemental Tables:");
                    foreach (var table in requirement.Tables)
                    {
                        sb.AppendLine($"### {table.EditableTitle}");
                        if (table.Table?.Count > 0)
                        {
                            foreach (var row in table.Table)
                            {
                                sb.AppendLine("| " + string.Join(" | ", row) + " |");
                            }
                        }
                        sb.AppendLine();
                    }
                }
                
                // Add loose content if available
                if (requirement.LooseContent?.Paragraphs?.Count > 0)
                {
                    sb.AppendLine("Supplemental Paragraphs:");
                    foreach (var para in requirement.LooseContent.Paragraphs)
                    {
                        sb.AppendLine($"- {para}");
                    }
                    sb.AppendLine();
                }
                
                if (requirement.LooseContent?.Tables?.Count > 0)
                {
                    sb.AppendLine("Supplemental Loose Tables:");
                    foreach (var table in requirement.LooseContent.Tables)
                    {
                        sb.AppendLine($"### {table.EditableTitle}");
                        if (table.Rows?.Count > 0)
                        {
                            foreach (var row in table.Rows)
                            {
                                sb.AppendLine("| " + string.Join(" | ", row) + " |");
                            }
                        }
                        sb.AppendLine();
                    }
                }
                
                sb.AppendLine();
                
                // AnythingLLM refined version
                sb.AppendLine("ANYTHINGLM REFINED VERSION:");
                sb.AppendLine("=" + new string('=', 50));
                var refinedText = EditingRequirementText?.Trim() ?? "No refined version available";
                sb.AppendLine(refinedText);
                sb.AppendLine();
                
                // Complete analysis methodology
                sb.AppendLine("ANALYSIS METHODOLOGY & SYSTEM INSTRUCTIONS:");
                sb.AppendLine("=" + new string('=', 50));
                sb.AppendLine();
                sb.AppendLine(TestCaseEditorApp.Services.AnythingLLMService.GetOptimalSystemPrompt());
                sb.AppendLine();
                
                // Instructions for external LLM
                sb.AppendLine("=" + new string('=', 50));
                sb.AppendLine("INSTRUCTIONS FOR YOUR ANALYSIS:");
                sb.AppendLine();
                sb.AppendLine("1. Compare the original requirement with the AnythingLLM refined version");
                sb.AppendLine("2. Apply the analysis methodology above to create your own refined version");
                sb.AppendLine("3. Identify any differences in approach or interpretation");
                sb.AppendLine("4. Provide your assessment of the requirement's quality and clarity");
                sb.AppendLine("5. Suggest improvements or highlight potential issues");
                sb.AppendLine();
                sb.AppendLine("Please return your analysis in a structured format showing:");
                sb.AppendLine("- Your refined requirement text");
                sb.AppendLine("- Comparison with AnythingLLM's version");
                sb.AppendLine("- Quality assessment and recommendations");
                sb.AppendLine();
                sb.AppendLine("Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

                var analysisPrompt = sb.ToString();
                Clipboard.SetText(analysisPrompt);
                
                // Note: Clipboard monitoring for external responses now handled by RequirementAnalysisViewModel
                
                // Log success
                _logger.LogInformation("[AnalysisVM] Generated external LLM analysis prompt for requirement {RequirementId}", requirement.Item);
                
                // Provide immediate feedback via debug output (visible in development)
                System.Diagnostics.Debug.WriteLine("[AnalysisVM] ✅ Analysis prompt copied to clipboard");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[AnalysisVM] Failed to generate external LLM analysis prompt");
                
                // Provide immediate error feedback
                System.Diagnostics.Debug.WriteLine($"[AnalysisVM] ❌ Error copying analysis prompt: {ex.Message}");
            }
        }

        protected override void Cancel()
        {
            // TODO: Cancel analysis operation if running
        }
        
        public override void Dispose()
        {
            // Note: Clipboard monitoring timer disposed by RequirementAnalysisViewModel
            
            _timerUpdateTimer?.Stop();
            _timerUpdateTimer = null;
            
            base.Dispose();
        }
    }
}