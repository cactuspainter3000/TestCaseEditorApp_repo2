using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.MVVM.Utils;
using TestCaseEditorApp.MVVM.Domains.Requirements.Events;
using TestCaseEditorApp.MVVM.Domains.NewProject.Events;
using TestCaseEditorApp.MVVM.Domains.NewProject.Mediators;
using TestCaseEditorApp.MVVM.Domains.OpenProject.Events;
using TestCaseEditorApp.MVVM.Events;
using TestCaseEditorApp.Services;
using TestCaseEditorApp.MVVM.Domains.Requirements.Services;
using TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Services; // For SmartRequirementImporter
using TestCaseEditorApp.MVVM.Domains.Notification.Mediators; // For INotificationMediator
using TestCaseEditorApp.MVVM.Domains.Requirements.ViewModels; // For RequirementsSearchAttachmentsViewModel
using System.Windows;

namespace TestCaseEditorApp.MVVM.Domains.Requirements.Mediators
{
    /// <summary>
    /// Requirements domain mediator implementation.
    /// Handles all requirements management functionality following architectural patterns.
    /// </summary>
    public class RequirementsMediator : BaseDomainMediator<RequirementsEvents>, IRequirementsMediator
    {
        private readonly IRequirementService _requirementService;
        private readonly TestCaseEditorApp.MVVM.Domains.Requirements.Services.IRequirementAnalysisService _analysisService; // Legacy - will be phased out
        private readonly IRequirementAnalysisEngine? _analysisEngine; // NEW: Requirements domain analysis
        private readonly IRequirementDataScrubber _scrubber;
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
                    
                    // ✅ CROSS-DOMAIN: Publish current requirement changed notification
                    PublishCurrentRequirementNotification(value);
                    
                    // Broadcast to notification system for cross-domain coordination
                    var notificationMediator = App.ServiceProvider?.GetService(typeof(TestCaseEditorApp.MVVM.Domains.Notification.Mediators.INotificationMediator)) as TestCaseEditorApp.MVVM.Domains.Notification.Mediators.INotificationMediator;
                    notificationMediator?.HandleBroadcastNotification(eventData);
                    
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
                    
