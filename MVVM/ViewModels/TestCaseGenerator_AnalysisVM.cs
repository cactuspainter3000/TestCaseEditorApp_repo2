using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using TestCaseEditorApp.MVVM.Models;
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

        public TestCaseGenerator_AnalysisVM(ITestCaseGenerator_Navigator? navigator = null, ITextGenerationService? llmService = null)
        {
            _navigator = navigator;
            _llmService = llmService;

            if (_navigator != null)
            {
                _navigator.PropertyChanged += Navigator_PropertyChanged;
            }

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
        }

        public ICommand AnalyzeRequirementCommand { get; }
        public ICommand EditRequirementCommand { get; }
        public IAsyncRelayCommand? ReAnalyzeCommand { get; private set; }

        private bool CanAnalyzeRequirement()
        {
            return _navigator?.CurrentRequirement != null && !IsAnalyzing;
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
                System.Diagnostics.Debug.WriteLine("[AnalysisVM] ReAnalyze command executing");
                
                // Update requirement description from editor
                requirement.Description = EditedDescription;
                var preview = requirement.Description?.Length > 50 
                    ? requirement.Description.Substring(0, 50) + "..." 
                    : requirement.Description ?? "";
                System.Diagnostics.Debug.WriteLine($"[AnalysisVM] Updated description: {preview}");
                
                // Show spinner
                IsAnalyzingInEditor = true;
                
                await Task.Delay(100); // Brief delay to ensure UI updates
                
                try
                {
                    // Run analysis
                    await AnalyzeRequirementAsync();
                    System.Diagnostics.Debug.WriteLine("[AnalysisVM] Analysis complete");
                }
                finally
                {
                    IsAnalyzingInEditor = false;
                    
                    // Close the editor window after analysis is complete
                    System.Diagnostics.Debug.WriteLine("[AnalysisVM] Closing editor window");
                    editorWindow.Close();
                }
            });

            // Show non-modal window
            editorWindow.Show();
            
            // Handle window closing - clear command reference
            editorWindow.Closed += (s, e) =>
            {
                System.Diagnostics.Debug.WriteLine("[AnalysisVM] Editor window closed");
                ReAnalyzeCommand = null;
                EditedDescription = string.Empty;
            };
        }

        private async Task AnalyzeRequirementAsync()
        {
            var requirement = _navigator?.CurrentRequirement;
            if (requirement == null) return;

            System.Diagnostics.Debug.WriteLine($"[AnalysisVM] AnalyzeRequirementAsync started");
            System.Diagnostics.Debug.WriteLine($"[AnalysisVM] Requirement Description (first 200 chars): '{requirement.Description?.Substring(0, Math.Min(200, requirement.Description?.Length ?? 0))}'");

            try
            {
                // Set busy flag to prevent navigation
                if (_navigator != null)
                {
                    System.Diagnostics.Debug.WriteLine("[AnalysisVM] Setting IsLlmBusy = true");
                    _navigator.IsLlmBusy = true;
                    System.Diagnostics.Debug.WriteLine($"[AnalysisVM] IsLlmBusy is now: {_navigator.IsLlmBusy}");
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

                System.Diagnostics.Debug.WriteLine($"[AnalysisVM] About to call AnalyzeRequirementAsync on service");

                // Perform analysis
                var analysis = await _analysisService.AnalyzeRequirementAsync(requirement, useFastMode: false);

                System.Diagnostics.Debug.WriteLine($"[AnalysisVM] Analysis complete. IsAnalyzed: {analysis.IsAnalyzed}, QualityScore: {analysis.QualityScore}");

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
                System.Diagnostics.Debug.WriteLine($"[AnalysisVM] Analysis error: {ex}");
            }
            finally
            {
                IsAnalyzing = false;
                
                // Clear busy flag
                if (_navigator != null)
                {
                    System.Diagnostics.Debug.WriteLine("[AnalysisVM] Setting IsLlmBusy = false");
                    _navigator.IsLlmBusy = false;
                    System.Diagnostics.Debug.WriteLine($"[AnalysisVM] IsLlmBusy is now: {_navigator.IsLlmBusy}");
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

        public bool IsNotAnalyzing => !_isAnalyzingInEditor;

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
