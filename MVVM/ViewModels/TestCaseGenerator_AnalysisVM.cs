using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.MVVM.Utils;
using TestCaseEditorApp.Services;
using TestCaseEditorApp.Services.Prompts;

namespace TestCaseEditorApp.MVVM.ViewModels
{
    /// <summary>
    /// ViewModel for displaying and managing LLM-powered requirement analysis.
    /// </summary>
    public partial class TestCaseGenerator_AnalysisVM : ObservableObject
    {
        private readonly ITestCaseGenerator_Navigator? _navigator;
        private readonly ITextGenerationService? _llmService;
        private RequirementAnalysisService? _analysisService;
        
        // Track if edit window is currently open to prevent multiple instances
        [ObservableProperty]
        private bool _isEditWindowOpen = false;
        
        // Expose batch analyzing state for UI binding
        public bool IsBatchAnalyzing => _navigator?.IsBatchAnalyzing ?? false;

        public TestCaseGenerator_AnalysisVM(ITestCaseGenerator_Navigator? navigator = null, ITextGenerationService? llmService = null)
        {
            _navigator = navigator;
            _llmService = llmService;

            if (_navigator != null)
            {
                _navigator.PropertyChanged += Navigator_PropertyChanged;
            }

            // Subscribe to mediator for analysis updates
            AnalysisMediator.AnalysisUpdated += OnAnalysisUpdated;

            AnalyzeRequirementCommand = new AsyncRelayCommand(AnalyzeRequirementAsync, CanAnalyzeRequirement);
            EditRequirementCommand = new RelayCommand(EditRequirement, CanEditRequirement);

            // Initial load
            RefreshAnalysisDisplay();
        }

