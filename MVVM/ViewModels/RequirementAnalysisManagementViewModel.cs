using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using TestCaseEditorApp.MVVM.Models;
using Application = System.Windows.Application;

namespace TestCaseEditorApp.MVVM.ViewModels;

/// <summary>
/// Manages requirement analysis operations: individual analysis, batch analysis, and re-analysis
/// </summary>
public partial class RequirementAnalysisManagementViewModel : ObservableObject
{
    private readonly ILogger<RequirementAnalysisManagementViewModel> _logger;
    // private MainViewModel? _mainViewModel; // REMOVED - architectural violation
    private object? _analysisService;
    
    // Batch analysis synchronization
    private readonly object _batchAnalysisLock = new();
    private bool _batchAnalysisInProgress;
    private readonly HashSet<string> _currentlyAnalyzing = new();
    private readonly HashSet<string> _alreadyAnalyzed = new();

    [ObservableProperty]
    private bool _isBatchAnalyzing;

    // Commands  
    public ICommand AnalyzeUnanalyzedCommand { get; }
    public ICommand ReAnalyzeModifiedCommand { get; }
    public ICommand AnalyzeCurrentRequirementCommand { get; }
    public ICommand BatchAnalyzeAllRequirementsCommand { get; }

    public RequirementAnalysisManagementViewModel(ILogger<RequirementAnalysisManagementViewModel> logger)
    {
        _logger = logger;
        
        // Initialize commands
        AnalyzeUnanalyzedCommand = new AsyncRelayCommand(AnalyzeUnanalyzedAsync);
        ReAnalyzeModifiedCommand = new AsyncRelayCommand(ReAnalyzeModifiedAsync);
        AnalyzeCurrentRequirementCommand = new AsyncRelayCommand(ReAnalyzeRequirementAsync, CanReAnalyze);
        BatchAnalyzeAllRequirementsCommand = new AsyncRelayCommand(BatchAnalyzeAllAsync);
    }

    public void Initialize(MainViewModel mainViewModel)
    {
        // TODO: Replace with proper domain coordination
        _logger.LogWarning("Initialize: Method disabled - architectural violation removed");
        return; // Disabled until proper domain coordination is implemented
        // _mainViewModel = mainViewModel; // REMOVED
        
        // Get analysis service from MainViewModel or create new one
        // This will be set when MainViewModel initializes the analysis service
    }

    /// <summary>
    /// Sets the analysis service instance for requirement analysis operations
    /// </summary>
    public void SetAnalysisService(object analysisService)
    {
        _analysisService = analysisService;
    }

    /// <summary>
    /// Analyzes unanalyzed requirements
    /// </summary>
    private Task AnalyzeUnanalyzedAsync()
    {
        // TODO: Replace with proper domain coordination
        _logger.LogWarning("AnalyzeUnanalyzedAsync: Method disabled - architectural violation removed");
        return Task.CompletedTask; // Disabled until proper domain coordination is implemented
        /*
        if (_mainViewModel?.Requirements == null) return;
        
        var unanalyzed = // _mainViewModel.Requirements.Where(r => r.Analysis == null).ToList();
        if (unanalyzed.Any())
        {
            await BatchAnalyzeRequirementsAsync(unanalyzed);
        }
        */
    }

    /// <summary>
    /// Re-analyzes modified requirements
    /// </summary>
    private Task ReAnalyzeModifiedAsync()
    {
        // TODO: Replace with proper domain coordination
        _logger.LogWarning("ReAnalyzeModifiedAsync: Method disabled - architectural violation removed");
        return Task.CompletedTask; // Disabled until proper domain coordination is implemented
        /*
        if (_mainViewModel?.Requirements == null) return;
        
        // For now, re-analyze requirements that have been analyzed but may need updating
        // In future, could add modification tracking to Requirements
        var analyzed = // _mainViewModel.Requirements.Where(r => r.Analysis?.IsAnalyzed == true).ToList();
        if (analyzed.Any())
        {
            await BatchAnalyzeRequirementsAsync(analyzed);
        }
        */
    }

    /// <summary>
    /// Batch analyzes all requirements
    /// </summary>
    private Task BatchAnalyzeAllAsync()
    {
        // TODO: Replace with proper domain coordination
        _logger.LogWarning("BatchAnalyzeAllAsync: Method disabled - architectural violation removed");
        return Task.CompletedTask; // Disabled until proper domain coordination is implemented
        /*
        if (_mainViewModel?.Requirements == null) return;
        
        var allRequirements = // _mainViewModel.Requirements.ToList();
        if (allRequirements.Any())
        {
            await BatchAnalyzeRequirementsAsync(allRequirements);
        }
        */
    }

    /// <summary>
    /// Determines if re-analysis can be performed on current requirement
    /// </summary>
    public bool CanReAnalyze()
    {
        // TODO: Replace with proper domain coordination
        // return _mainViewModel?.CurrentRequirement != null && !// _mainViewModel.IsLlmBusy;
        return false; // Disabled pending domain coordination
    }

