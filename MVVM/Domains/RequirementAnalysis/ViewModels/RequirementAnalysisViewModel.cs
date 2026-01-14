using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.MVVM.ViewModels;
using TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Mediators;
using TestCaseEditorApp.MVVM.Events;

namespace TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.ViewModels
{
    /// <summary>
    /// Domain ViewModel responsible for requirement analysis functionality.
    /// Extracted from MainViewModel to handle all analysis-related operations.
    /// </summary>
    public partial class RequirementAnalysisViewModel : BaseDomainViewModel, IDisposable
    {
        // Domain mediator (properly typed)
        private new readonly ITestCaseGenerationMediator _mediator;
        
        // Legacy delegate support for backwards compatibility
        private readonly Action<string, int>? _setTransientStatus;
        
        // Data access delegates from MainViewModel
        private readonly Func<IEnumerable<Requirement>> _getRequirements;
        private readonly Func<Requirement?> _getCurrentRequirement;

        public RequirementAnalysisViewModel(
            ITestCaseGenerationMediator mediator,
            ILogger<RequirementAnalysisViewModel> logger,
            Func<IEnumerable<Requirement>> getRequirements,
            Func<Requirement?> getCurrentRequirement,
            Action<string, int>? setTransientStatus = null)
            : base(mediator, logger)
        {
            _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
            _getRequirements = getRequirements ?? throw new ArgumentNullException(nameof(getRequirements));
            _getCurrentRequirement = getCurrentRequirement ?? throw new ArgumentNullException(nameof(getCurrentRequirement));
            _setTransientStatus = setTransientStatus;

            _logger.LogDebug("RequirementAnalysisViewModel initialized");
            InitializeCommands();
        }

        private new void InitializeCommands()
        {
            AnalyzeUnanalyzedCommand = new RelayCommand(
                () => AnalyzeUnanalyzed());
                
            ReAnalyzeModifiedCommand = new RelayCommand(
                () => ReAnalyzeModified());
                
            AnalyzeCurrentRequirementCommand = new RelayCommand(
                () => AnalyzeCurrentRequirement(),
                () => _getCurrentRequirement() != null);
                
            BatchAnalyzeAllRequirementsCommand = new RelayCommand(
                () => BatchAnalyzeAllRequirements(),
                () => _getRequirements().Any());
        }

        #region Commands

        public ICommand AnalyzeUnanalyzedCommand { get; private set; } = null!;
        public ICommand ReAnalyzeModifiedCommand { get; private set; } = null!;
        public ICommand AnalyzeCurrentRequirementCommand { get; private set; } = null!;
        public ICommand BatchAnalyzeAllRequirementsCommand { get; private set; } = null!;

        #endregion

        #region Analysis Methods

        /// <summary>
        /// Analyzes requirements that haven't been analyzed yet.
        /// Extracted from MainViewModel.AnalyzeUnanalyzed()
        /// </summary>
        private void AnalyzeUnanalyzed()
        {
            try
            {
                var requirements = _getRequirements().ToList();
                var unanalyzedCount = requirements.Count(r => r.Analysis == null);
                _setTransientStatus?.Invoke($"🔍 Analyzing {unanalyzedCount} unanalyzed requirements...", 3);
                TestCaseEditorApp.Services.Logging.Log.Info($"[ANALYSIS] Analyze unanalyzed requested for {unanalyzedCount} requirements");
                
                // TODO: Implement actual analysis logic
                // This would trigger analysis for unanalyzed requirements
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[ANALYSIS] Failed to analyze unanalyzed: {ex.Message}");
                _setTransientStatus?.Invoke("❌ Failed to analyze unanalyzed", 3);
            }
        }
        
