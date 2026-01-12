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
        
        /// <summary>
        /// Whether the copy button should be hidden (reflects mediator state)
        /// </summary>
        public bool ShouldHideCopyButton => _mediator.HasUnsavedEditingChanges;
        
        // Clipboard monitoring for external LLM workflow
        private System.Windows.Threading.DispatcherTimer? _clipboardMonitorTimer;
        private string _lastClipboardContent = string.Empty;
        private string _copiedPromptHash = string.Empty;
        
        [ObservableProperty]
        private string _copyAnalysisButtonText = "Copy Analysis Prompt to Clipboard";
        
        [ObservableProperty]
        private bool _isWaitingForExternalResponse = false;
        
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
            _mediator.Subscribe<TestCaseGenerationEvents.RequirementEditStateChanged>(OnEditStateChanged);

            // Subscribe to mediator for analysis updates
            AnalysisMediator.AnalysisUpdated += OnAnalysisUpdated;

            AnalyzeRequirementCommand = new AsyncRelayCommand(AnalyzeRequirementAsync, CanAnalyzeRequirement);
            EditRequirementCommand = new RelayCommand(EditRequirement, CanEditRequirement);
            SaveRequirementCommand = new RelayCommand(SaveRequirementEdit, CanSaveRequirement);
            CancelEditRequirementCommand = new RelayCommand(CancelRequirementEdit);
            CopyAnalysisPromptCommand = new RelayCommand(ExecuteSmartClipboardAction, CanExecuteSmartClipboardAction);
            
            // Start clipboard monitoring for external LLM workflow
            StartClipboardMonitoring();
            
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
                    _mediator.CancelEditingRequirement();
                    IsEditingRequirement = false;
                    EditingRequirementText = string.Empty;
                }
                
                RefreshAnalysisDisplay();
                OnPropertyChanged(nameof(HasAnalysis));
                OnPropertyChanged(nameof(AnalysisQualityScore));
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
        public ICommand CopyAnalysisPromptCommand { get; }
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

        /// <summary>
        /// Determines if the Copy Analysis Prompt command can execute
        /// </summary>
        private bool CanExecuteSmartClipboardAction()
        {
            return CurrentRequirement != null && IsEditingRequirement && !string.IsNullOrWhiteSpace(EditingRequirementText);
        }

        /// <summary>
        /// Start clipboard monitoring for external LLM workflow
        /// </summary>
        private void StartClipboardMonitoring()
        {
            try
            {
                _clipboardMonitorTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(1000) // Check every second
                };
                _clipboardMonitorTimer.Tick += OnClipboardMonitorTick;
                _clipboardMonitorTimer.Start();
                
                _logger.LogDebug("[AnalysisVM] Started clipboard monitoring for external LLM workflow");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[AnalysisVM] Failed to start clipboard monitoring: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// Monitor clipboard for external LLM response
        /// </summary>
        private void OnClipboardMonitorTick(object? sender, EventArgs e)
        {
            try
            {
                if (!IsWaitingForExternalResponse) return;

                // Check clipboard content
                var currentClipboard = Clipboard.GetText();
                if (string.IsNullOrWhiteSpace(currentClipboard) || currentClipboard == _lastClipboardContent)
                    return;

                // Update tracking
                _lastClipboardContent = currentClipboard;

                // Check if this looks like an external LLM response (heuristic)
                if (IsLikelyExternalLLMResponse(currentClipboard))
                {
                    CopyAnalysisButtonText = "🔄 Paste Analysis from Clipboard";
                    _logger.LogInformation("[AnalysisVM] Detected potential external LLM response in clipboard");
                    System.Diagnostics.Debug.WriteLine("[AnalysisVM] 🔄 Clipboard contains potential external LLM response - button updated");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[AnalysisVM] Error monitoring clipboard: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// Heuristic to detect if clipboard content is likely an external LLM response
        /// </summary>
        private bool IsLikelyExternalLLMResponse(string content)
        {
            if (string.IsNullOrWhiteSpace(content) || content.Length < 100) return false;

            // Look for common LLM response patterns
            var indicators = new[]
            {
                "refined requirement", "analysis", "assessment", "recommendation",
                "improved version", "clarity", "structured format", "comparison",
                "quality", "interpretation", "approach"
            };

            var lowContent = content.ToLowerInvariant();
            var indicatorCount = indicators.Count(indicator => lowContent.Contains(indicator));
            
            // Also check if it's substantially different from our prompt
            var isSubstantiallyDifferent = content.Length != _copiedPromptHash.Length;
            
            return indicatorCount >= 3 || isSubstantiallyDifferent;
        }

        /// <summary>
        /// Smart clipboard action: Copy prompt or paste external response
        /// </summary>
        private void ExecuteSmartClipboardAction()
        {
            if (CopyAnalysisButtonText.Contains("Paste"))
            {
                PasteExternalAnalysisFromClipboard();
            }
            else
            {
                CopyAnalysisPromptToClipboard();
            }
        }

        /// <summary>
        /// Paste external LLM analysis from clipboard and process for learning
        /// </summary>
        private async void PasteExternalAnalysisFromClipboard()
        {
            try
            {
                var clipboardContent = Clipboard.GetText();
                if (string.IsNullOrWhiteSpace(clipboardContent))
                {
                    _logger.LogWarning("[AnalysisVM] No content in clipboard to paste");
                    return;
                }

                var requirement = CurrentRequirement;
                if (requirement == null)
                {
                    _logger.LogWarning("[AnalysisVM] No current requirement for external analysis integration");
                    return;
                }

                _logger.LogInformation("[AnalysisVM] Processing external LLM analysis for requirement {RequirementId}", requirement.Item);

                // Extract refined requirement text from external response (basic heuristic)
                var refinedText = ExtractRefinedRequirementFromResponse(clipboardContent);
                if (!string.IsNullOrWhiteSpace(refinedText))
                {
                    // Start editing state with original text, then update to refined text to show changes
                    _mediator.StartEditingRequirement(requirement, requirement.Description ?? "");
                    _mediator.UpdateEditingText(refinedText);
                    EditingRequirementText = refinedText;
                    IsEditingRequirement = true;
                    _logger.LogInformation("[AnalysisVM] Started editing mode with external LLM analysis");
                }

                // Extract and update analysis results from external LLM
                UpdateAnalysisFromExternalResponse(clipboardContent);

                // Create learning data for AnythingLLM
                var learningPrompt = BuildLearningPrompt(requirement, clipboardContent);
                
                // Feed to AnythingLLM learning system
                await FeedLearningToAnythingLLM(learningPrompt);

                // Reset UI state
                IsWaitingForExternalResponse = false;
                CopyAnalysisButtonText = "Copy Analysis Prompt to Clipboard";
                
                System.Diagnostics.Debug.WriteLine("[AnalysisVM] ✅ External LLM analysis integrated successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[AnalysisVM] Failed to process external LLM analysis");
                System.Diagnostics.Debug.WriteLine($"[AnalysisVM] ❌ Error processing external analysis: {ex.Message}");
                
                // Reset UI state on error
                IsWaitingForExternalResponse = false;
                CopyAnalysisButtonText = "Copy Analysis Prompt to Clipboard";
            }
        }

        /// <summary>
        /// Extract refined requirement text from external LLM response using heuristics
        /// </summary>
        private string ExtractRefinedRequirementFromResponse(string response)
        {
            try
            {
                var lines = response.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                
                // Look for "IMPROVED REQUIREMENT" section specifically
                for (int i = 0; i < lines.Length; i++)
                {
                    var line = lines[i].Trim();
                    var upperLine = line.ToUpperInvariant();
                    
                    if (upperLine.Contains("IMPROVED REQUIREMENT") && upperLine.Contains(":"))
                    {
                        // Extract content after the colon on the same line
                        var colonIndex = line.IndexOf(':');
                        if (colonIndex >= 0 && colonIndex < line.Length - 1)
                        {
                            var sameLine = line.Substring(colonIndex + 1).Trim();
                            if (!string.IsNullOrWhiteSpace(sameLine) && sameLine.Length > 50)
                            {
                                return sameLine;
                            }
                        }
                        
                        // If not on same line, look at next lines
                        for (int j = i + 1; j < lines.Length; j++)
                        {
                            var nextLine = lines[j].Trim();
                            if (string.IsNullOrWhiteSpace(nextLine)) continue;
                            
                            var upperNextLine = nextLine.ToUpperInvariant();
                            // Stop at next major section
                            if (upperNextLine.StartsWith("RECOMMENDATIONS") || 
                                upperNextLine.StartsWith("HALLUCINATION") ||
                                upperNextLine.StartsWith("OVERALL") ||
                                upperNextLine.Contains("ASSESSMENT"))
                            {
                                break;
                            }
                            
                            if (nextLine.Length > 50) // Reasonable length for a requirement
                            {
                                return nextLine;
                            }
                        }
                    }
                }
                
                // Fallback: Look for other patterns like "refined requirement", "rewrite", etc.
                for (int i = 0; i < lines.Length; i++)
                {
                    var line = lines[i].Trim();
                    var upperLine = line.ToUpperInvariant();
                    
                    if ((upperLine.Contains("REFINED") && upperLine.Contains("REQUIREMENT")) ||
                        (upperLine.Contains("REWRITE") || upperLine.Contains("REWRITTEN")) ||
                        (upperLine.Contains("IMPROVED") && upperLine.Contains("VERSION")))
                    {
                        // Check if content is on the same line after colon
                        var colonIndex = line.IndexOf(':');
                        if (colonIndex >= 0 && colonIndex < line.Length - 1)
                        {
                            var sameLine = line.Substring(colonIndex + 1).Trim();
                            if (!string.IsNullOrWhiteSpace(sameLine) && sameLine.Length > 50)
                            {
                                return sameLine;
                            }
                        }
                        
                        // Look at subsequent lines
                        for (int j = i + 1; j < lines.Length && j < i + 5; j++) // Look ahead max 5 lines
                        {
                            var nextLine = lines[j].Trim();
                            if (string.IsNullOrWhiteSpace(nextLine)) continue;
                            
                            var upperNextLine = nextLine.ToUpperInvariant();
                            // Skip section headers
                            if (upperNextLine.Contains(":") && (upperNextLine.Contains("RECOMMENDATIONS") || 
                                upperNextLine.Contains("ISSUES") || upperNextLine.Contains("ASSESSMENT")))
                                break;
                                
                            if (nextLine.Length > 50) // Reasonable length for a requirement
                            {
                                return nextLine;
                            }
                        }
                    }
                }
                
                // Final fallback: Look for a substantial paragraph that might be the improved requirement
                var paragraphs = response.Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var para in paragraphs)
                {
                    var cleaned = para.Trim();
                    var upperPara = cleaned.ToUpperInvariant();
                    
                    // Skip analysis sections
                    if (upperPara.StartsWith("QUALITY SCORE") || 
                        upperPara.StartsWith("ISSUES FOUND") ||
                        upperPara.StartsWith("STRENGTHS") ||
                        upperPara.StartsWith("RECOMMENDATIONS") ||
                        upperPara.StartsWith("HALLUCINATION") ||
                        upperPara.StartsWith("OVERALL"))
                        continue;
                        
                    // Look for requirement-like language
                    if (cleaned.Length > 100 && 
                        (cleaned.ToLowerInvariant().Contains("shall") || 
                         cleaned.ToLowerInvariant().Contains("must") || 
                         cleaned.ToLowerInvariant().Contains("system") ||
                         cleaned.ToLowerInvariant().Contains("interface")))
                    {
                        return cleaned;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[AnalysisVM] Error extracting refined text from external response");
            }
            
            _logger.LogWarning("[AnalysisVM] Could not extract improved requirement from external response");
            return string.Empty;
        }

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
                    _logger.LogInformation("[AnalysisVM] Updated {Count} issues from external LLM", extractedIssues.Count);
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
                    if (inIssuesSection && trimmed.StartsWith("*"))
                    {
                        var issue = ParseIssueFromLine(trimmed);
                        if (issue != null)
                        {
                            issues.Add(issue);
                        }
                    }
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
                var content = line.TrimStart('*').Trim();
                
                // Look for pattern: "Category Issue (Priority): Description | Fix: Solution"
                var match = System.Text.RegularExpressions.Regex.Match(content, 
                    @"^(.+?)\s+Issue\s*\((.+?)\):\s*(.+?)(?:\s*\|\s*Fix:\s*(.+))?$", 
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                
                if (match.Success)
                {
                    var category = match.Groups[1].Value.Trim();
                    var priority = match.Groups[2].Value.Trim();
                    var description = match.Groups[3].Value.Trim();
                    var fix = match.Groups[4].Value.Trim();
                    
                    return new AnalysisIssue
                    {
                        Category = category,
                        Severity = ParseSeverityString(priority),
                        Description = $"{category} Issue: {description}",
                        Fix = string.IsNullOrWhiteSpace(fix) ? "" : fix
                    };
                }
                
                // Fallback: treat entire line as description
                if (content.Length > 10)
                {
                    return new AnalysisIssue
                    {
                        Category = "General",
                        Severity = "Medium",
                        Description = content,
                        Fix = ""
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[AnalysisVM] Error parsing issue from line: {Line}", line);
            }
            
            return null;
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
        /// Feed learning data to AnythingLLM system
        /// </summary>
        private async Task FeedLearningToAnythingLLM(string learningData)
        {
            try
            {
                // Check if the analysis service has AnythingLLM capability
                if (_llmService is TestCaseEditorApp.Services.AnythingLLMService anythingLlmService)
                {
                    // Get available workspaces
                    var workspaces = await anythingLlmService.GetWorkspacesAsync();
                    var learningWorkspace = workspaces?.FirstOrDefault(w => w.Name?.Contains("learning", StringComparison.OrdinalIgnoreCase) == true)
                                          ?? workspaces?.FirstOrDefault();
                    
                    if (learningWorkspace != null)
                    {
                        // Send as a learning message to the workspace
                        var learningMessage = $"[LEARNING DATA] {learningData}";
                        await anythingLlmService.SendChatMessageAsync(learningWorkspace.Slug, learningMessage);
                        _logger.LogInformation("[AnalysisVM] Learning data sent to AnythingLLM workspace: {WorkspaceName}", learningWorkspace.Name);
                    }
                    else
                    {
                        _logger.LogWarning("[AnalysisVM] No suitable workspace found for learning data");
                    }
                }
                else
                {
                    _logger.LogWarning("[AnalysisVM] AnythingLLM service not available for learning");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[AnalysisVM] Failed to send learning data to AnythingLLM");
            }
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
                
                // Store hash for detection and start monitoring
                _copiedPromptHash = analysisPrompt;
                _lastClipboardContent = analysisPrompt;
                IsWaitingForExternalResponse = true;
                CopyAnalysisButtonText = "⏳ Waiting for external response...";
                
                // Log success and provide fallback notification
                _logger.LogInformation("[AnalysisVM] Generated external LLM analysis prompt for requirement {RequirementId}", requirement.Item);
                
                // Provide immediate feedback via debug output (visible in development)
                System.Diagnostics.Debug.WriteLine("[AnalysisVM] ✅ Analysis prompt copied to clipboard - monitoring for external LLM response");
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
            _clipboardMonitorTimer?.Stop();
            _clipboardMonitorTimer = null;
            
            _timerUpdateTimer?.Stop();
            _timerUpdateTimer = null;
            
            base.Dispose();
        }
    }
}