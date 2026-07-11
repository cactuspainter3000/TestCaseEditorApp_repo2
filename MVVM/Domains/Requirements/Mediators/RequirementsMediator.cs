using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.MVVM.Utils;
using TestCaseEditorApp.MVVM.Domains.Requirements.Events;
using TestCaseEditorApp.MVVM.Domains.NewProject.Events;
using TestCaseEditorApp.MVVM.Domains.NewProject.Mediators;
using TestCaseEditorApp.MVVM.Domains.OpenProject.Events;
using TestCaseEditorApp.MVVM.Events;
using TestCaseEditorApp.Services; // For SmartRequirementImporter
using TestCaseEditorApp.MVVM.Domains.Requirements.Services;
using System.Windows;

namespace TestCaseEditorApp.MVVM.Domains.Requirements.Mediators
{
    /// <summary>
    /// Requirements domain mediator implementation.
    /// Handles all requirements management functionality following architectural patterns.
    /// </summary>
    public class RequirementsMediator : BaseDomainMediator<RequirementsEvents>, IRequirementsMediator
    {
        private readonly TestCaseEditorApp.MVVM.Domains.Requirements.Services.IRequirementAnalysisService _analysisService;
        private readonly IRequirementAnalysisEngine? _analysisEngine;
        private readonly SmartRequirementImporter _smartImporter;
        private readonly ObservableCollection<Requirement> _requirements;
        private readonly IWorkspaceContext _workspaceContext;
        private readonly INewProjectMediator _newProjectMediator;
        private readonly IJamaConnectService _jamaConnectService;
        private readonly IJamaDocumentParserService _jamaDocumentParserService;
        
        private Requirement? _currentRequirement;
        private bool _isDirty;
        private bool _isAnalyzing;
        private bool _isImporting;

        public ObservableCollection<Requirement> Requirements => _requirements;

        public Requirement? CurrentRequirement
        {
            get => _currentRequirement;
            set
            {
                if (_currentRequirement != value)
                {
                    _currentRequirement = value;
                    _logger.LogInformation("[RequirementsMediator] CurrentRequirement setter - Requirement: {Item}, HasAnalysis: {HasAnalysis}, IsAnalyzed: {IsAnalyzed}",
                        value?.Item ?? "null",
                        value?.Analysis != null ? "true" : "false",
                        value?.Analysis?.IsAnalyzed ?? false);
                    var eventData = new RequirementsEvents.RequirementSelected
                    {
                        Requirement = value!,
                        SelectedBy = "Mediator"
                    };
                    PublishEvent(eventData);
                    
                    _logger.LogDebug("Current requirement changed to: {RequirementId}", value?.GlobalId ?? "null");
                }
            }
        }

        public bool IsDirty
        {
            get => _isDirty;
            set
            {
                if (_isDirty != value)
                {
                    _isDirty = value;
                    var workflowEvent = new RequirementsEvents.WorkflowStateChanged
                    {
                        PropertyName = nameof(IsDirty),
                        NewValue = value,
                        OldValue = _isDirty
                    };
                    PublishEvent(workflowEvent);
                    
                    _logger.LogDebug("IsDirty changed to: {IsDirty}", value);
                }
            }
        }

        public bool IsAnalyzing
        {
            get => _isAnalyzing;
            set
            {
                if (_isAnalyzing != value)
                {
                    var oldValue = _isAnalyzing; // Capture old value before changing
                    _isAnalyzing = value;
                    PublishEvent(new RequirementsEvents.WorkflowStateChanged
                    {
                        PropertyName = nameof(IsAnalyzing),
                        NewValue = value,
                        OldValue = oldValue
                    });
                    _logger.LogDebug("IsAnalyzing changed to: {IsAnalyzing}", value);
                }
            }
        }

        public bool IsImporting
        {
            get => _isImporting;
            set
            {
                if (_isImporting != value)
                {
                    _isImporting = value;
                    PublishEvent(new RequirementsEvents.WorkflowStateChanged
                    {
                        PropertyName = nameof(IsImporting),
                        NewValue = value,
                        OldValue = _isImporting
                    });
                    _logger.LogDebug("IsImporting changed to: {IsImporting}", value);
                }
            }
        }

        public RequirementsMediator(
            ILogger<RequirementsMediator> logger,
            IDomainUICoordinator uiCoordinator,
            TestCaseEditorApp.MVVM.Domains.Requirements.Services.IRequirementAnalysisService analysisService,
            IWorkspaceContext workspaceContext,
            INewProjectMediator newProjectMediator,
            IJamaConnectService jamaConnectService,
            IJamaDocumentParserService jamaDocumentParserService,
            SmartRequirementImporter smartImporter,
            IRequirementAnalysisEngine? analysisEngine = null,
            PerformanceMonitoringService? performanceMonitor = null,
            EventReplayService? eventReplay = null)
            : base(logger, uiCoordinator, "Requirements", performanceMonitor, eventReplay)
        {
            _analysisService = analysisService ?? throw new ArgumentNullException(nameof(analysisService));
            _workspaceContext = workspaceContext ?? throw new ArgumentNullException(nameof(workspaceContext));
            _newProjectMediator = newProjectMediator ?? throw new ArgumentNullException(nameof(newProjectMediator));
            _jamaConnectService = jamaConnectService ?? throw new ArgumentNullException(nameof(jamaConnectService));
            _jamaDocumentParserService = jamaDocumentParserService ?? throw new ArgumentNullException(nameof(jamaDocumentParserService));
            _smartImporter = smartImporter ?? throw new ArgumentNullException(nameof(smartImporter));
            _analysisEngine = analysisEngine;
            
            _requirements = new ObservableCollection<Requirement>();

            // Subscribe to cross-domain events for requirement synchronization
            SubscribeToCrossDomainEvents();

            _logger.LogDebug("RequirementsMediator created");
        }

        // ===== REQUIREMENTS MANAGEMENT =====

        public async Task<bool> ImportRequirementsAsync(string filePath, string importType = "Auto")
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("File path cannot be null or empty", nameof(filePath));

            IsImporting = true;
            ShowProgress("Analyzing document format...", 0);

            try
            {
                UpdateProgress("Running smart import analysis...", 25);
                
                var importResult = await _smartImporter.ImportRequirementsAsync(filePath);
                
                UpdateProgress("Processing import results...", 75);
                
                if (importResult.Success && importResult.Requirements.Count > 0)
                {
                    // Clear existing and add new requirements
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        _requirements.Clear();
                        var sortedRequirements = importResult.Requirements.OrderBy(r => r.Item ?? r.Name ?? string.Empty).ToList();
                        foreach (var requirement in sortedRequirements)
                        {
                            _requirements.Add(requirement);
                        }
                    });

                    // Set first requirement as current
                    if (importResult.Requirements.Count > 0)
                    {
                        CurrentRequirement = importResult.Requirements.First();
                    }

                    PublishEvent(new RequirementsEvents.RequirementsImported
                    {
                        Requirements = importResult.Requirements,
                        SourceFile = filePath,
                        ImportMethod = importResult.ImportMethod,
                        ImportDuration = importResult.ImportDuration
                    });

                    var collectionEvent = new RequirementsEvents.RequirementsCollectionChanged
                    {
                        Action = "Import",
                        AffectedRequirements = importResult.Requirements,
                        NewCount = _requirements.Count
                    };
                    PublishEvent(collectionEvent);

                    IsDirty = true;
                    HideProgress();
                    ShowNotification(importResult.UserMessage, DomainNotificationType.Success);

                    _logger.LogInformation("Requirements import completed: {Count} requirements from {FilePath}",
                        importResult.Requirements.Count, filePath);

