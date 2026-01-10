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
using TestCaseEditorApp.MVVM.ViewModels;
using TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Mediators;
using TestCaseEditorApp.MVVM.Events;
using Microsoft.Extensions.Logging;

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
        private readonly IRequirementAnalysisService _analysisService;
        private readonly IEditDetectionService? _editDetectionService;
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
        
        [ObservableProperty]
        private string _analysisElapsedTime = "";
        
        // Analysis state is managed by mediator, not individual ViewModels

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

        /// <summary>
        /// Cache statistics for display in UI
        /// </summary>
        public RequirementAnalysisCache.CacheStatistics? CacheStatistics => _analysisService.CacheStatistics;

        /// <summary>
        /// Human-readable cache performance summary
        /// </summary>
        public string CachePerformanceText
        {
            get
            {
                var stats = CacheStatistics;
                if (stats == null) return "Cache: Disabled";

                if (stats.TotalRequests == 0)
                    return "Cache: No requests yet";

                return $"Cache: {stats.CacheHits}/{stats.TotalRequests} hits ({stats.HitRate:F1}%), {stats.TotalTimeSaved.TotalSeconds:F0}s saved";
            }
        }

        public TestCaseGenerator_AnalysisVM(ITestCaseGenerationMediator mediator, ILogger<TestCaseGenerator_AnalysisVM> logger, IRequirementAnalysisService analysisService, IEditDetectionService? editDetectionService = null, ITextGenerationService? llmService = null)
            : base(mediator, logger)
        {
            _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
            _analysisService = analysisService ?? throw new ArgumentNullException(nameof(analysisService));
            _editDetectionService = editDetectionService;
            _llmService = llmService;

            // Subscribe to domain events
            _mediator.Subscribe<TestCaseGenerationEvents.RequirementSelected>(OnRequirementSelected);
            _mediator.Subscribe<TestCaseGenerationEvents.RequirementAnalyzed>(OnRequirementAnalyzed);
            _mediator.Subscribe<TestCaseGenerationEvents.WorkflowStateChanged>(OnWorkflowStateChanged);

            // Subscribe to mediator for analysis updates
            AnalysisMediator.AnalysisUpdated += OnAnalysisUpdated;

            AnalyzeRequirementCommand = new AsyncRelayCommand(AnalyzeRequirementAsync, CanAnalyzeRequirement);
            EditRequirementCommand = new RelayCommand(EditRequirement, CanEditRequirement);
            SaveRequirementCommand = new RelayCommand(SaveRequirementEdit, CanSaveRequirement);
            CancelEditRequirementCommand = new RelayCommand(CancelRequirementEdit);
            
            // Cache management commands
            ClearCacheCommand = new RelayCommand(ClearAnalysisCache, () => _analysisService.CacheStatistics?.TotalEntries > 0);
            InvalidateCurrentCacheCommand = new RelayCommand(InvalidateCurrentCache, () => CurrentRequirement != null);

            Title = "Requirement Analysis";
            // Initial load
            RefreshAnalysisDisplay();
            
            // TODO: Remove after testing - Create dummy data for testing Edit functionality
            CreateDummyRequirementForTesting();
        }
        
        /// <summary>
        /// Creates dummy requirement with analysis data for testing Edit functionality.
        /// TODO: Remove this method after Edit testing is complete.
        /// </summary>
        private void CreateDummyRequirementForTesting()
        {
            var dummyRequirement = new Requirement
            {
                Item = "TEST-REQ-001",
                Name = "Test Requirement for Edit Testing",
                Description = "This is a dummy requirement created for testing the Edit button functionality. It contains some sample text that can be edited to verify the requirement editor is working properly.",
                Analysis = new RequirementAnalysis
                {
                    IsAnalyzed = true,
                    QualityScore = 7,
                    Timestamp = DateTime.Now,
                    Issues = new List<AnalysisIssue>
                    {
                        new AnalysisIssue
                        {
                            Category = "Clarity",
                            Severity = "Medium",
                            Description = "Some terms could be more specific"
                        },
                        new AnalysisIssue
                        {
                            Category = "Completeness",
                            Severity = "Low", 
                            Description = "Consider adding acceptance criteria"
                        }
                    },
                    Recommendations = new List<AnalysisRecommendation>
                    {
                        new AnalysisRecommendation
                        {
                            Category = "Clarity",
                            Description = "Define technical terms more explicitly",
                            SuggestedEdit = "Replace 'some sample text' with specific functional requirements"
                        }
                    },
                    FreeformFeedback = "Overall good structure but could benefit from more detailed specifications.",
                    ImprovedRequirement = "This is an enhanced version of the dummy requirement created for testing the Edit button functionality. It contains more detailed sample text that demonstrates the improved requirement feature and can be edited to verify the requirement editor is working properly with proper technical specifications."
                }
            };
            
            // Set as current requirement
            _currentRequirement = dummyRequirement;
            OnPropertyChanged(nameof(CurrentRequirement));
            OnPropertyChanged(nameof(HasAnalysis));
            OnPropertyChanged(nameof(AnalysisQualityScore));
            
            // Refresh analysis display
            RefreshAnalysisDisplay();
            
            // Update command states
            ((AsyncRelayCommand)AnalyzeRequirementCommand).NotifyCanExecuteChanged();
            ((RelayCommand)EditRequirementCommand).NotifyCanExecuteChanged();
            ((RelayCommand)InvalidateCurrentCacheCommand).NotifyCanExecuteChanged();
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
            OnPropertyChanged(nameof(CacheStatistics));
            OnPropertyChanged(nameof(CachePerformanceText));
            OnPropertyChanged(nameof(ServiceStatusText));
            
            // Update command states
            ((AsyncRelayCommand)AnalyzeRequirementCommand).NotifyCanExecuteChanged();
            ((RelayCommand)EditRequirementCommand).NotifyCanExecuteChanged();
            ((RelayCommand)InvalidateCurrentCacheCommand).NotifyCanExecuteChanged();
        }
        
        /// <summary>
        /// Handle requirement analysis completion from mediator
        /// </summary>
        private void OnRequirementAnalyzed(TestCaseGenerationEvents.RequirementAnalyzed e)
        {
            if (ReferenceEquals(CurrentRequirement, e.Requirement))
            {
                // Ensure we're not in editing mode after analysis completes
                if (IsEditingRequirement)
                {
                    IsEditingRequirement = false;
                    EditingRequirementText = string.Empty;
                }
                
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
        public ICommand SaveRequirementCommand { get; }
        public ICommand CancelEditRequirementCommand { get; }
        public IAsyncRelayCommand? ReAnalyzeCommand { get; private set; }
        
        // Cache management commands
        public ICommand ClearCacheCommand { get; }
        public ICommand InvalidateCurrentCacheCommand { get; }

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
                    
                    // Check for learning feedback if we have significant changes
                    _ = CheckForLearningFeedbackAsync(originalImprovedText, newImprovedText, "improved requirement");
                    
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

        /// <summary>
        /// Check if user edit should trigger learning feedback
        /// </summary>
        private async System.Threading.Tasks.Task CheckForLearningFeedbackAsync(string originalText, string editedText, string context)
        {
            try
            {
                // Only check if we have edit detection service
                if (_editDetectionService == null)
                    return;

                // Skip if either text is empty
                if (string.IsNullOrWhiteSpace(originalText) || string.IsNullOrWhiteSpace(editedText))
                    return;

                // Trigger learning feedback detection
                await _editDetectionService.ProcessTextEditAsync(originalText, editedText, context);
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, "[AnalysisVM] Error checking for learning feedback");
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

            TestCaseEditorApp.Services.Logging.Log.Debug($"[AnalysisVM] AnalyzeRequirementAsync started - delegating to mediator");

            try
            {
                // Delegate to mediator which manages all analysis state and coordination
                var success = await _mediator.AnalyzeRequirementAsync(requirement);
                
                if (success)
                {
                    TestCaseEditorApp.Services.Logging.Log.Debug($"[AnalysisVM] Analysis completed successfully via mediator");
                }
                else
                {
                    TestCaseEditorApp.Services.Logging.Log.Warn($"[AnalysisVM] Analysis failed via mediator");
                }

                // Refresh display - requirement.Analysis will have been updated by mediator
                RefreshAnalysisDisplay();

                // Single UI refresh to avoid duplicate displays
                OnPropertyChanged(nameof(Recommendations));
                OnPropertyChanged(nameof(HasAnalysis));
                OnPropertyChanged(nameof(ServiceStatusText)); // Update service status display
                OnPropertyChanged(nameof(CacheStatistics)); // Update cache statistics
                OnPropertyChanged(nameof(CachePerformanceText)); // Update cache performance text
                TestCaseEditorApp.Services.Logging.Log.Debug("[AnalysisVM] UI refresh for analysis results");
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, "[AnalysisVM] Error delegating analysis to mediator");
            }
        }

        private void RefreshAnalysisDisplay()
        {
            var analysis = CurrentRequirement?.Analysis;
            
            TestCaseEditorApp.Services.Logging.Log.Info($"[AnalysisVM] RefreshAnalysisDisplay called. CurrentReq: {CurrentRequirement?.Item}, HasAnalysis: {analysis?.IsAnalyzed}, Score: {analysis?.QualityScore}");

            // Reset editing state when refreshing analysis - default should be read-only view
            if (IsEditingRequirement)
            {
                IsEditingRequirement = false;
                EditingRequirementText = string.Empty;
            }

            if (analysis?.IsAnalyzed == true)
            {
                HasAnalysis = true;
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
            }

            // Ensure all analysis properties are notified for UI binding
            OnPropertyChanged(nameof(QualityScore));
            OnPropertyChanged(nameof(HasIssues));
            OnPropertyChanged(nameof(HasRecommendations));
            OnPropertyChanged(nameof(HasFreeformFeedback));
            OnPropertyChanged(nameof(HasImprovedRequirement));
            OnPropertyChanged(nameof(ImprovedRequirement));
            OnPropertyChanged(nameof(HasNoAnalysis));
            
            TestCaseEditorApp.Services.Logging.Log.Debug($"[AnalysisVM] RefreshAnalysisDisplay complete - QualityScore: {QualityScore}, HasAnalysis: {HasAnalysis}");
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
            
            // Note: Timer management is handled by OnWorkflowStateChanged from mediator
            // to avoid conflicts between local property changes and mediator state
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

        // ===== CACHE MANAGEMENT =====
        
        private void ClearAnalysisCache()
        {
            try
            {
                _analysisService.ClearAnalysisCache();
                OnPropertyChanged(nameof(CacheStatistics));
                OnPropertyChanged(nameof(CachePerformanceText));
                
                // Update command states
                ((RelayCommand)ClearCacheCommand).NotifyCanExecuteChanged();
                
                TestCaseEditorApp.Services.Logging.Log.Info("[AnalysisVM] User cleared analysis cache");
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, "[AnalysisVM] Error clearing cache");
            }
        }
        
        private void InvalidateCurrentCache()
        {
            try
            {
                var requirement = CurrentRequirement;
                if (requirement?.GlobalId != null)
                {
                    _analysisService.InvalidateCache(requirement.GlobalId);
                    OnPropertyChanged(nameof(CacheStatistics));
                    OnPropertyChanged(nameof(CachePerformanceText));
                    
                    TestCaseEditorApp.Services.Logging.Log.Info($"[AnalysisVM] User invalidated cache for requirement {requirement.GlobalId}");
                }
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, "[AnalysisVM] Error invalidating current cache");
            }
        }

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
        protected override void Cancel()
        {
            // TODO: Cancel analysis operation if running
        }
    }
}