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
            TestCaseEditorApp.Services.Logging.Log.Debug($"[AnalysisVM] OnAnalysisUpdated fired for: {requirement.Item}");
            TestCaseEditorApp.Services.Logging.Log.Debug($"[AnalysisVM] Current requirement: {_navigator?.CurrentRequirement?.Item}");
            TestCaseEditorApp.Services.Logging.Log.Debug($"[AnalysisVM] Analysis IsAnalyzed: {requirement.Analysis?.IsAnalyzed}, Score: {requirement.Analysis?.QualityScore}");
            
            // Only refresh if this is the currently displayed requirement
            if (_navigator?.CurrentRequirement?.Item == requirement.Item)
            {
                TestCaseEditorApp.Services.Logging.Log.Debug($"[AnalysisVM] Match found! Refreshing display for {requirement.Item}");
                System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                {
                    RefreshAnalysisDisplay();
                });
            }
            else
            {
                TestCaseEditorApp.Services.Logging.Log.Debug($"[AnalysisVM] No match - not refreshing (user viewing different requirement)");
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
            return _navigator?.CurrentRequirement != null && !IsAnalyzing;
        }

        private void EditRequirement()
        {
            var requirement = _navigator?.CurrentRequirement;
            if (requirement == null) return;

            // Set the description to edit
            EditedDescription = requirement.Description ?? string.Empty;

            // Create editor window with this ViewModel as DataContext
            var editorWindow = new Views.RequirementDescriptionEditorWindow
            {
                DataContext = this,
                Owner = System.Windows.Application.Current.MainWindow
            };
            
            // Create command for Re-Analyze button
            ReAnalyzeCommand = new AsyncRelayCommand(async () =>
            {
                TestCaseEditorApp.Services.Logging.Log.Debug("[AnalysisVM] ReAnalyze command executing");
                
                // Update requirement description from editor
                requirement.Description = EditedDescription;
                var preview = requirement.Description?.Length > 50 
                    ? requirement.Description.Substring(0, 50) + "..." 
                    : requirement.Description ?? "";
                TestCaseEditorApp.Services.Logging.Log.Debug($"[AnalysisVM] Updated description: {preview}");
                
                // Check if batch analysis is running
                if (_navigator?.IsBatchAnalyzing == true)
                {
                    // Queue for re-analysis instead of analyzing immediately
                    requirement.IsQueuedForReanalysis = true;
                    TestCaseEditorApp.Services.Logging.Log.Debug($"[AnalysisVM] Queued requirement {requirement.Item} for re-analysis");
                    
                    // Clear existing analysis to show it's outdated
                    requirement.Analysis = null;
                    RefreshAnalysisDisplay();
                    
                    // Show feedback message
                    AnalysisStatusMessage = "Queued for re-analysis after batch import completes";
                    
                    // Close editor immediately since we're just queuing
                    editorWindow.Close();
                    return;
                }
                
                // Show spinner
                IsAnalyzingInEditor = true;
                
                await Task.Delay(100); // Brief delay to ensure UI updates
                
                try
                {
                    // Run analysis immediately if not in batch mode
                    await AnalyzeRequirementAsync();
                    TestCaseEditorApp.Services.Logging.Log.Debug("[AnalysisVM] Analysis complete");
                }
                finally
                {
                    IsAnalyzingInEditor = false;
                    
                    // Close the editor window after analysis is complete
                    TestCaseEditorApp.Services.Logging.Log.Debug("[AnalysisVM] Closing editor window");
                    editorWindow.Close();
                }
            });

            // Show non-modal window
            editorWindow.Show();
            
            // Handle window closing - clear command reference
            editorWindow.Closed += (s, e) =>
            {
                TestCaseEditorApp.Services.Logging.Log.Debug("[AnalysisVM] Editor window closed");
                ReAnalyzeCommand = null;
                EditedDescription = string.Empty;
            };
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
                var analysis = await _analysisService.AnalyzeRequirementAsync(requirement, useFastMode: false);

                TestCaseEditorApp.Services.Logging.Log.Debug($"[AnalysisVM] Analysis complete. IsAnalyzed: {analysis.IsAnalyzed}, QualityScore: {analysis.QualityScore}");

                // Store result
                requirement.Analysis = analysis;

                // Refresh display
                RefreshAnalysisDisplay();

                if (analysis.IsAnalyzed)
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

            if (analysis?.IsAnalyzed == true)
            {
                HasAnalysis = true;
                QualityScore = analysis.QualityScore;
                Issues = analysis.Issues;
                Recommendations = analysis.Recommendations;
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

            OnPropertyChanged(nameof(HasIssues));
            OnPropertyChanged(nameof(HasRecommendations));
            OnPropertyChanged(nameof(HasFreeformFeedback));
            OnPropertyChanged(nameof(HasNoAnalysis));
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
        public bool HasFreeformFeedback => !string.IsNullOrWhiteSpace(FreeformFeedback);
        public bool HasNoAnalysis => !HasAnalysis;
    }
}