                    return true;
                }
                else
                {
                    HideProgress();
                    ShowNotification(importResult.ErrorMessage ?? "No requirements found", DomainNotificationType.Warning);

                    PublishEvent(new RequirementsEvents.RequirementsImportFailed
                    {
                        FilePath = filePath,
                        ErrorMessage = importResult.ErrorMessage ?? "No requirements found",
                        FormatAnalysis = importResult.FormatAnalysis?.Description
                    });

                    return false;
                }
            }
            catch (Exception ex)
            {
                HideProgress();
                ShowNotification($"Import failed: {ex.Message}", DomainNotificationType.Error);

                PublishEvent(new RequirementsEvents.RequirementsImportFailed
                {
                    FilePath = filePath,
                    ErrorMessage = ex.Message,
                    Exception = ex
                });

                _logger.LogError(ex, "Requirements import failed for {FilePath}", filePath);
                return false;
            }
            finally
            {
                IsImporting = false;
            }
        }

        public async Task<bool> ImportAdditionalRequirementsAsync(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("File path cannot be null or empty", nameof(filePath));

            IsImporting = true;
            ShowProgress("Importing additional requirements...", 0);

            try
            {
                var importResult = await _smartImporter.ImportRequirementsAsync(filePath);

                if (importResult.Success && importResult.Requirements.Count > 0)
                {
                    // Add new requirements to existing collection
                    var newRequirements = new List<Requirement>();
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        foreach (var requirement in importResult.Requirements)
                        {
                            // Check for duplicates
                            if (!_requirements.Any(r => r.GlobalId == requirement.GlobalId))
                            {
                                _requirements.Add(requirement);
                                newRequirements.Add(requirement);
                            }
                        }
                    });

                    if (newRequirements.Count > 0)
                    {
                        PublishEvent(new RequirementsEvents.RequirementsCollectionChanged
                        {
                            Action = "Add",
                            AffectedRequirements = newRequirements,
                            NewCount = _requirements.Count
                        });

                        IsDirty = true;
                        ShowNotification($"Added {newRequirements.Count} new requirements", DomainNotificationType.Success);
                    }
                    else
                    {
                        ShowNotification("No new requirements found (duplicates skipped)", DomainNotificationType.Info);
                    }

                    return true;
                }

                return false;
            }
            finally
            {
                IsImporting = false;
                HideProgress();
            }
        }

        public async Task<bool> ExportRequirementsAsync(IReadOnlyList<Requirement> requirements, string exportType, string outputPath)
        {
            if (requirements == null) throw new ArgumentNullException(nameof(requirements));
            if (string.IsNullOrWhiteSpace(exportType)) throw new ArgumentException("Export type cannot be null or empty", nameof(exportType));
            if (string.IsNullOrWhiteSpace(outputPath)) throw new ArgumentException("Output path cannot be null or empty", nameof(outputPath));

            ShowProgress($"Exporting {requirements.Count} requirements...", 0);

            try
            {
                UpdateProgress("Formatting requirements...", 50);

                // TODO: Implement actual export logic
                await Task.Delay(1000); // Simulate export work

                PublishEvent(new RequirementsEvents.RequirementsExported
                {
                    Requirements = requirements.ToList(),
                    ExportType = exportType,
                    OutputPath = outputPath,
                    Success = true,
                    ExportTime = TimeSpan.FromSeconds(1)
                });

                HideProgress();
                ShowNotification($"Requirements exported successfully to {outputPath}", DomainNotificationType.Success);

                _logger.LogInformation("Requirements export completed: {Count} requirements to {OutputPath}",
                    requirements.Count, outputPath);

                return true;
            }
            catch (Exception ex)
            {
                HideProgress();
                ShowNotification($"Export failed: {ex.Message}", DomainNotificationType.Error);

                _logger.LogError(ex, "Requirements export failed to {OutputPath}", outputPath);
                return false;
            }
        }

        public void ClearRequirements()
        {
            if (_requirements.Count == 0) return;

            var clearedRequirements = _requirements.ToList();
            Application.Current.Dispatcher.Invoke(() =>
            {
                _requirements.Clear();
            });

            CurrentRequirement = null;

            PublishEvent(new RequirementsEvents.RequirementsCollectionChanged
            {
                Action = "Clear",
                AffectedRequirements = clearedRequirements,
                NewCount = 0
            });

            IsDirty = true;
            _logger.LogDebug("Cleared {Count} requirements", clearedRequirements.Count);
        }

        public void AddRequirement(Requirement requirement)
        {
            if (requirement == null) throw new ArgumentNullException(nameof(requirement));

            Application.Current.Dispatcher.Invoke(() =>
            {
                _requirements.Add(requirement);
            });

            PublishEvent(new RequirementsEvents.RequirementsCollectionChanged
            {
                Action = "Add",
                AffectedRequirements = new List<Requirement> { requirement },
                NewCount = _requirements.Count
            });

            IsDirty = true;
            _logger.LogDebug("Added requirement: {RequirementId}", requirement.GlobalId);
        }

        public void RemoveRequirement(Requirement requirement)
        {
            if (requirement == null) throw new ArgumentNullException(nameof(requirement));

            bool removed = false;
            Application.Current.Dispatcher.Invoke(() =>
            {
                removed = _requirements.Remove(requirement);
            });

            if (removed)
            {
                if (CurrentRequirement == requirement)
                {
                    CurrentRequirement = _requirements.FirstOrDefault();
                }

                PublishEvent(new RequirementsEvents.RequirementsCollectionChanged
                {
                    Action = "Remove",
                    AffectedRequirements = new List<Requirement> { requirement },
                    NewCount = _requirements.Count
                });

                IsDirty = true;
                _logger.LogDebug("Removed requirement: {RequirementId}", requirement.GlobalId);
            }
        }

        public void UpdateRequirement(Requirement requirement, IReadOnlyList<string> modifiedFields)
        {
            if (requirement == null) throw new ArgumentNullException(nameof(requirement));

            _logger.LogInformation("[RequirementsMediator] UpdateRequirement called for {RequirementId}, publishing RequirementUpdated event", 
                requirement.GlobalId);
            
            PublishEvent(new RequirementsEvents.RequirementUpdated
            {
                Requirement = requirement,
                ModifiedFields = modifiedFields?.ToList() ?? new List<string>(),
                UpdatedBy = "UserEdit"
            });

            IsDirty = true;
            _logger.LogDebug("Updated requirement: {RequirementId}, Fields: {Fields}",
                requirement.GlobalId, string.Join(", ", modifiedFields ?? Array.Empty<string>()));
        }

        // ===== REQUIREMENT SELECTION =====

        public void SelectRequirement(Requirement requirement)
        {
            if (requirement == null) throw new ArgumentNullException(nameof(requirement));

            CurrentRequirement = requirement;
            _logger.LogDebug("Requirement selected: {RequirementId}", requirement.GlobalId);
        }

        public bool NavigateToNext()
        {
            if (_currentRequirement == null || _requirements.Count == 0) return false;

            var currentIndex = _requirements.IndexOf(_currentRequirement);
            if (currentIndex >= 0 && currentIndex < _requirements.Count - 1)
            {
                CurrentRequirement = _requirements[currentIndex + 1];
                return true;
            }

            return false;
        }

        public bool NavigateToPrevious()
        {
            if (_currentRequirement == null || _requirements.Count == 0) return false;

            var currentIndex = _requirements.IndexOf(_currentRequirement);
            if (currentIndex > 0)
            {
                CurrentRequirement = _requirements[currentIndex - 1];
                return true;
            }

            return false;
        }

        public int GetCurrentRequirementIndex()
        {
            if (_currentRequirement == null) return -1;
            return _requirements.IndexOf(_currentRequirement);
        }

        // ===== ANALYSIS FUNCTIONALITY =====

        public async Task<bool> AnalyzeRequirementAsync(Requirement requirement)
        {
            if (requirement == null) throw new ArgumentNullException(nameof(requirement));
            var analysisAttemptId = Guid.NewGuid().ToString("N")[..8];

            _logger.LogInformation("[ANALYSIS_TRACE] MEDIATOR_START Attempt={AttemptId} Requirement={RequirementId} Item={Item}",
                analysisAttemptId,
                requirement.GlobalId ?? "null",
                requirement.Item ?? "null");

            IsAnalyzing = true;
            ShowProgress($"Analyzing requirement {requirement.GlobalId}...", 0);

            PublishEvent(new RequirementsEvents.RequirementAnalysisStarted
            {
                Requirement = requirement,
                AnalysisType = "Quality"
            });

            var startTime = DateTime.Now;

            try
            {
                UpdateProgress("Running LLM analysis...", 50);

                RequirementAnalysis analysis;

                // ENHANCED SYSTEM: ALWAYS use new Requirements domain analysis engine (no fallback)
                if (_analysisEngine != null)
                {
                    _logger.LogInformation("[RequirementsMediator] Using ENHANCED Requirements domain analysis engine (no fallback enabled)");
                    analysis = await _analysisEngine.AnalyzeRequirementAsync(requirement, 
                        progress => OnAnalysisProgressUpdate(requirement, progress));
                }
                else
                {
                    _logger.LogError("[RequirementsMediator] ENHANCED SYSTEM FAILURE: Analysis engine not available - throwing exception instead of fallback");
                    throw new InvalidOperationException("Enhanced Requirements analysis engine is required but not available. Fallback to legacy system disabled.");
                }

                // Store analysis duration
                var duration = DateTime.Now - startTime;
                analysis.AnalysisDurationSeconds = duration.TotalSeconds;

                requirement.Analysis = analysis;
                var analysisSucceeded = analysis?.IsAnalyzed == true;
                var analysisErrorMessage = analysis?.ErrorMessage ?? "Analysis did not return a valid result.";
                _logger.LogInformation("[ANALYSIS_TRACE] MEDIATOR_ANALYSIS_READY Attempt={AttemptId} Requirement={RequirementId} IsAnalyzed={IsAnalyzed} Score={Score} Issues={IssueCount} Recs={RecCount}",
                    analysisAttemptId,
                    requirement.GlobalId ?? "null",
                    analysis?.IsAnalyzed ?? false,
                    analysis?.OriginalQualityScore ?? 0,
                    analysis?.Issues?.Count ?? 0,
                    analysis?.Recommendations?.Count ?? 0);

                PublishEvent(new RequirementsEvents.RequirementAnalyzed
                {
                    Requirement = requirement,
                    Analysis = analysis,
                    Success = analysisSucceeded,
                    AnalysisTime = duration,
                    ErrorMessage = analysisSucceeded ? null : analysisErrorMessage
                });

                // Publish RequirementUpdated to mark workspace dirty
                PublishEvent(new RequirementsEvents.RequirementUpdated
                {
                    Requirement = requirement,
                    ModifiedFields = new List<string> { "Analysis" },
                    UpdatedBy = "RequirementsMediator.AnalyzeRequirementAsync"
                });

                IsDirty = true;
                HideProgress();
                if (analysisSucceeded)
                {
                    ShowNotification($"Analysis completed for {requirement.GlobalId}", DomainNotificationType.Success);
                }
                else
                {
                    ShowNotification($"Analysis failed: {analysisErrorMessage}", DomainNotificationType.Error);
                }

                _logger.LogInformation("Requirement analysis completed for {RequirementId}", requirement.GlobalId);
                if (analysisSucceeded)
                {
                    _logger.LogInformation("[ANALYSIS_TRACE] MEDIATOR_SUCCESS Attempt={AttemptId} Requirement={RequirementId}",
                        analysisAttemptId,
                        requirement.GlobalId ?? "null");
                }
                else
                {
                    _logger.LogWarning("[ANALYSIS_TRACE] MEDIATOR_FAIL_RESULT Attempt={AttemptId} Requirement={RequirementId} Error={Error}",
                        analysisAttemptId,
                        requirement.GlobalId ?? "null",
                        analysisErrorMessage);
                }
                
                // Auto-save after successful analysis
                try
                {
                    await _newProjectMediator.SaveProjectAsync();
                    _logger.LogInformation("Auto-saved workspace after requirement analysis for {RequirementId}", requirement.GlobalId);
                }
                catch (Exception saveEx)
                {
                    _logger.LogWarning(saveEx, "Auto-save failed after analysis for {RequirementId}", requirement.GlobalId);
                    // Don't fail the analysis operation if save fails
                }
                
                return analysisSucceeded;
            }
            catch (Exception ex)
            {
                HideProgress();
                ShowNotification($"Analysis failed: {ex.Message}", DomainNotificationType.Error);
                _logger.LogError(ex, "[ANALYSIS_TRACE] MEDIATOR_FAIL Attempt={AttemptId} Requirement={RequirementId}",
                    analysisAttemptId,
                    requirement.GlobalId ?? "null");

                PublishEvent(new RequirementsEvents.RequirementAnalyzed
                {
                    Requirement = requirement,
                    Analysis = null,
                    Success = false,
                    AnalysisTime = TimeSpan.Zero,
                    ErrorMessage = ex.Message
                });

                _logger.LogError(ex, "Requirement analysis failed for {RequirementId}", requirement.GlobalId);
                return false;
            }
            finally
            {
                IsAnalyzing = false;
                _logger.LogInformation("[ANALYSIS_TRACE] MEDIATOR_END Attempt={AttemptId} Requirement={RequirementId} IsAnalyzing={IsAnalyzing}",
                    analysisAttemptId,
                    requirement.GlobalId ?? "null",
                    IsAnalyzing);
            }
        }

        /// <summary>
        /// Handles progress updates from the analysis engine and publishes AnalysisProgress events.
        /// Progress format: "Stage|Message|Percentage" where Stage is one of: Uploading, Processing, Extracting
        /// </summary>
        private void OnAnalysisProgressUpdate(Requirement requirement, string progressMessage)
        {
            if (string.IsNullOrEmpty(progressMessage))
                return;

            try
            {
                // Parse the structured progress message
                var parts = progressMessage.Split('|');
                if (parts.Length < 3)
                {
                    // Fallback for non-structured messages
                    UpdateProgress(progressMessage, 75);
                    return;
                }

                var stage = parts[0].Trim(); // "Uploading", "Processing", "Extracting"
                var message = parts[1].Trim();
                if (!int.TryParse(parts[2].Trim(), out var percentage))
                    percentage = 75;

                // Update the UI progress bar via mediator
                UpdateProgress(message, percentage);

                // Publish the structured AnalysisProgress event
                PublishEvent(new RequirementsEvents.AnalysisProgress
                {
                    Requirement = requirement,
                    Stage = stage,
                    PercentComplete = percentage,
                    StatusMessage = message
                });

                _logger.LogDebug("[AnalysisProgress] Stage={Stage} Percentage={Percent}% Message={Message}", 
                    stage, percentage, message);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[AnalysisProgress] Failed to parse progress message: {Message}", progressMessage);
                UpdateProgress(progressMessage, 75);
            }
        }

        public async Task<bool> AnalyzeBatchRequirementsAsync(IReadOnlyList<Requirement> requirements)
        {
            if (requirements == null) throw new ArgumentNullException(nameof(requirements));
            if (!requirements.Any()) return true;

            IsAnalyzing = true;
            ShowProgress("Starting batch analysis...", 0);

            PublishEvent(new RequirementsEvents.BatchOperationStarted
            {
                OperationType = "Analysis",
                TargetRequirements = requirements.ToList()
            });

            var successful = 0;
            var failed = 0;
            var errors = new List<string>();

            try
            {
                for (int i = 0; i < requirements.Count; i++)
                {
                    var requirement = requirements[i];
                    var progress = (double)(i + 1) / requirements.Count * 100;

                    UpdateProgress($"Analyzing {requirement.GlobalId}... ({i + 1}/{requirements.Count})", progress);

                    try
                    {
                        RequirementAnalysis analysis;
                        
                        // ARCHITECTURE: Prefer new Requirements domain engine when available
                        if (_analysisEngine != null)
                        {
                            analysis = await _analysisEngine.AnalyzeRequirementAsync(requirement, 
                                progressMsg => UpdateProgress($"Analyzing {requirement.GlobalId}... ({i + 1}/{requirements.Count}) - {progressMsg}", (double)(i + 1) / requirements.Count * 100));
                        }
                        else
                        {
                            analysis = await _analysisService.AnalyzeRequirementAsync(requirement);
                        }

                        requirement.Analysis = analysis;
                        successful++;
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        errors.Add($"{requirement.GlobalId}: {ex.Message}");
                        _logger.LogError(ex, "Batch analysis failed for requirement {RequirementId}", requirement.GlobalId);
                    }
                }

                PublishEvent(new RequirementsEvents.BatchOperationCompleted
                {
                    OperationType = "Analysis",
                    TargetRequirements = requirements.ToList(),
                    SuccessCount = successful,
                    FailureCount = failed,
                    Errors = errors,
                    Duration = TimeSpan.FromSeconds(requirements.Count * 2) // Placeholder
                });

                if (successful > 0)
                {
                    IsDirty = true;
                }

                HideProgress();

                if (failed == 0)
                {
                    ShowNotification($"Batch analysis completed successfully: {successful} requirements", DomainNotificationType.Success);
                }
                else
                {
                    ShowNotification($"Batch analysis completed: {successful} successful, {failed} failed", DomainNotificationType.Warning);
                }

                _logger.LogInformation("Batch analysis completed: {Successful} successful, {Failed} failed", successful, failed);
                return failed == 0;
            }
            finally
            {
                IsAnalyzing = false;
            }
        }

        public async Task<bool> AnalyzeUnanalyzedRequirementsAsync()
        {
            var unanalyzed = _requirements.Where(r => r.Analysis == null).ToList();
            if (!unanalyzed.Any())
            {
                ShowNotification("All requirements are already analyzed", DomainNotificationType.Info);
                return true;
            }

            return await AnalyzeBatchRequirementsAsync(unanalyzed.AsReadOnly());
        }

        public async Task<bool> ReAnalyzeModifiedRequirementsAsync()
        {
            // TODO: Track modified requirements and re-analyze them
            var modifiedRequirements = _requirements.Where(r => r.Analysis != null /* && r.IsModified */).ToList();
            
            if (!modifiedRequirements.Any())
            {
                ShowNotification("No modified requirements found", DomainNotificationType.Info);
                return true;
            }

            return await AnalyzeBatchRequirementsAsync(modifiedRequirements.AsReadOnly());
        }

        // ===== SEARCH & FILTERING =====

        public IReadOnlyList<Requirement> SearchRequirements(string searchText)
        {
            if (string.IsNullOrWhiteSpace(searchText)) return Array.Empty<Requirement>();

            var results = _requirements
                .Where(r => r.Item?.Contains(searchText, StringComparison.OrdinalIgnoreCase) == true)
                .ToList();

            _logger.LogDebug("Search for '{SearchText}' returned {Count} results", searchText, results.Count);
            return results.AsReadOnly();
        }

        public IReadOnlyList<Requirement> FilterByAnalysisStatus(bool analyzed)
        {
            var results = _requirements
                .Where(r => analyzed ? r.Analysis != null : r.Analysis == null)
                .ToList();

            return results.AsReadOnly();
        }

        public IReadOnlyList<Requirement> FilterByVerificationMethod(VerificationMethod method)
        {
            var results = _requirements
                .Where(r => r.Method == method)
                .ToList();

            return results.AsReadOnly();
        }

        // ===== VALIDATION =====

        public async Task<ValidationResult> ValidateRequirementAsync(Requirement requirement)
        {
            if (requirement == null) throw new ArgumentNullException(nameof(requirement));

            var result = new ValidationResult();
            
            // Basic validation rules
            if (string.IsNullOrWhiteSpace(requirement.Name))
                result.Errors.Add("Requirement name is required");

            if (string.IsNullOrWhiteSpace(requirement.Description))
                result.Errors.Add("Requirement description is required");

            if (string.IsNullOrWhiteSpace(requirement.GlobalId))
                result.Errors.Add("Requirement ID is required");

            // TODO: Add more sophisticated validation rules

            result.IsValid = !result.Errors.Any();
            await Task.CompletedTask;

            return result;
        }

        public async Task<ValidationResult> ValidateAllRequirementsAsync()
        {
            var overallResult = new ValidationResult { IsValid = true };

            foreach (var requirement in _requirements)
            {
                var result = await ValidateRequirementAsync(requirement);
                if (!result.IsValid)
                {
                    overallResult.IsValid = false;
                    overallResult.Errors.AddRange(result.Errors.Select(e => $"{requirement.GlobalId}: {e}"));
                }
            }

            return overallResult;
        }

        // ===== PROJECT INTEGRATION =====

        public async Task<bool> LoadFromProjectAsync(Workspace workspace)
        {
            _logger.LogInformation("📥 RequirementsMediator.LoadFromProjectAsync called - workspace.Requirements.Count: {Count}", 
                workspace?.Requirements?.Count ?? 0);
                
            if (workspace?.Requirements == null) 
            {
                _logger.LogWarning("⚠️ RequirementsMediator.LoadFromProjectAsync: workspace or workspace.Requirements is null");
                return false;
            }

            // Drop known synthetic health-check requirement if it was accidentally persisted.
            var filteredRequirements = workspace.Requirements
                .Where(requirement => requirement != null && !IsSyntheticHealthCheckRequirement(requirement))
                .ToList();

            var droppedSyntheticCount = workspace.Requirements.Count - filteredRequirements.Count;
            if (droppedSyntheticCount > 0)
            {
                _logger.LogWarning("⚠️ RequirementsMediator filtered {Count} synthetic health-check requirement(s) from project load", droppedSyntheticCount);
                workspace.Requirements = filteredRequirements;
            }

            try
            {
                // CRITICAL FIX: Do sorting/filtering on background thread, NOT on UI thread
                // This prevents the UI from blocking while 818+ requirements are sorted
                var sortedRequirements = await Task.Run(() =>
                {
                    var sorted = filteredRequirements.OrderBy(r => r, new RequirementNaturalComparer()).ToList();
                    _logger.LogInformation("📊 RequirementsMediator: Sorted {Count} requirements on background thread", sorted.Count);
                    return sorted;
                });

                // NOW invoke to UI thread only for collection updates
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    // Check if data is already loaded and current - avoid unnecessary reload!
                    if (_requirements.Count == sortedRequirements.Count && 
                        _requirements.SequenceEqual(sortedRequirements, new RequirementEqualityComparer()))
                    {
                        _logger.LogInformation("🔄 RequirementsMediator: Data already current, preserving navigation state. CurrentRequirement: {Current}", 
                            CurrentRequirement?.Item ?? "none");
                        
                        // ✅ Ensure CurrentRequirement is set to first requirement if null but requirements exist
                        if (CurrentRequirement == null && _requirements.Count > 0)
                        {
                            CurrentRequirement = _requirements.First();
                            _logger.LogDebug("Set CurrentRequirement to first requirement: {Item}", CurrentRequirement.Item);
                        }
                        
                        // ✅ CRITICAL: Publish RequirementSelected event to refresh UI with current analysis data
                        // This ensures ViewModels display persisted analysis even when data is "already current"
                        if (CurrentRequirement != null)
                        {
                            PublishEvent(new RequirementsEvents.RequirementSelected
                            {
                                Requirement = CurrentRequirement,
                                SelectedBy = "ProjectActivation"
                            });
                            _logger.LogInformation("🔔 RequirementsMediator publishing RequirementSelected for project activation: {Item}", CurrentRequirement.Item);
                        }
                        
                        var eventData = new RequirementsEvents.RequirementsCollectionChanged
                        {
                            Action = "ProjectActivated",
                            AffectedRequirements = _requirements.ToList(),
                            NewCount = _requirements.Count
                        };
                        _logger.LogInformation("🔔 RequirementsMediator publishing RequirementsCollectionChanged: {Action}, Count: {Count}", eventData.Action, eventData.NewCount);
                        PublishEvent(eventData);
                    }
                    else
                    {
                        _logger.LogInformation("📊 RequirementsMediator: Reloading requirements data on UI thread (count changed or different data)");
                        
                        // Preserve current requirement if possible
                        var previousCurrentRequirement = CurrentRequirement;
                        
                        _requirements.Clear();
                        foreach (var requirement in sortedRequirements)
                        {
                            _requirements.Add(requirement);
                        }
                        
                        if (_requirements.Count > 0)
                        {
                            // Try to preserve current requirement position
                            if (previousCurrentRequirement != null)
                            {
                                var matchingReq = _requirements.FirstOrDefault(r => 
                                    r.Item == previousCurrentRequirement.Item || 
                                    r.Name == previousCurrentRequirement.Name);
                                CurrentRequirement = matchingReq ?? _requirements.First();
                            }
                            else
                            {
                                CurrentRequirement = _requirements.First();
                            }
                            
                            // CRITICAL: Notify ViewModels about the selected requirement
                            PublishEvent(new RequirementsEvents.RequirementSelected
                            {
                                Requirement = CurrentRequirement
                            });
                        }

                        var loadEventData = new RequirementsEvents.RequirementsCollectionChanged
                        {
                            Action = "Load",
                            AffectedRequirements = _requirements.ToList(),
                            NewCount = _requirements.Count
                        };
                        _logger.LogInformation("🔔 RequirementsMediator publishing RequirementsCollectionChanged: {Action}, Count: {Count}", loadEventData.Action, loadEventData.NewCount);
                        PublishEvent(loadEventData);
                        
                        IsDirty = false;
                    }
                    
                    // ✅ Always notify about requirement selection to update header (even if null)
                    PublishEvent(new RequirementsEvents.RequirementSelected
                    {
                        Requirement = CurrentRequirement!
                    });
                    
                    _logger.LogInformation("Loaded {Count} requirements from project", _requirements.Count);
                });

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load requirements from project");
                return false;
            }
        }

        private static bool IsSyntheticHealthCheckRequirement(Requirement requirement)
        {
            if (requirement == null)
            {
                return false;
            }

            var isTestItem = string.Equals(requirement.Item?.Trim(), "TEST", StringComparison.OrdinalIgnoreCase);
            var isHealthCheckName = string.Equals(requirement.Name?.Trim(), "Health Check", StringComparison.OrdinalIgnoreCase);
            var hasHealthCheckDescription = requirement.Description?.IndexOf("health validation", StringComparison.OrdinalIgnoreCase) >= 0;

            return isTestItem && isHealthCheckName && hasHealthCheckDescription;
        }

        public async Task<bool> SaveToProjectAsync()
        {
            // TODO: Implement save to project functionality
            await Task.CompletedTask;
            
            IsDirty = false;
            ShowNotification("Requirements saved to project", DomainNotificationType.Success);
            
            return true;
        }

        public string CurrentProjectName
        {
            get
            {
                var currentWorkspace = _workspaceContext.CurrentWorkspace;
                var workspaceInfo = _workspaceContext.CurrentWorkspaceInfo;
                
                // First try workspace name
                if (!string.IsNullOrEmpty(workspaceInfo?.Name))
                {
                    return workspaceInfo.Name;
                }
                
                // Then try Jama test plan name  
                if (!string.IsNullOrEmpty(currentWorkspace?.JamaTestPlan))
                {
                    return currentWorkspace.JamaTestPlan;
                }
                
                // Fall back to project ID if available
                if (!string.IsNullOrEmpty(currentWorkspace?.JamaProject) && 
                    int.TryParse(currentWorkspace.JamaProject, out var projectId))
                {
                    return $"Project {projectId}";
                }
                
                // Final fallback
                return "Unknown Project";
            }
        }

        public int CurrentProjectId
        {
            get
            {
                var currentWorkspace = _workspaceContext.CurrentWorkspace;
                
                if (!string.IsNullOrEmpty(currentWorkspace?.JamaProject))
                {
                    // Only try parsing as numeric ID (new format) - no blocking async calls in property
                    if (int.TryParse(currentWorkspace.JamaProject, out var projectId) && projectId > 0)
                    {
                        return projectId;
                    }
                    
                    // For project keys, caller must use GetCurrentProjectIdAsync() to avoid deadlock
                    _logger?.LogWarning("CurrentProjectId accessed with project key '{ProjectKey}' - use GetCurrentProjectIdAsync() instead", currentWorkspace.JamaProject);
                }
                
                return -1; // No valid numeric project ID available synchronously
            }
        }

        /// <summary>
        /// Gets the current project ID from canonical numeric workspace metadata.
        /// </summary>
        public async Task<int> GetCurrentProjectIdAsync()
        {
            var currentWorkspace = _workspaceContext.CurrentWorkspace;
            
            if (!string.IsNullOrEmpty(currentWorkspace?.JamaProject))
            {
                // First try parsing as numeric ID (new format)
                if (int.TryParse(currentWorkspace.JamaProject, out var projectId) && projectId > 0)
                {
                    return projectId;
                }
            }
            
            await Task.CompletedTask;
            return -1; // No valid project ID
        }

        public void UpdateProjectContext(string? projectName)
        {
            _logger.LogDebug("Project context updated: {ProjectName}", projectName ?? "No Project");
        }

        // ===== CROSS-DOMAIN COMMUNICATION =====

        public override void BroadcastToAllDomains<T>(T notification) where T : class
        {
            base.BroadcastToAllDomains(notification);
        }

        public void HandleBroadcastNotification<T>(T notification) where T : class
        {
            _logger.LogInformation("Received broadcast notification: {NotificationType}", typeof(T).Name);

            // Handle project-related events
            if (notification is NewProjectEvents.ProjectCreated projectCreated)
            {
                if (projectCreated.Workspace != null)
                {
                    _ = LoadFromProjectAsync(projectCreated.Workspace);
                    // Trigger automatic RAG document sync for analysis service
                    _analysisService?.SetWorkspaceContext(projectCreated.WorkspaceName);
                    
                    // NOTE: WorkspaceContext notification is handled by NewProjectMediator
                    // No need for explicit refresh - proper architectural separation
                }
            }
            else if (notification is OpenProjectEvents.ProjectOpened openProjectOpened)
            {
                _logger.LogInformation("🔔 RequirementsMediator: Handling OpenProjectEvents.ProjectOpened - WorkspaceName: {WorkspaceName}", openProjectOpened.WorkspaceName);
                if (openProjectOpened.Workspace != null)
                {
                    _logger.LogInformation("🚀 RequirementsMediator: About to call LoadFromProjectAsync for workspace with {RequirementCount} requirements", 
                        openProjectOpened.Workspace.Requirements?.Count ?? 0);
                    
                    // NOTE: ViewModels are created at mediator construction, so they already exist and are subscribed
                    // No need for manual event delivery - proper event-driven architecture
                    
                    _ = LoadFromProjectAsync(openProjectOpened.Workspace);
                    // Trigger automatic RAG document sync for analysis service
                    _analysisService?.SetWorkspaceContext(openProjectOpened.WorkspaceName);
                    
                    // NOTE: WorkspaceContext notification is handled by NewProjectMediator
                    // No need for explicit refresh - proper architectural separation
                }
                else
                {
                    _logger.LogWarning("⚠️ RequirementsMediator: OpenProjectEvents.ProjectOpened has null Workspace");
                }
            }
            else if (notification is NewProjectEvents.ProjectClosed)
            {
                ClearRequirements();
                IsDirty = false;
            }
            else if (notification is TestCaseEditorApp.MVVM.Events.CrossDomainMessages.ImportRequirementsRequest importRequest)
            {
                _ = ImportRequirementsAsync(importRequest.DocumentPath, importRequest.PreferJamaParser ? "Jama" : "Auto");
            }
            // Handle cross-domain project creation notifications for attachment scanning
            else if (notification is TestCaseEditorApp.MVVM.Events.CrossDomainMessages.ProjectCreatedNotification projectCreatedNotification)
            {
                _logger.LogInformation("[RequirementsMediator] Processing cross-domain ProjectCreatedNotification for attachment scanning");
                _ = Task.Run(async () => await HandleProjectCreatedNotificationAsync(projectCreatedNotification));
            }
        }

        /// <summary>
        /// Determine if the current data source is from Jama Connect
        /// Used by ViewConfigurationService for proper view routing
        /// </summary>
        public bool IsJamaDataSource()
        {
            // Use centralized workspace context for clean access
            var currentWorkspace = _workspaceContext.CurrentWorkspace;
            
            TestCaseEditorApp.Services.Logging.Log.Debug($"[RequirementsMediator] IsJamaDataSource() - currentWorkspace: {(currentWorkspace == null ? "NULL" : "exists")}");
            if (currentWorkspace != null)
            {
                TestCaseEditorApp.Services.Logging.Log.Debug($"[RequirementsMediator] ImportSource: '{currentWorkspace.ImportSource ?? "NULL"}'");
            }
            
            if (currentWorkspace == null)
            {
                TestCaseEditorApp.Services.Logging.Log.Debug("[RequirementsMediator] IsJamaDataSource() returning false (no workspace)");
                return false;
            }
            
            // Treat any workspace with a Jama project association as Jama-capable,
            // regardless of how the project was originally created.
            bool hasJamaAssociation = currentWorkspace.JamaProjectId.HasValue ||
                                      (!string.IsNullOrWhiteSpace(currentWorkspace.JamaProject) &&
                                       int.TryParse(currentWorkspace.JamaProject, out _));

            if (hasJamaAssociation)
            {
                TestCaseEditorApp.Services.Logging.Log.Debug("[RequirementsMediator] IsJamaDataSource() returning true (Jama project association present)");
                return true;
            }

            // Default to document view if no Jama association and no import marker.
            TestCaseEditorApp.Services.Logging.Log.Debug("[RequirementsMediator] IsJamaDataSource() returning false (no Jama association)");
            return false;
        }

        /// <summary>
        /// Navigate to Requirements Search in Attachments feature
        /// Following Architectural Guide AI patterns for domain-specific navigation
        /// </summary>
        public void NavigateToRequirementsSearchAttachments()
        {
            try
            {
                _logger.LogInformation("[RequirementsMediator] Navigating to Requirements Search in Attachments");
                
                // Publish domain event to coordinate view change within Requirements domain
                // This follows the Architectural Guide AI pattern for internal domain navigation
                var navigationEvent = new RequirementsEvents.NavigateToAttachmentSearch
                {
                    Timestamp = DateTime.Now,
                    TargetView = "RequirementsSearchAttachments"
                };
                
                PublishEvent(navigationEvent);
                
                _logger.LogInformation("[RequirementsMediator] Navigation event published successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RequirementsMediator] Error navigating to Requirements Search in Attachments");
            }
        }
        
        /// <summary>
        /// Trigger background attachment scanning for the specified project
        /// Called from OpenProject domain when automatic scanning is needed
        /// ARCHITECTURAL COMPLIANCE: Uses mediator's own methods instead of service provider lookup
        /// </summary>
        public async Task TriggerBackgroundAttachmentScanAsync(int projectId)
        {
            try
            {
                _logger.LogInformation("[RequirementsMediator] Triggering background attachment scan for project {ProjectId}", projectId);
                
                // Use our own mediator method instead of calling ViewModel directly
                var attachments = await ScanProjectAttachmentsAsync(projectId);
                
                _logger.LogInformation("[RequirementsMediator] Background attachment scan completed for project {ProjectId} - found {Count} attachments", 
                    projectId, attachments.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RequirementsMediator] Error triggering background attachment scan for project {ProjectId}", projectId);
            }
        }

        /// <summary>
        /// Notify about attachment scan progress updates
        /// </summary>
        public void NotifyAttachmentScanProgress(string progressText)
        {
            try
            {
                PublishEvent(new TestCaseEditorApp.MVVM.Domains.Requirements.Events.RequirementsEvents.AttachmentScanProgress
                {
                    ProgressText = progressText,
                    Timestamp = DateTime.Now
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RequirementsMediator] Error notifying attachment scan progress");
            }
        }



        /// <summary>
        /// Scan project attachments and return results with progress reporting
        /// Proper mediator method that replaces direct ViewModel service calls
        /// </summary>
        public async Task<List<JamaAttachment>> ScanProjectAttachmentsAsync(int projectId, IProgress<AttachmentScanProgressData>? progress = null, CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogInformation("[RequirementsMediator] Starting attachment scan for project {ProjectId}", projectId);
                
                // Get project name from current workspace if available, or fetch from Jama API
                var currentWorkspace = _workspaceContext.CurrentWorkspace;
                var projectName = currentWorkspace?.JamaTestPlan ?? currentWorkspace?.JamaProject;
                
                // If no project name in workspace, try to fetch it from Jama API
                if (string.IsNullOrEmpty(projectName))
                {
                    try
                    {
                        var projects = await _jamaConnectService.GetProjectsAsync();
                        var project = projects.FirstOrDefault(p => p.Id == projectId);
                        projectName = project?.Name ?? $"Project {projectId}";
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "[RequirementsMediator] Failed to fetch project name from Jama API for project {ProjectId}", projectId);
                        projectName = $"Project {projectId}";
                    }
                }
                
                // Publish start event
                PublishEvent(new RequirementsEvents.AttachmentScanStarted
                {
                    ProjectId = projectId,
                    ProjectName = projectName
                });

                var startTime = DateTime.Now;
                var scanTimedOut = false;
                string? completionErrorMessage = null;

                // Fast path: limited scan for responsive UI. Fall back to full scan if nothing is found.
                progress?.Report(new AttachmentScanProgressData
                {
                    Current = 0,
                    Total = 1,
                    ProgressText = $"Searching {projectName} for attachments | quick scan"
                });

                PublishEvent(new RequirementsEvents.AttachmentScanProgress
                {
                    ProjectId = projectId,
                    ProgressText = $"Searching {projectName} for attachments | quick scan"
                });

                var attachments = await _jamaConnectService.GetProjectAttachmentsLimitedAsync(
                    projectId,
                    maxItems: 20,
                    cancellationToken: cancellationToken,
                    progressCallback: (current, total, progressData) =>
                    {
                        progress?.Report(new AttachmentScanProgressData
                        {
                            Current = current,
                            Total = total,
                            ProgressText = progressData
                        });

                        PublishEvent(new RequirementsEvents.AttachmentScanProgress
                        {
                            ProjectId = projectId,
                            ProgressText = progressData
                        });
                    },
                    projectName: projectName);

                if (attachments.Count == 0)
                {
                    _logger.LogInformation("[RequirementsMediator] Quick scan found no attachments for project {ProjectId}; falling back to full scan", projectId);

                    progress?.Report(new AttachmentScanProgressData
                    {
                        Current = 0,
                        Total = 1,
                        ProgressText = $"Searching {projectName} for attachments | full scan"
                    });

                    PublishEvent(new RequirementsEvents.AttachmentScanProgress
                    {
                        ProjectId = projectId,
                        ProgressText = $"Searching {projectName} for attachments | full scan"
                    });

                    using var fullScanTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    fullScanTimeoutCts.CancelAfter(TimeSpan.FromMinutes(2));

                    // Fallback to full scan when quick scan yields no results
                    try
                    {
                        attachments = await _jamaConnectService.GetProjectAttachmentsAsync(projectId, fullScanTimeoutCts.Token, (current, total, progressData) =>
                        {
                            // Report progress to caller
                            progress?.Report(new AttachmentScanProgressData
                            {
                                Current = current,
                                Total = total,
                                ProgressText = progressData
                            });

                            // Also publish progress event for other subscribers
                            PublishEvent(new RequirementsEvents.AttachmentScanProgress
                            {
                                ProjectId = projectId,
                                ProgressText = progressData
                            });
                        }, projectName);
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && fullScanTimeoutCts.IsCancellationRequested)
                    {
                        _logger.LogWarning("[RequirementsMediator] Full scan timed out for project {ProjectId} after {TimeoutMinutes} minutes", projectId, 2);

                        var timeoutMessage = $"Searching {projectName} for attachments | full scan timed out after 2 minutes";
                        progress?.Report(new AttachmentScanProgressData
                        {
                            Current = 1,
                            Total = 1,
                            ProgressText = timeoutMessage
                        });

                        PublishEvent(new RequirementsEvents.AttachmentScanProgress
                        {
                            ProjectId = projectId,
                            ProgressText = timeoutMessage
                        });

                        scanTimedOut = true;
                        completionErrorMessage = "Full scan timed out after 2 minutes. The project may still contain attachments beyond the preview scan.";
                        attachments = new List<JamaAttachment>();
                    }
                }

                var duration = DateTime.Now - startTime;

                // Publish completion event
                PublishEvent(new RequirementsEvents.AttachmentScanCompleted
                {
                    ProjectId = projectId,
                    AttachmentCount = attachments?.Count ?? 0,
                    Success = !scanTimedOut,
                    ErrorMessage = completionErrorMessage,
                    Duration = duration,
                    Attachments = attachments ?? new List<JamaAttachment>()
                });

                _logger.LogInformation("[RequirementsMediator] Attachment scan completed for project {ProjectId} - found {Count} attachments", 
                    projectId, attachments?.Count ?? 0);

                return attachments ?? new List<JamaAttachment>();
            }
            catch (HttpRequestException httpEx) when (httpEx.InnerException is System.Net.Sockets.SocketException socketEx && socketEx.SocketErrorCode == System.Net.Sockets.SocketError.HostNotFound)
            {
                _logger.LogError(httpEx, "[RequirementsMediator] Network connectivity error - cannot resolve Jama server hostname for project {ProjectId}", projectId);

                // Publish failure event with specific network error
                PublishEvent(new RequirementsEvents.AttachmentScanCompleted
                {
                    ProjectId = projectId,
                    AttachmentCount = 0,
                    Success = false,
                    ErrorMessage = "Network connectivity error: Cannot resolve Jama server. Please check VPN connection.",
                    Duration = TimeSpan.Zero,
                    Attachments = new List<JamaAttachment>()
                });

                throw; // Re-throw to let caller handle with improved error message
            }
            catch (HttpRequestException httpEx)
            {
                _logger.LogError(httpEx, "[RequirementsMediator] HTTP request error scanning attachments for project {ProjectId}", projectId);

                // Publish failure event with network error
                PublishEvent(new RequirementsEvents.AttachmentScanCompleted
                {
                    ProjectId = projectId,
                    AttachmentCount = 0,
                    Success = false,
                    ErrorMessage = $"Network connection error: {httpEx.Message}",
                    Duration = TimeSpan.Zero,
                    Attachments = new List<JamaAttachment>()
                });

                throw; // Re-throw to let caller handle
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RequirementsMediator] Error scanning attachments for project {ProjectId}", projectId);

                // Publish failure event
                PublishEvent(new RequirementsEvents.AttachmentScanCompleted
                {
                    ProjectId = projectId,
                    AttachmentCount = 0,
                    Success = false,
                    ErrorMessage = ex.Message,
                    Duration = TimeSpan.Zero,
                    Attachments = new List<JamaAttachment>()
                });

                throw; // Re-throw to let caller handle
            }
        }

        /// <summary>
        /// Parse attachment for requirements using document parsing service
        /// </summary>
        public async Task<List<Requirement>> ParseAttachmentRequirementsAsync(JamaAttachment attachment, int projectId, System.Action<string>? progressCallback = null, System.Action<Requirement>? onRequirementDiscovered = null, CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogInformation("[ATTACHMENT_DIAG] START ParseAttachmentRequirementsAsync AttachmentId={AttachmentId} FileName={FileName} ProjectId={ProjectId}",
                    attachment.Id, attachment.FileName, projectId);

                _logger.LogInformation("[RequirementsMediator] Parsing requirements from attachment {AttachmentId} ({FileName}) in project {ProjectId}", 
                    attachment.Id, attachment.FileName, projectId);

                // Use real document parsing service with attachment metadata to avoid re-scanning
                var extractedRequirements = await _jamaDocumentParserService.ParseAttachmentAsync(attachment, projectId, progressCallback, onRequirementDiscovered, cancellationToken);

                _logger.LogInformation(
                    "[ATTACHMENT_TRACE] ParsedRequirements AttachmentId={AttachmentId} FileName={FileName} Count={Count} Sample={Sample}",
                    attachment.Id,
                    attachment.FileName,
                    extractedRequirements.Count,
                    BuildRequirementTraceSample(extractedRequirements));

                if (extractedRequirements.Count > 0)
                {
                    // ── Duplicate guard ──────────────────────────────────────────────────────
                    // Each derived requirement carries a stable TraceReference of the form
                    // "TRC-ATT{attachmentId}-..." which is embedded in its Rationale/description.
                    // Query Jama for any items that already contain this attachment's trace prefix
                    // so that re-scraping the same document does not create duplicates.
                    var tracePrefix = $"TRC-ATT{attachment.Id}-";
                    try
                    {
                        progressCallback?.Invoke($"🔎 Checking Jama for previously saved requirements from this document...");
                        var existingItems = await _jamaConnectService.SearchAbstractItemsAsync(
                            projectId,
                            contains: tracePrefix,
                            maxResults: 50);

                        if (existingItems.Count > 0)
                        {
                            _logger.LogInformation(
                                "[RequirementsMediator] Found {ExistingCount} items already in Jama with trace prefix '{Prefix}'. Filtering duplicates.",
                                existingItems.Count, tracePrefix);

                            // Build a set of trace references already present in Jama (from item name or description)
                            var existingTraceRefs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            foreach (var item in existingItems)
                            {
                                foreach (var field in new[] { item.Fields?.Name ?? string.Empty, item.Fields?.Description ?? string.Empty })
                                {
                                    var idx = field.IndexOf(tracePrefix, StringComparison.OrdinalIgnoreCase);
                                    if (idx >= 0)
                                    {
                                        var end = field.IndexOfAny(new[] { ' ', '\n', '\r', '<', '"' }, idx);
                                        var token = end >= 0 ? field.Substring(idx, end - idx) : field.Substring(idx);
                                        existingTraceRefs.Add(token.Trim());
                                    }
                                }
                            }

                            var before = extractedRequirements.Count;
                            extractedRequirements = extractedRequirements
                                .Where(r => string.IsNullOrWhiteSpace(r.TraceReference) || !existingTraceRefs.Contains(r.TraceReference))
                                .ToList();
                            var skipped = before - extractedRequirements.Count;

                            if (skipped > 0)
                            {
                                _logger.LogInformation(
                                    "[RequirementsMediator] Skipped {Skipped} duplicate requirements (already in Jama) for attachment {AttachmentId}.",
                                    skipped, attachment.Id);
                                progressCallback?.Invoke($"⏭️ Skipped {skipped} already-saved requirements (duplicates)");
                            }
                        }
                    }
                    catch (Exception dedupEx)
                    {
                        // Dedup check is best-effort; proceed with full save if the query fails
                        _logger.LogWarning(dedupEx, "[RequirementsMediator] Duplicate check failed for attachment {AttachmentId}. Proceeding without dedup.", attachment.Id);
                    }
                    // ────────────────────────────────────────────────────────────────────────

                    if (extractedRequirements.Count == 0)
                    {
                        progressCallback?.Invoke("✅ All requirements from this document are already saved in Jama — nothing to do.");
                        _logger.LogInformation("[RequirementsMediator] All requirements already exist in Jama for attachment {AttachmentId}.", attachment.Id);
                        return extractedRequirements;
                    }

                    progressCallback?.Invoke($"💾 Saving {extractedRequirements.Count} extracted requirements to Jama...");
                    
                    // Check for test container override (e.g., JAMA_TEST_CONTAINER_ID=19853308)
                    var testContainerIdEnv = Environment.GetEnvironmentVariable("JAMA_TEST_CONTAINER_ID");
                    var preferredParentContainerId = !string.IsNullOrWhiteSpace(testContainerIdEnv) && int.TryParse(testContainerIdEnv, out var testContainerId)
                        ? testContainerId
                        : (attachment.Item > 0 ? attachment.Item : (int?)null);
                    
                    if (!string.IsNullOrWhiteSpace(testContainerIdEnv) && int.TryParse(testContainerIdEnv, out _))
                    {
                        _logger.LogInformation("[RequirementsMediator] Using test container override: JAMA_TEST_CONTAINER_ID={TestContainerId}", testContainerIdEnv);
                        progressCallback?.Invoke($"🧪 Using test container: {preferredParentContainerId}");
                    }
                    
                    var (savedCount, failedCount) = await _jamaConnectService.ImportRequirementsToJamaAsync(
                        projectId,
                        extractedRequirements,
                        preferredParentContainerId,
                        cancellationToken,
                        (processed, total, failures, detail) =>
                        {
                            var suffix = string.IsNullOrWhiteSpace(detail) ? string.Empty : $" | {detail}";
                            progressCallback?.Invoke($"💾 Jama save progress: {processed}/{total} processed, {failures} failed{suffix}");
                        });

                    // Fail-closed posture: if any records failed, retry only unsaved requirements once more.
                    if (failedCount > 0)
                    {
                        var unsavedRequirements = extractedRequirements
                            .Where(r => string.IsNullOrWhiteSpace(r.ApiId))
                            .ToList();

                        if (unsavedRequirements.Count > 0)
                        {
                            progressCallback?.Invoke($"🔁 Retrying Jama save for {unsavedRequirements.Count} unsaved requirements...");
                            var (retrySavedCount, retryFailedCount) = await _jamaConnectService.ImportRequirementsToJamaAsync(
                                projectId,
                                unsavedRequirements,
                                preferredParentContainerId,
                                cancellationToken,
                                (processed, total, failures, detail) =>
                                {
                                    var suffix = string.IsNullOrWhiteSpace(detail) ? string.Empty : $" | {detail}";
                                    progressCallback?.Invoke($"🔁 Retry save progress: {processed}/{total} processed, {failures} failed{suffix}");
                                });
                            savedCount += retrySavedCount;
                            failedCount = retryFailedCount;
                        }
                    }

                    _logger.LogInformation("[RequirementsMediator] Persisted {SavedCount}/{TotalCount} extracted requirements to Jama (failed: {FailedCount})",
                        savedCount, extractedRequirements.Count, failedCount);

                    _logger.LogInformation(
                        "[ATTACHMENT_TRACE] PersistResult AttachmentId={AttachmentId} SavedCount={SavedCount} FailedCount={FailedCount} PersistedSample={PersistedSample}",
                        attachment.Id,
                        savedCount,
                        failedCount,
                        BuildRequirementTraceSample(extractedRequirements.Where(r => !string.IsNullOrWhiteSpace(r.ApiId)).ToList()));

                    if (savedCount > 0 && failedCount == 0)
                    {
                        progressCallback?.Invoke($"✅ Saved {savedCount} extracted requirements to Jama");
                    }

                    // Capture in Jama is a primary requirement. Any remaining failures should halt the workflow.
                    if (failedCount > 0)
                    {
                        _logger.LogError("[ATTACHMENT_DIAG] Jama save incomplete after retry. Saved={SavedCount} Total={TotalCount} Failed={FailedCount} AttachmentId={AttachmentId} ProjectId={ProjectId}",
                            savedCount, extractedRequirements.Count, failedCount, attachment.Id, projectId);
                        progressCallback?.Invoke($"❌ Jama save incomplete: {savedCount}/{extractedRequirements.Count} saved. Import halted to avoid data loss.");
                        throw new InvalidOperationException($"Failed to persist all extracted requirements to Jama. Saved {savedCount}/{extractedRequirements.Count}.");
                    }
                }

                _logger.LogInformation("[RequirementsMediator] Parsed {Count} requirements from attachment {AttachmentId} ({FileName})", 
                    extractedRequirements.Count, attachment.Id, attachment.FileName);

                return extractedRequirements;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RequirementsMediator] Error parsing attachment {AttachmentId} ({FileName})", 
                    attachment.Id, attachment.FileName);
                _logger.LogError(ex, "[ATTACHMENT_DIAG] EXCEPTION ParseAttachmentRequirementsAsync AttachmentId={AttachmentId} FileName={FileName} ProjectId={ProjectId}",
                    attachment.Id, attachment.FileName, projectId);

                WriteAttachmentParseFailureSnapshot(attachment, projectId, ex);
                throw;
            }
        }

        private static string BuildRequirementTraceSample(IReadOnlyList<Requirement> requirements, int maxItems = 5)
        {
            if (requirements == null || requirements.Count == 0)
            {
                return "<none>";
            }

            var sample = requirements
                .Take(maxItems)
                .Select(r =>
                {
                    var id = !string.IsNullOrWhiteSpace(r.GlobalId)
                        ? r.GlobalId
                        : !string.IsNullOrWhiteSpace(r.Item)
                            ? r.Item
                            : "<no-id>";
                    var name = !string.IsNullOrWhiteSpace(r.Name) ? r.Name : "<no-name>";
                    var apiId = !string.IsNullOrWhiteSpace(r.ApiId) ? r.ApiId : "<no-api-id>";
                    return $"{id}::{name}::{apiId}";
                });

            return string.Join(" | ", sample);
        }

        public async Task<Dictionary<int, AttachmentIndexValidationResult>> GetAttachmentIndexValidationAsync(
            int projectId,
            IReadOnlyCollection<JamaAttachment> attachments,
            CancellationToken cancellationToken = default)
        {
            return await _jamaDocumentParserService.GetAttachmentIndexValidationAsync(projectId, attachments, cancellationToken);
        }

        public async Task<bool> ReindexAttachmentAsync(
            JamaAttachment attachment,
            int projectId,
            CancellationToken cancellationToken = default)
        {
            return await _jamaDocumentParserService.ReindexAttachmentAsync(attachment, projectId, cancellationToken);
        }

        private static void WriteAttachmentParseFailureSnapshot(JamaAttachment attachment, int projectId, Exception exception)
        {
            try
            {
                WriteAttachmentParseFailureFile(attachment, projectId, exception);

                var context = new TestCaseEditorApp.Services.Logging.AnalysisSnapshotContext
                {
                    MethodName = nameof(ParseAttachmentRequirementsAsync),
                    TriggeredBy = "AttachmentParsing",
                    RequirementId = attachment.Id.ToString(),
                    Comments = $"ProjectId={projectId}; FileName={attachment.FileName}; Error={exception.Message}",
                    CustomData = new Dictionary<string, object>
                    {
                        ["ProjectId"] = projectId,
                        ["AttachmentId"] = attachment.Id,
                        ["FileName"] = attachment.FileName ?? string.Empty,
                        ["ErrorType"] = exception.GetType().FullName ?? exception.GetType().Name,
                        ["StackTrace"] = exception.StackTrace ?? string.Empty
                    }
                };

                TestCaseEditorApp.Services.Logging.Log.WriteRequirementsAnalysisLogSnapshot(maxTraceWindows: 2, snapshotFileName: "attachment-parse-failure.txt", context: context);
            }
            catch (Exception snapshotEx)
            {
                TestCaseEditorApp.Services.Logging.Log.Warn($"[RequirementsMediator] Failed to write attachment parse failure snapshot: {snapshotEx.Message}");
            }
        }

        private static void WriteAttachmentParseFailureFile(JamaAttachment attachment, int projectId, Exception exception)
        {
            try
            {
                var logDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "TestCaseEditorApp", "logs");
                Directory.CreateDirectory(logDirectory);

                var failureFilePath = Path.Combine(logDirectory, "attachment-parse-failure.txt");
                var lines = new List<string>
                {
                    "============================================================",
                    $"Utc: {DateTime.UtcNow:O}",
                    $"ProjectId: {projectId}",
                    $"AttachmentId: {attachment.Id}",
                    $"FileName: {attachment.FileName}",
                    $"ErrorType: {exception.GetType().FullName}",
                    $"Message: {exception.Message}",
                    "StackTrace:",
                    exception.StackTrace ?? string.Empty,
                    string.Empty
                };

                File.AppendAllLines(failureFilePath, lines);
            }
            catch (Exception writeEx)
            {
                TestCaseEditorApp.Services.Logging.Log.Warn($"[RequirementsMediator] Failed to write direct attachment failure file: {writeEx.Message}");
            }
        }

        /// <summary>
        /// Import extracted requirements into the current project
        /// </summary>
        public Task ImportRequirementsAsync(List<Requirement> requirements)
        {
            try
            {
                _logger.LogInformation("[RequirementsMediator] Importing {Count} requirements", requirements.Count);

                foreach (var requirement in requirements)
                {
                    AddRequirement(requirement);
                }

                // Publish import event
                PublishEvent(new RequirementsEvents.RequirementsImported
                {
                    Requirements = requirements,
                    SourceFile = "Attachment Parsing",
                    ImportMethod = "JamaDocumentParser"
                });

                _logger.LogInformation("[RequirementsMediator] Successfully imported {Count} requirements", requirements.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RequirementsMediator] Error importing {Count} requirements", requirements.Count);
                throw;
            }

            return Task.CompletedTask;
        }

        public async Task<List<JamaProject>> GetProjectsAsync()
        {
            try
            {
                _logger.LogInformation("[RequirementsMediator] Loading available Jama projects through mediator (proper architectural pattern)");
                
                var projects = await _jamaConnectService.GetProjectsAsync();
                if (projects == null)
                {
                    _logger.LogWarning("[RequirementsMediator] GetProjectsAsync returned null from Jama service");
                    return new List<JamaProject>();
                }

                _logger.LogInformation("[RequirementsMediator] Successfully loaded {Count} Jama projects", projects.Count);
                return projects;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RequirementsMediator] Failed to load Jama projects");
                return new List<JamaProject>();
            }
        }

        // ===== MEDIATOR BASE FUNCTIONALITY =====

        public new void PublishEvent<T>(T eventData) where T : class
        {
            base.PublishEvent(eventData);
        }

        public new void MarkAsRegistered()
        {
            base.MarkAsRegistered();
        }

        // ===== REQUIRED ABSTRACT METHOD IMPLEMENTATIONS =====

        public override void NavigateToInitialStep()
        {
            _currentStep = "Import";
            _logger.LogDebug("Requirements domain: Navigated to initial step (Import)");
        }

        public override void NavigateToFinalStep()
        {
            _currentStep = "Export";
            _logger.LogDebug("Requirements domain: Navigated to final step (Export)");
        }

        public override bool CanNavigateBack()
        {
            return !string.IsNullOrEmpty(_currentStep) && _currentStep != "Import";
        }

        public override bool CanNavigateForward()
        {
            return !string.IsNullOrEmpty(_currentStep) && _currentStep != "Export";
        }

        // ===== CROSS-DOMAIN SYNCHRONIZATION =====

        /// <summary>
        /// Subscribe to mediator events that keep requirement state and persistence in sync.
        /// </summary>
        private void SubscribeToCrossDomainEvents()
        {
            try
            {
                // Subscribe to RequirementUpdated events (from analysis or other modifications)
                // This ensures the mediator marks the workspace as dirty when requirements are modified
                Subscribe<RequirementsEvents.RequirementUpdated>(OnRequirementUpdated);
                _logger.LogDebug("[RequirementsMediator] Subscribed to RequirementUpdated events");
                
                // NOTE: Cross-domain project creation events handled via HandleBroadcastNotification
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[RequirementsMediator] Failed to subscribe to cross-domain events");
            }
        }

        /// <summary>
        /// Handle RequirementUpdated event - marks workspace as dirty when requirements are modified
        /// This ensures analysis results and improved requirements get saved to the project
        /// </summary>
        private void OnRequirementUpdated(RequirementsEvents.RequirementUpdated eventData)
        {
            try
            {
                _logger.LogInformation("[RequirementsMediator] Requirement updated by {UpdatedBy}: {RequirementId}, Fields: {Fields}",
                    eventData.UpdatedBy, eventData.Requirement?.Item ?? "unknown", 
                    string.Join(", ", eventData.ModifiedFields));
                
                // Mark workspace as dirty so changes are persisted
                IsDirty = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RequirementsMediator] Error handling RequirementUpdated event");
            }
        }

        /// <summary>
        /// Handle cross-domain project creation notification - triggers attachment scanning for all project types
        /// This centralizes attachment scanning logic for both Jama imports and Word document imports
        /// </summary>
        private Task HandleProjectCreatedNotificationAsync(TestCaseEditorApp.MVVM.Events.CrossDomainMessages.ProjectCreatedNotification notification)
        {
            try
            {
                _logger.LogInformation("[RequirementsMediator] Project created: {WorkspaceName}, IsJamaImport: {IsJamaImport}, JamaProjectId: {JamaProjectId}",
                    notification.WorkspaceName, notification.IsJamaImport, notification.JamaProjectId);

                // Check for Jama project association for ALL project types
                var jamaProjectId = TryGetJamaProjectIdFromNotification(notification);
                if (jamaProjectId.HasValue)
                {
                    _logger.LogInformation("[RequirementsMediator] Project has Jama association: {JamaProjectId} (ImportType: {ImportType}) - attachment scanning available on user request", 
                        jamaProjectId.Value, notification.IsJamaImport ? "Jama" : "WordDocument");
                    
                    // NOTE: Attachment scanning will be triggered manually by user clicking scan button
                    // await TriggerBackgroundAttachmentScanAsync(jamaProjectId.Value);
                }
                else
                {
                    if (notification.IsJamaImport)
                    {
                        _logger.LogDebug("[RequirementsMediator] Jama import but no project ID found - attachment search not available");
                    }
                    else
                    {
                        _logger.LogDebug("[RequirementsMediator] Word document import with no Jama association - attachment search requires manual project configuration");
                    }
                }

                // Always publish availability event for Word document imports (even without Jama association)
                if (!notification.IsJamaImport)
                {
                    PublishEvent(new RequirementsEvents.DocumentScraperAvailable
                    {
                        WorkspaceName = notification.WorkspaceName,
                        ProjectPath = notification.ProjectPath,
                        ImportSource = "WordDocument",
                        HasJamaAssociation = jamaProjectId.HasValue
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RequirementsMediator] Error handling cross-domain ProjectCreatedNotification");
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Extract Jama project ID from cross-domain notification for attachment scanning
        /// Handles both direct Jama imports and Word document imports with Jama workspace associations
        /// </summary>
        private int? TryGetJamaProjectIdFromNotification(TestCaseEditorApp.MVVM.Events.CrossDomainMessages.ProjectCreatedNotification notification)
        {
            try
            {
                // Direct Jama project from import
                if (notification.JamaProjectId.HasValue)
                {
                    _logger.LogDebug("[RequirementsMediator] Found direct Jama project ID: {JamaProjectId}", notification.JamaProjectId.Value);
                    return notification.JamaProjectId.Value;
                }

                // Check current workspace context for Jama project association
                var currentWorkspace = _workspaceContext.CurrentWorkspace;
                if (currentWorkspace?.JamaProject != null && int.TryParse(currentWorkspace.JamaProject, out var workspaceProjectId))
                {
                    _logger.LogDebug("[RequirementsMediator] Found workspace Jama project ID: {JamaProjectId}", workspaceProjectId);
                    return workspaceProjectId;
                }

                _logger.LogDebug("[RequirementsMediator] No Jama project ID found in notification or workspace data");
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[RequirementsMediator] Error extracting Jama project ID from notification");
                return null;
            }
        }
        
    }

    /// <summary>
    /// Custom comparer for natural numeric sorting of requirements
    /// Ensures DECAGON-REQ_RC-5 comes before DECAGON-REQ_RC-12, etc.
    /// (Copied from TestCaseGeneration domain for consistency)
    /// </summary>
    internal class RequirementNaturalComparer : IComparer<Requirement>
    {
        private static readonly System.Text.RegularExpressions.Regex _trailingNumberRegex = 
            new System.Text.RegularExpressions.Regex(@"^(.*?)(\d+)$", System.Text.RegularExpressions.RegexOptions.Compiled);

        public int Compare(Requirement? x, Requirement? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x == null) return -1;
            if (y == null) return 1;

            // Prefer 'Item' then 'Name' as the canonical id string
            var sa = (x.Item ?? x.Name ?? string.Empty).Trim();
            var sb = (y.Item ?? y.Name ?? string.Empty).Trim();

            // If identical strings, consider them equal
            if (string.Equals(sa, sb, StringComparison.OrdinalIgnoreCase)) return 0;

            var ma = _trailingNumberRegex.Match(sa);
            var mb = _trailingNumberRegex.Match(sb);

            if (ma.Success && mb.Success)
            {
                var prefixA = ma.Groups[1].Value;
                var prefixB = mb.Groups[1].Value;
                if (!string.Equals(prefixA, prefixB, StringComparison.OrdinalIgnoreCase))
                {
                    // Compare prefixes alphabetically
                    return StringComparer.OrdinalIgnoreCase.Compare(prefixA, prefixB);
                }

                // Both prefixes equal – compare numeric suffix ascending so 5 comes before 12
                if (long.TryParse(ma.Groups[2].Value, out var na) && long.TryParse(mb.Groups[2].Value, out var nb))
                {
                    // Ascending numeric order
                    var numCompare = na.CompareTo(nb);
                    if (numCompare != 0) return numCompare;
                }

                // Fallback to full-string compare if numeric equal
                return StringComparer.OrdinalIgnoreCase.Compare(sa, sb);
            }

            // If one has numeric suffix and other not, place numeric-suffixed after/before depending on prefix
            if (ma.Success && !mb.Success)
            {
                var prefixA = ma.Groups[1].Value;
                var prefixB = sb;
                var cmp = StringComparer.OrdinalIgnoreCase.Compare(prefixA, prefixB);
                if (cmp != 0) return cmp;
                // If prefixes same, treat the numeric-suffixed as less (so similar entries cluster)
                return -1;
            }
            if (!ma.Success && mb.Success)
            {
                var prefixA = sa;
                var prefixB = mb.Groups[1].Value;
                var cmp = StringComparer.OrdinalIgnoreCase.Compare(prefixA, prefixB);
                if (cmp != 0) return cmp;
                return 1;
            }

            // No numeric suffixes – plain string compare
            return StringComparer.OrdinalIgnoreCase.Compare(sa, sb);
        }
    }
    
    /// <summary>
    /// Equality comparer for requirements to check if collections are equivalent
    /// </summary>
    internal class RequirementEqualityComparer : IEqualityComparer<Requirement>
    {
        public bool Equals(Requirement? x, Requirement? y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (x == null || y == null) return false;
            
            return x.Item == y.Item && x.Name == y.Name && x.Description == y.Description;
        }
        
        public int GetHashCode(Requirement obj)
        {
            return HashCode.Combine(obj.Item, obj.Name, obj.Description);
        }
    }
}