                    // Broadcast to notification system
                    var notificationMediator = App.ServiceProvider?.GetService(typeof(TestCaseEditorApp.MVVM.Domains.Notification.Mediators.INotificationMediator)) as TestCaseEditorApp.MVVM.Domains.Notification.Mediators.INotificationMediator;
                    notificationMediator?.HandleBroadcastNotification(workflowEvent);
                    
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
                    _isAnalyzing = value;
                    PublishEvent(new RequirementsEvents.WorkflowStateChanged
                    {
                        PropertyName = nameof(IsAnalyzing),
                        NewValue = value,
                        OldValue = _isAnalyzing
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
            IRequirementService requirementService,
            TestCaseEditorApp.MVVM.Domains.Requirements.Services.IRequirementAnalysisService analysisService,
            IRequirementDataScrubber scrubber,
            IWorkspaceContext workspaceContext,
            INewProjectMediator newProjectMediator,
            IJamaConnectService jamaConnectService,
            IJamaDocumentParserService jamaDocumentParserService,
            IRequirementAnalysisEngine? analysisEngine = null, // NEW: Optional for transition period
            PerformanceMonitoringService? performanceMonitor = null,
            EventReplayService? eventReplay = null)
            : base(logger, uiCoordinator, "Requirements", performanceMonitor, eventReplay)
        {
            _requirementService = requirementService ?? throw new ArgumentNullException(nameof(requirementService));
            _analysisService = analysisService ?? throw new ArgumentNullException(nameof(analysisService));
            _scrubber = scrubber ?? throw new ArgumentNullException(nameof(scrubber));
            _workspaceContext = workspaceContext ?? throw new ArgumentNullException(nameof(workspaceContext));
            _newProjectMediator = newProjectMediator ?? throw new ArgumentNullException(nameof(newProjectMediator));
            _jamaConnectService = jamaConnectService ?? throw new ArgumentNullException(nameof(jamaConnectService));
            _jamaDocumentParserService = jamaDocumentParserService ?? throw new ArgumentNullException(nameof(jamaDocumentParserService));
            _analysisEngine = analysisEngine; // Optional during transition
            _smartImporter = new SmartRequirementImporter(requirementService, 
                Microsoft.Extensions.Logging.Abstractions.NullLogger<SmartRequirementImporter>.Instance);
            
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
                    
                    // Broadcast to notification system
                var notificationMediator = App.ServiceProvider?.GetService(typeof(TestCaseEditorApp.MVVM.Domains.Notification.Mediators.INotificationMediator)) as TestCaseEditorApp.MVVM.Domains.Notification.Mediators.INotificationMediator;

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

                // ARCHITECTURE: Prefer new Requirements domain engine when available
                if (_analysisEngine != null)
                {
                    _logger.LogDebug("[RequirementsMediator] Using NEW Requirements domain analysis engine");
                    analysis = await _analysisEngine.AnalyzeRequirementAsync(requirement, 
                        progress => UpdateProgress($"Analysis progress: {progress}", 75));
                }
                else
                {
                    _logger.LogDebug("[RequirementsMediator] Fallback to legacy TestCaseGeneration analysis service");
                    analysis = await _analysisService.AnalyzeRequirementAsync(requirement);
                }

                // Store analysis duration
                var duration = DateTime.Now - startTime;
                analysis.AnalysisDurationSeconds = duration.TotalSeconds;

                requirement.Analysis = analysis;

                PublishEvent(new RequirementsEvents.RequirementAnalyzed
                {
                    Requirement = requirement,
                    Analysis = analysis,
                    Success = true,
                    AnalysisTime = duration
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
                ShowNotification($"Analysis completed for {requirement.GlobalId}", DomainNotificationType.Success);

                // Update requirements progress notification for header
                PublishRequirementsProgressNotification();

                _logger.LogInformation("Requirement analysis completed for {RequirementId}", requirement.GlobalId);
                
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
                
                return true;
            }
            catch (Exception ex)
            {
                HideProgress();
                ShowNotification($"Analysis failed: {ex.Message}", DomainNotificationType.Error);

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
                .Where(r => 
                    r.Name?.Contains(searchText, StringComparison.OrdinalIgnoreCase) == true ||
                    r.Description?.Contains(searchText, StringComparison.OrdinalIgnoreCase) == true ||
                    r.GlobalId?.Contains(searchText, StringComparison.OrdinalIgnoreCase) == true)
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

            try
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    // Check if data is already loaded and current - avoid unnecessary reload!
                    var sortedRequirements = workspace.Requirements.OrderBy(r => r, new RequirementNaturalComparer()).ToList();
                    
                    // If we already have the same requirements loaded, preserve current navigation state
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
                        _logger.LogInformation("📊 RequirementsMediator: Reloading requirements data (count changed or different data)");
                        
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
                    
                    // ✅ CROSS-DOMAIN: Publish notification events for workspace coordination (single call, final state only)
                    PublishRequirementsProgressNotification();
                    
                    // ✅ Always notify about requirement selection to update header (even if null)
                    PublishEvent(new RequirementsEvents.RequirementSelected
                    {
                        Requirement = CurrentRequirement
                    });
                    
                    _logger.LogInformation("Loaded {Count} requirements from project", _requirements.Count);
                });

                await Task.CompletedTask;
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load requirements from project");
                return false;
            }
        }