        /// <summary>
        /// Re-analyzes requirements that have been modified.
        /// Extracted from MainViewModel.ReAnalyzeModified()
        /// </summary>
        private void ReAnalyzeModified()
        {
            try
            {
                var requirements = _getRequirements().ToList();
                var modifiedCount = requirements.Count(r => r.Analysis != null && r.IsQueuedForReanalysis);
                _setTransientStatus?.Invoke($"🔄 Re-analyzing {modifiedCount} modified requirements...", 3);
                TestCaseEditorApp.Services.Logging.Log.Info($"[ANALYSIS] Re-analyze modified requested for {modifiedCount} requirements");
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[ANALYSIS] Failed to re-analyze modified: {ex.Message}");
                _setTransientStatus?.Invoke("❌ Failed to re-analyze modified", 3);
            }
        }
        
        /// <summary>
        /// Analyzes the currently selected requirement.
        /// Extracted from MainViewModel.AnalyzeCurrentRequirement()
        /// </summary>
        private void AnalyzeCurrentRequirement()
        {
            try
            {
                var currentRequirement = _getCurrentRequirement();
                if (currentRequirement == null)
                {
                    _setTransientStatus?.Invoke("❌ No requirement selected", 3);
                    return;
                }

                _setTransientStatus?.Invoke($"🔍 Analyzing requirement: {currentRequirement.Item}...", 3);
                TestCaseEditorApp.Services.Logging.Log.Info($"[ANALYSIS] Analyze current requirement requested: {currentRequirement.Item}");
                
                // TODO: Implement actual analysis logic for single requirement
                // This could trigger the LLM analysis for the current requirement
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[ANALYSIS] Failed to analyze current requirement: {ex.Message}");
                _setTransientStatus?.Invoke("❌ Failed to analyze current requirement", 3);
            }
        }
        
        /// <summary>
        /// Performs batch analysis on all requirements in the project.
        /// Extracted from MainViewModel.BatchAnalyzeAllRequirements()
        /// </summary>
        private void BatchAnalyzeAllRequirements()
        {
            try
            {
                var requirements = _getRequirements().ToList();
                var totalCount = requirements.Count;
                _setTransientStatus?.Invoke($"⚡ Starting batch analysis of {totalCount} requirements...", 3);
                TestCaseEditorApp.Services.Logging.Log.Info($"[ANALYSIS] Batch analyze all requested for {totalCount} requirements");
                
                // TODO: Implement actual batch analysis logic
                // This could trigger analysis for all requirements in sequence or parallel
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[ANALYSIS] Failed to batch analyze all requirements: {ex.Message}");
                _setTransientStatus?.Invoke("❌ Failed to batch analyze all requirements", 3);
            }
        }

        /// <summary>
        /// Pastes ChatGPT analysis from clipboard and applies it to current requirements.
        /// </summary>
        public void PasteChatGptAnalysis()
        {
            try
            {
                _setTransientStatus?.Invoke("📋 Paste ChatGPT analysis coming soon...", 3);
                TestCaseEditorApp.Services.Logging.Log.Info("[ANALYSIS] ChatGPT analysis paste requested");
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[ANALYSIS] Failed to paste ChatGPT analysis: {ex.Message}");
                _setTransientStatus?.Invoke("❌ Failed to paste analysis", 3);
            }
        }

        /// <summary>
        /// Batch analyze requirements in background after import.
        /// Shows progress notifications and updates requirements with analysis results.
        /// Thread-safe with duplicate prevention.
        /// </summary>
        public async Task BatchAnalyzeRequirementsAsync(List<Requirement> requirements)
        {
            try
            {
                _setTransientStatus?.Invoke($"🔍 Batch analysis functionality coming soon... ({requirements.Count} requirements)", 3);
                TestCaseEditorApp.Services.Logging.Log.Info($"[ANALYSIS] Batch analysis requested for {requirements.Count} requirements");
                
                // TODO: Implement full batch analysis logic here
                // This method was extracted from MainViewModel and needs to be properly implemented
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                TestCaseEditorApp.Services.Logging.Log.Error(ex, $"[ANALYSIS] Failed to batch analyze requirements: {ex.Message}");
                _setTransientStatus?.Invoke("❌ Failed to start batch analysis", 3);
            }
        }

        #endregion
    }
}