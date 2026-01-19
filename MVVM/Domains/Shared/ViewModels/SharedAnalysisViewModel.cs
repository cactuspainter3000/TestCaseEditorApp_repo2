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

namespace TestCaseEditorApp.MVVM.Domains.Shared.ViewModels
{
    /// <summary>
    /// Shared ViewModel for requirement analysis UI concerns across all domains.
    /// 
    /// This consolidates the duplicate analysis controls into a single, focused implementation
    /// that follows proper MVVM principles:
    /// - UI state management only
    /// - Business logic delegated to Requirements domain services
    /// - Proper dependency injection
    /// - Clear separation of concerns
    /// 
    /// Replaces both TestCaseGenerator_AnalysisControl and Requirements_AnalysisControl
    /// with a single, shared component following DRY principles.
    /// </summary>
    public partial class SharedAnalysisViewModel : ObservableObject, IDisposable
    {
        private readonly IRequirementAnalysisEngine _analysisEngine;
        private readonly ILogger<SharedAnalysisViewModel> _logger;
        private CancellationTokenSource? _analysisCancellation;
        private Requirement? _currentRequirement;
        private bool _isDisposed;

        public SharedAnalysisViewModel(
            IRequirementAnalysisEngine analysisEngine,
            ILogger<SharedAnalysisViewModel> logger)
        {
            _analysisEngine = analysisEngine ?? throw new ArgumentNullException(nameof(analysisEngine));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            AnalyzeNowCommand = new AsyncRelayCommand(AnalyzeCurrentRequirementAsync, () => CanAnalyze);
            PropertyChanged += OnPropertyChanged;
        }

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
        private string freeformFeedback = string.Empty;

        [ObservableProperty]
        private RequirementAnalysis? currentAnalysis;

        // Computed Properties
        public bool HasNoAnalysis => !HasAnalysis && !IsAnalyzing;
        public bool CanAnalyze => _currentRequirement != null && !IsAnalyzing;
        public bool HasAnalysisResult => HasAnalysis && !IsAnalyzing;

        // Commands
        public IAsyncRelayCommand AnalyzeNowCommand { get; }

        /// <summary>
        /// Sets the requirement to analyze. Called by parent ViewModels.
        /// </summary>
        public void SetRequirement(Requirement? requirement)
        {
            if (_currentRequirement == requirement) return;

            _currentRequirement = requirement;
            
            // Clear previous analysis when requirement changes
            ClearAnalysis();
            
            // Update command state
            AnalyzeNowCommand.NotifyCanExecuteChanged();

            _logger.LogDebug("Requirement set for analysis: {HasRequirement}", requirement != null);
        }

        /// <summary>
        /// Perform analysis of the current requirement
        /// </summary>
        private async Task AnalyzeCurrentRequirementAsync()
        {
            if (_currentRequirement == null)
            {
                _logger.LogWarning("Cannot analyze: no requirement set");
                return;
            }

            try
            {
                // Cancel any previous analysis
                _analysisCancellation?.Cancel();
                _analysisCancellation = new CancellationTokenSource();

                IsAnalyzing = true;
                AnalysisStatusMessage = "Analyzing requirement...";
                
                _logger.LogInformation("Starting analysis for requirement: {RequirementText}", _currentRequirement.Description);

                // Delegate to Requirements domain analysis engine
                var analysisResult = await _analysisEngine.AnalyzeRequirementAsync(
                    _currentRequirement, 
                    progressMessage => AnalysisStatusMessage = progressMessage,
                    _analysisCancellation.Token);

                if (analysisResult != null)
                {
                    // Update UI state from analysis results
                    CurrentAnalysis = analysisResult;
                    QualityScore = analysisResult.OriginalQualityScore; // Use original score for user feedback
                    Issues = analysisResult.Issues?.ToList() ?? new List<AnalysisIssue>();
                    FreeformFeedback = analysisResult.FreeformFeedback ?? string.Empty;
                    
                    HasAnalysis = true;
                    AnalysisStatusMessage = "Analysis complete";
                    
                    _logger.LogInformation("Analysis completed successfully with quality score: {Score}", QualityScore);
                }
                else
                {
                    _logger.LogWarning("Analysis returned null result");
                    AnalysisStatusMessage = "Analysis failed - please try again";
                    ClearAnalysis();
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Analysis was cancelled");
                AnalysisStatusMessage = "Analysis cancelled";
                ClearAnalysis();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during requirement analysis");
                AnalysisStatusMessage = "Analysis failed - please try again";
                ClearAnalysis();
            }
            finally
            {
                IsAnalyzing = false;
                AnalyzeNowCommand.NotifyCanExecuteChanged();
            }
        }

        /// <summary>
        /// Clear all analysis state
        /// </summary>
        private void ClearAnalysis()
        {
            CurrentAnalysis = null;
            QualityScore = 0;
            Issues = new List<AnalysisIssue>();
            FreeformFeedback = string.Empty;
            HasAnalysis = false;
        }

        /// <summary>
        /// Handle property change notifications
        /// </summary>
        private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(HasAnalysis) or nameof(IsAnalyzing))
            {
                OnPropertyChanged(nameof(HasNoAnalysis));
                OnPropertyChanged(nameof(HasAnalysisResult));
            }
        }

        /// <summary>
        /// Clean up resources
        /// </summary>
        public void Dispose()
        {
            if (_isDisposed) return;
            
            _analysisCancellation?.Cancel();
            _analysisCancellation?.Dispose();
            
            _isDisposed = true;
            GC.SuppressFinalize(this);
        }
    }
}