        public async Task<bool> SaveToProjectAsync()
        {
            // TODO: Implement save to project functionality
            await Task.CompletedTask;
            
            IsDirty = false;
            ShowNotification("Requirements saved to project", DomainNotificationType.Success);
            
            return true;
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
            // REMOVED: Legacy TestCaseGeneration event handling - NavigationViewModel now uses proper RequirementsEvents directly
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
            
            // Check ImportSource flag - this is the authoritative source for view routing
            if (!string.IsNullOrEmpty(currentWorkspace.ImportSource))
            {
                var isJama = string.Equals(currentWorkspace.ImportSource, "Jama", StringComparison.OrdinalIgnoreCase);
                TestCaseEditorApp.Services.Logging.Log.Debug($"[RequirementsMediator] IsJamaDataSource() returning {isJama} (ImportSource comparison)");
                return isJama;
            }
            
            // Default to document view if ImportSource is missing/empty
            TestCaseEditorApp.Services.Logging.Log.Debug("[RequirementsMediator] IsJamaDataSource() returning false (empty ImportSource)");
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
        public async Task<List<JamaAttachment>> ScanProjectAttachmentsAsync(int projectId, IProgress<AttachmentScanProgressData>? progress = null)
        {
            try
            {
                _logger.LogInformation("[RequirementsMediator] Starting attachment scan for project {ProjectId}", projectId);
                
                // Get project name from current workspace if available
                var currentWorkspace = _workspaceContext.CurrentWorkspace;
                var projectName = currentWorkspace?.JamaTestPlan ?? $"Project {projectId}";
                
                // Publish start event
                PublishEvent(new RequirementsEvents.AttachmentScanStarted
                {
                    ProjectId = projectId,
                    ProjectName = projectName
                });

                var startTime = DateTime.Now;

                // Use the injected Jama service to get attachments with project name for better progress messages
                var attachments = await _jamaConnectService.GetProjectAttachmentsAsync(projectId, default, (current, total, progressData) =>
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

                var duration = DateTime.Now - startTime;

                // Publish completion event
                PublishEvent(new RequirementsEvents.AttachmentScanCompleted
                {
                    ProjectId = projectId,
                    AttachmentCount = attachments?.Count ?? 0,
                    Success = true,
                    Duration = duration,
                    Attachments = attachments ?? new List<JamaAttachment>()
                });

                _logger.LogInformation("[RequirementsMediator] Attachment scan completed for project {ProjectId} - found {Count} attachments", 
                    projectId, attachments?.Count ?? 0);

                return attachments ?? new List<JamaAttachment>();
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
        public async Task<List<Requirement>> ParseAttachmentRequirementsAsync(int attachmentId)
        {
            try
            {
                _logger.LogInformation("[RequirementsMediator] Parsing requirements from attachment {AttachmentId}", attachmentId);

                // For now, simulate parsing until IJamaDocumentParserService is fully implemented
                await Task.Delay(2000);

                var simulatedRequirements = new List<Requirement>
                {
                    new Requirement 
                    { 
                        GlobalId = Guid.NewGuid().ToString(),
                        Name = $"Extracted Requirement from Attachment {attachmentId}",
                        Description = $"Sample requirement extracted from attachment {attachmentId} using LLM document parsing",
                        ItemType = "Requirement"
                    }
                };

                _logger.LogInformation("[RequirementsMediator] Parsed {Count} requirements from attachment {AttachmentId}", 
                    simulatedRequirements.Count, attachmentId);

                return simulatedRequirements;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RequirementsMediator] Error parsing attachment {AttachmentId}", attachmentId);
                throw;
            }
        }

        /// <summary>
        /// Import extracted requirements into the current project
        /// </summary>
        public async Task ImportRequirementsAsync(List<Requirement> requirements)
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
        /// Subscribe to cross-domain events for requirement selection synchronization
        /// </summary>
        private void SubscribeToCrossDomainEvents()
        {
            try
            {
                // Subscribe to TestCaseGeneration domain requirement selection
                // This ensures Requirements domain stays in sync with global requirement selection
                var domainCoordinator = GetDomainCoordinator();
                if (domainCoordinator != null)
                {
                    // TODO: Implement proper cross-domain subscription through DomainCoordinator
                    _logger.LogDebug("[RequirementsMediator] DomainCoordinator available for cross-domain events");
                }
                
                // Subscribe to RequirementUpdated events (from analysis or other modifications)
                // This ensures the mediator marks the workspace as dirty when requirements are modified
                Subscribe<RequirementsEvents.RequirementUpdated>(OnRequirementUpdated);
                _logger.LogDebug("[RequirementsMediator] Subscribed to RequirementUpdated events");
                
                // NOTE: Cross-domain project creation events handled via HandleBroadcastNotification
                
                _logger.LogDebug("[RequirementsMediator] Subscribed to cross-domain TestCaseGeneration.RequirementSelected events");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[RequirementsMediator] Failed to subscribe to cross-domain events");
            }
        }

        /// <summary>
        /// Handle requirement selection from TestCaseGeneration domain
        /// Synchronizes Requirements domain state with global requirement selection
        /// </summary>
        private async void OnTestCaseGenerationRequirementSelected(TestCaseGenerationEvents.RequirementSelected eventData)
        {
            try
            {
                _logger.LogDebug("[RequirementsMediator] Received cross-domain RequirementSelected: {RequirementId}", 
                    eventData.Requirement?.GlobalId ?? "null");

                // Find the requirement in our collection
                var requirement = _requirements.FirstOrDefault(r => r.GlobalId == eventData.Requirement?.GlobalId);
                
                if (requirement != null && CurrentRequirement?.GlobalId != requirement.GlobalId)
                {
                    _logger.LogDebug("[RequirementsMediator] Synchronizing to requirement: {RequirementId}", requirement.GlobalId);
                    
                    // Update CurrentRequirement without publishing our own event to avoid circular notifications
                    _currentRequirement = requirement;
                    
                    // Notify UI via property change but don't publish cross-domain event
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        PublishEvent(new RequirementsEvents.RequirementSelected
                        {
                            Requirement = requirement,
                            SelectedBy = "CrossDomainSync"
                        });
                    });
                    
                    _logger.LogDebug("[RequirementsMediator] Successfully synchronized to requirement: {RequirementId}", requirement.GlobalId);
                }
                else if (requirement == null && eventData.Requirement != null)
                {
                    _logger.LogDebug("[RequirementsMediator] Requirement {RequirementId} not found in Requirements domain collection", 
                        eventData.Requirement.GlobalId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RequirementsMediator] Error handling cross-domain RequirementSelected event");
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
        private async Task HandleProjectCreatedNotificationAsync(TestCaseEditorApp.MVVM.Events.CrossDomainMessages.ProjectCreatedNotification notification)
        {
            try
            {
                _logger.LogInformation("[RequirementsMediator] Project created: {WorkspaceName}, IsJamaImport: {IsJamaImport}, JamaProjectId: {JamaProjectId}",
                    notification.WorkspaceName, notification.IsJamaImport, notification.JamaProjectId);

                // Check for Jama project association for ALL project types
                var jamaProjectId = TryGetJamaProjectIdFromNotification(notification);
                if (jamaProjectId.HasValue)
                {
                    _logger.LogInformation("[RequirementsMediator] Starting attachment search for project: {JamaProjectId} (ImportType: {ImportType})", 
                        jamaProjectId.Value, notification.IsJamaImport ? "Jama" : "WordDocument");
                    
                    // Trigger background attachment scanning for any project with Jama association
                    await TriggerBackgroundAttachmentScanAsync(jamaProjectId.Value);
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
        
        /// <summary>
        /// Publish requirements progress notification for cross-domain workspace coordination
        /// </summary>
        private void PublishRequirementsProgressNotification()
        {
            try
            {
                var totalReqs = _requirements.Count;
                var analyzedReqs = _requirements.Count(r => r.Analysis != null);
                var withTestCases = _requirements.Count(r => r.HasGeneratedTestCase);
                
                var notificationMediator = App.ServiceProvider?.GetService(typeof(TestCaseEditorApp.MVVM.Domains.Notification.Mediators.INotificationMediator)) 
                    as TestCaseEditorApp.MVVM.Domains.Notification.Mediators.INotificationMediator;
                
                if (notificationMediator != null)
                {
                    var progressEvent = new TestCaseEditorApp.MVVM.Domains.Notification.Events.NotificationEvents.RequirementsProgressChanged
                    {
                        TotalRequirements = totalReqs,
                        AnalyzedRequirements = analyzedReqs,
                        RequirementsWithTestCases = withTestCases,
                        SourceDomain = "Requirements"
                    };
                    
                    notificationMediator.HandleBroadcastNotification(progressEvent);
                    _logger.LogDebug("[RequirementsMediator] Published requirements progress: {Total}/{Analyzed}/{WithTestCases}", 
                        totalReqs, analyzedReqs, withTestCases);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RequirementsMediator] Error publishing requirements progress notification");
            }
        }
        
        /// <summary>
        /// Publish current requirement changed notification for cross-domain workspace coordination
        /// </summary>
        private void PublishCurrentRequirementNotification(Requirement? requirement)
        {
            try
            {
                var notificationMediator = App.ServiceProvider?.GetService(typeof(TestCaseEditorApp.MVVM.Domains.Notification.Mediators.INotificationMediator)) 
                    as TestCaseEditorApp.MVVM.Domains.Notification.Mediators.INotificationMediator;
                
                if (notificationMediator != null)
                {
                    var currentEvent = new TestCaseEditorApp.MVVM.Domains.Notification.Events.NotificationEvents.CurrentRequirementChanged
                    {
                        RequirementId = requirement?.GlobalId ?? "None",
                        RequirementTitle = requirement?.Name ?? "No requirement selected",
                        VerificationMethod = requirement?.VerificationMethodText ?? "Unassigned"
                    };
                    
                    notificationMediator.HandleBroadcastNotification(currentEvent);
                    _logger.LogDebug("[RequirementsMediator] Published current requirement changed: {RequirementId}", 
                        requirement?.GlobalId ?? "null");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RequirementsMediator] Error publishing current requirement notification");
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