        private void Navigator_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ITestCaseGenerator_Navigator.CurrentRequirement))
            {
                RefreshAnalysisDisplay();
                ((AsyncRelayCommand)AnalyzeRequirementCommand).NotifyCanExecuteChanged();
                ((RelayCommand)EditRequirementCommand).NotifyCanExecuteChanged();
            }
            else if (e.PropertyName == nameof(ITestCaseGenerator_Navigator.IsBatchAnalyzing))
            {
                OnPropertyChanged(nameof(IsBatchAnalyzing));
                ((AsyncRelayCommand)AnalyzeRequirementCommand).NotifyCanExecuteChanged();
            }
            else if (e.PropertyName == nameof(ITestCaseGenerator_Navigator.Requirements))
            {
                // Requirements collection updated - refresh if current requirement's analysis changed
                RefreshAnalysisDisplay();
            }
        }

        private void OnAnalysisUpdated(Requirement requirement)
        {
            TestCaseEditorApp.Services.Logging.Log.Info($"[AnalysisVM] OnAnalysisUpdated fired for: {requirement.Item}");
            TestCaseEditorApp.Services.Logging.Log.Info($"[AnalysisVM] Current requirement: {_navigator?.CurrentRequirement?.Item}");
            TestCaseEditorApp.Services.Logging.Log.Info($"[AnalysisVM] Analysis IsAnalyzed: {requirement.Analysis?.IsAnalyzed}, Score: {requirement.Analysis?.QualityScore}");
            
            // Only refresh if this is the currently displayed requirement
            if (_navigator?.CurrentRequirement?.Item == requirement.Item)
            {
                TestCaseEditorApp.Services.Logging.Log.Info($"[AnalysisVM] Match found! Refreshing display for {requirement.Item}");
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
            return _navigator?.CurrentRequirement != null && !IsAnalyzing && !IsBatchAnalyzing;
        }

        private bool CanEditRequirement()
        {
            return _navigator?.CurrentRequirement != null && !IsAnalyzing && !IsEditWindowOpen;
        }

        private void EditRequirement()
        {
            var requirement = _navigator?.CurrentRequirement;
            if (requirement == null || IsEditWindowOpen) return;

            try
            {
                TestCaseEditorApp.Services.Logging.Log.Debug("[AnalysisVM] EditRequirement called");
                
                // Mark that edit window is open and refresh command states
                IsEditWindowOpen = true;
                ((RelayCommand)EditRequirementCommand).NotifyCanExecuteChanged();

                // Use the navigator's modal system to show the enhanced editor
                _navigator?.ShowRequirementEditor(requirement);
                
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
            var requirement = _navigator?.CurrentRequirement;
            if (requirement == null)
                return "No current requirement selected.";

            try
            {
                // Initialize service if needed
                if (_analysisService == null)
                {
                    var llm = _llmService ?? LlmFactory.Create();
                    _analysisService = new RequirementAnalysisService(llm);
                }

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
            var requirement = _navigator?.CurrentRequirement;
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
            var requirement = _navigator?.CurrentRequirement;
            if (requirement == null) return;

            TestCaseEditorApp.Services.Logging.Log.Debug($"[AnalysisVM] AnalyzeRequirementAsync started");
            TestCaseEditorApp.Services.Logging.Log.Debug($"[AnalysisVM] Requirement Description (first 200 chars): '{requirement.Description?.Substring(0, Math.Min(200, requirement.Description?.Length ?? 0))}'");

            try
            {
                // Set busy flag to prevent navigation
                if (_navigator != null)
                {
                    TestCaseEditorApp.Services.Logging.Log.Debug("[AnalysisVM] Setting IsLlmBusy = true");
                    _navigator.IsLlmBusy = true;
                    TestCaseEditorApp.Services.Logging.Log.Debug($"[AnalysisVM] IsLlmBusy is now: {_navigator.IsLlmBusy}");
                }

                // Clear existing analysis and show spinner
                HasAnalysis = false;
                IsAnalyzing = true;
                AnalysisStatusMessage = "Analyzing requirement quality...";

                // Initialize service if needed
                if (_analysisService == null)
                {
                    var llm = _llmService ?? LlmFactory.Create();
                    _analysisService = new RequirementAnalysisService(llm);
                }

                TestCaseEditorApp.Services.Logging.Log.Debug($"[AnalysisVM] About to call AnalyzeRequirementAsync on service");

                // Perform analysis
                var analysis = await _analysisService.AnalyzeRequirementAsync(requirement);

                TestCaseEditorApp.Services.Logging.Log.Debug($"[AnalysisVM] Analysis complete. IsAnalyzed: {analysis.IsAnalyzed}, QualityScore: {analysis.QualityScore}");

                // Store result
                requirement.Analysis = analysis;

                // Mark workspace dirty when analysis is completed
                if (_navigator is MainViewModel mainVm)
                {
                    mainVm.IsDirty = true;
                    TestCaseEditorApp.Services.Logging.Log.Debug("[AnalysisVM] Analysis completed - marked workspace dirty");
                }

                // Refresh display
                RefreshAnalysisDisplay();

                // Single UI refresh to avoid duplicate displays
                OnPropertyChanged(nameof(Recommendations));
                OnPropertyChanged(nameof(HasAnalysis));
                TestCaseEditorApp.Services.Logging.Log.Debug("[AnalysisVM] UI refresh for analysis results");                if (analysis.IsAnalyzed)
                {
                    AnalysisStatusMessage = $"Analysis complete. Quality score: {analysis.QualityScore}/10";
                }
                else
                {
                    AnalysisStatusMessage = $"Analysis failed: {analysis.ErrorMessage}";
                }
            }
            catch (Exception ex)
            {
                AnalysisStatusMessage = $"Error: {ex.Message}";
                TestCaseEditorApp.Services.Logging.Log.Debug($"[AnalysisVM] Analysis error: {ex}");
            }
            finally
            {
                IsAnalyzing = false;
                
                // Clear busy flag
                if (_navigator != null)
                {
                    TestCaseEditorApp.Services.Logging.Log.Debug("[AnalysisVM] Setting IsLlmBusy = false");
                    _navigator.IsLlmBusy = false;
                    TestCaseEditorApp.Services.Logging.Log.Debug($"[AnalysisVM] IsLlmBusy is now: {_navigator.IsLlmBusy}");
                }

                // Clear status message after a delay
                await Task.Delay(3000);
                AnalysisStatusMessage = string.Empty;
            }
        }

        private void RefreshAnalysisDisplay()
        {
            var analysis = _navigator?.CurrentRequirement?.Analysis;
            var isBatchRunning = IsBatchAnalyzing;
            
            TestCaseEditorApp.Services.Logging.Log.Info($"[AnalysisVM] RefreshAnalysisDisplay called. CurrentReq: {_navigator?.CurrentRequirement?.Item}, HasAnalysis: {analysis?.IsAnalyzed}, Score: {analysis?.QualityScore}, IsBatchAnalyzing: {isBatchRunning}");

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
                    TestCaseEditorApp.Services.Logging.Log.Info($"[AnalysisVM] Setting IsAnalyzing=true for batch analysis on requirement without analysis: {_navigator?.CurrentRequirement?.Item}");
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
    }
}