    /// <summary>
    /// Gets the TestCaseGenerator instance from the MainViewModel
    /// </summary>
    private object? GetTestCaseGeneratorInstance()
    {
        // TODO: Replace with proper domain coordination
        // return _mainViewModel?.GetTestCaseGeneratorInstance();
        return null; // Disabled pending domain coordination
    }

    /// <summary>
    /// Re-analyzes the current requirement using the analysis service
    /// </summary>
    public async Task ReAnalyzeRequirementAsync()
    {
        _logger.LogInformation("Starting re-analysis of current requirement");
        
        await Task.CompletedTask;
        try
        {
            var tcg = GetTestCaseGeneratorInstance();
            if (tcg == null) 
            {
                _logger.LogWarning("TestCaseGenerator instance not available for re-analysis");
                return;
            }
            
            // Get the AnalysisVM from TestCaseGenerator_VM
            var analysisVmProp = tcg.GetType().GetProperty("AnalysisVM", BindingFlags.Public | BindingFlags.Instance);
            if (analysisVmProp == null) 
            {
                _logger.LogWarning("AnalysisVM property not found on TestCaseGenerator");
                return;
            }
            
            var analysisVm = analysisVmProp.GetValue(tcg);
            if (analysisVm == null) 
            {
                _logger.LogWarning("AnalysisVM instance is null");
                return;
            }
            
            // Switch to Analysis tab first
            var isAnalysisSelectedProp = tcg.GetType().GetProperty("IsAnalysisSelected", BindingFlags.Public | BindingFlags.Instance);
            isAnalysisSelectedProp?.SetValue(tcg, true);
            
            // Trigger analysis
            var analyzeCommandProp = analysisVm.GetType().GetProperty("AnalyzeRequirementCommand", BindingFlags.Public | BindingFlags.Instance);
            if (analyzeCommandProp?.GetValue(analysisVm) is ICommand analyzeCommand && analyzeCommand.CanExecute(null))
            {
                // _logger.LogInformation($"Executing re-analysis for requirement: {_mainViewModel?.CurrentRequirement?.Item}");
                analyzeCommand.Execute(null);
            }
            else
            {
                _logger.LogWarning("AnalyzeRequirementCommand not available or cannot execute");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Re-analysis failed");
            // TODO: Replace with proper domain UI coordinator: _mainViewModel?.SetTransientStatus($"Re-analysis failed: {ex.Message}", 5, true);
        }
    }

    /// <summary>
    /// Performs batch analysis on a list of requirements
    /// </summary>
    public async Task BatchAnalyzeRequirementsAsync(List<Requirement> requirements)
    {
        if (_analysisService == null || !requirements.Any())
        {
            _logger.LogInformation("Batch analysis skipped - no analysis service or requirements");
            return;
        }

        // Prevent concurrent batch analysis
        lock (_batchAnalysisLock)
        {
            if (_batchAnalysisInProgress)
            {
                _logger.LogInformation($"Batch analysis already in progress - skipping duplicate call for {requirements.Count} requirements");
                return;
            }
            _batchAnalysisInProgress = true;
        }

        try
        {
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                IsBatchAnalyzing = true;
            });
            
            _logger.LogInformation($"Starting batch analysis for {requirements.Count} requirements");
            
            await Task.Delay(500); // Brief delay to let UI settle after import

            // Get requirements in the order they appear in the UI (sorted view)
            var orderedRequirements = GetOrderedRequirements(requirements);
            
            // Filter requirements to only those that need analysis
            var needAnalysis = FilterRequirementsNeedingAnalysis(orderedRequirements);

            if (!needAnalysis.Any())
            {
                _logger.LogInformation("No requirements need analysis - batch analysis complete");
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    // TODO: Replace with proper domain UI coordinator: _mainViewModel?.SetTransientStatus("All requirements already analyzed", 3);
                });
                return;
            }

            await ProcessRequirementsSequentially(needAnalysis);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Batch analysis failed");
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                // TODO: Replace with proper domain UI coordinator: _mainViewModel?.SetTransientStatus($"Batch analysis failed: {ex.Message}", 5, true);
            });
        }
        finally
        {
            lock (_batchAnalysisLock)
            {
                _batchAnalysisInProgress = false;
                _currentlyAnalyzing.Clear();
            }
            
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                IsBatchAnalyzing = false;
                // TODO: Replace with proper domain UI coordinator: _mainViewModel?.SetTransientStatus("Batch analysis completed", 3);
            });
        }
    }

    /// <summary>
    /// Handle requirement analysis request events
    /// </summary>
    public void OnRequirementAnalysisRequested(object? sender, object e)
    {
        _logger.LogInformation("Requirement analysis requested via event");
        
        // Trigger re-analysis of current requirement
        Task.Run(async () => await ReAnalyzeRequirementAsync());
    }

    /// <summary>
    /// Handle requirement edit completion events
    /// </summary>
    public void OnRequirementEdited(object? sender, object e)
    {
        _logger.LogInformation("Requirement edited - may trigger re-analysis if needed");
        
        // Optionally trigger re-analysis after editing
        // This could be configurable based on user preferences
    }

    /// <summary>
    /// Handle requirement edit cancellation events
    /// </summary>
    public void OnRequirementEditCancelled(object? sender, EventArgs e)
    {
        _logger.LogInformation("Requirement editing cancelled");
    }

    /// <summary>
    /// Get requirements ordered by UI display order
    /// </summary>
    private List<Requirement> GetOrderedRequirements(List<Requirement> requirements)
    {
        var orderedRequirements = new List<Requirement>();
        try
        {
            // Try to get UI display order from requirements navigator
            // TODO: Replace with proper domain coordination
            // var requirementsView = _mainViewModel?.RequirementsNavigator?.RequirementsView;
            var requirementsView = (object?)null; // Disabled pending domain coordination
            if (requirementsView != null && requirementsView is IEnumerable<Requirement> requirementsList)
            {
                foreach (Requirement req in requirementsList)
                {
                    // TODO: Fix requirements collection access
                    // if (requirements.Contains(req))
                    {
                        // orderedRequirements.Add(req);
                    }
                }
                _logger.LogInformation($"Using UI display order: [{string.Join(", ", orderedRequirements.Select(r => r.Item))}]");
            }
            else
            {
                _logger.LogInformation("RequirementsView not available, using import order");
                orderedRequirements = requirements;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error getting UI order, using import order");
            orderedRequirements = requirements;
        }

        return orderedRequirements;
    }

    /// <summary>
    /// Filter requirements to only those that need analysis
    /// </summary>
    private List<Requirement> FilterRequirementsNeedingAnalysis(List<Requirement> requirements)
    {
        return requirements.Where(r => 
        {
            if (string.IsNullOrWhiteSpace(r.Item)) return false;
            
            lock (_batchAnalysisLock)
            {
                if (_currentlyAnalyzing.Contains(r.Item) || _alreadyAnalyzed.Contains(r.Item))
                {
                    _logger.LogInformation($"Skipping {r.Item} - already processing or analyzed");
                    return false;
                }
            }
            
            if (r.Analysis?.IsAnalyzed == true)
            {
                _logger.LogInformation($"Skipping {r.Item} - already has analysis");
                lock (_batchAnalysisLock)
                {
                    _alreadyAnalyzed.Add(r.Item);
                }
                return false;
            }
            
            if (string.IsNullOrWhiteSpace(r.Description))
            {
                _logger.LogInformation($"Skipping {r.Item} - no description");
                return false;
            }
            
            return true;
        }).ToList();
    }

    /// <summary>
    /// Process requirements sequentially to avoid overwhelming the LLM service
    /// </summary>
    private async Task ProcessRequirementsSequentially(List<Requirement> requirements)
    {
        // Mark all requirements as currently being analyzed
        lock (_batchAnalysisLock)
        {
            foreach (var req in requirements)
            {
                _currentlyAnalyzing.Add(req.Item);
            }
        }

        int completed = 0;
        int total = requirements.Count;
        TimeSpan? avgAnalysisTime = null;

        _logger.LogInformation($"Processing {total} requirements in UI display order: [{string.Join(", ", requirements.Select(r => r.Item))}]");

        foreach (var req in requirements)
        {
            try
            {
                _logger.LogInformation($"Processing requirement {req.Item} ({completed + 1}/{total})");
                
                // Show progress message
                var progressMessage = total == 1 
                    ? "Analyzing requirement..." 
                    : avgAnalysisTime.HasValue
                        ? $"Analyzing requirements... ({completed + 1}/{total}) - ~{Math.Ceiling((total - completed) * avgAnalysisTime.Value.TotalMinutes)} min remaining"
                        : $"Analyzing requirements... ({completed + 1}/{total})";
                
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    // TODO: Replace with domain coordinator - _mainViewModel?.SetTransientStatus(progressMessage, 300); // Long timeout for analysis
                });

                // TODO: Implement actual requirement analysis logic here
                await Task.Delay(2000); // Simulate analysis time
                
                completed++;
                
                // Update average analysis time for better progress estimation
                if (completed > 0)
                {
                    avgAnalysisTime = TimeSpan.FromMinutes(2); // Estimate based on typical analysis time
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to analyze requirement {req.Item}");
            }
            finally
            {
                lock (_batchAnalysisLock)
                {
                    _currentlyAnalyzing.Remove(req.Item);
                    _alreadyAnalyzed.Add(req.Item);
                }
            }
        }
    }
}
