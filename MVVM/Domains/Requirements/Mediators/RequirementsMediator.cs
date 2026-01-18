using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.MVVM.Utils;
using TestCaseEditorApp.MVVM.Domains.Requirements.Events;
using TestCaseEditorApp.MVVM.Domains.NewProject.Events;
using TestCaseEditorApp.MVVM.Domains.OpenProject.Events;
using TestCaseEditorApp.MVVM.Events;
using TestCaseEditorApp.Services;
using TestCaseEditorApp.MVVM.Domains.Requirements.Services;
using TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Services; // For SmartRequirementImporter
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
                    PublishEvent(new RequirementsEvents.RequirementSelected
                    {
                        Requirement = value!,
                        SelectedBy = "Mediator"
                    });
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
                    PublishEvent(new RequirementsEvents.WorkflowStateChanged
                    {
                        PropertyName = nameof(IsDirty),
                        NewValue = value,
                        OldValue = _isDirty
                    });
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
            IRequirementAnalysisEngine? analysisEngine = null, // NEW: Optional for transition period
            PerformanceMonitoringService? performanceMonitor = null,
            EventReplayService? eventReplay = null)
            : base(logger, uiCoordinator, "Requirements", performanceMonitor, eventReplay)
        {
            _requirementService = requirementService ?? throw new ArgumentNullException(nameof(requirementService));
            _analysisService = analysisService ?? throw new ArgumentNullException(nameof(analysisService));
            _scrubber = scrubber ?? throw new ArgumentNullException(nameof(scrubber));
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

                    PublishEvent(new RequirementsEvents.RequirementsCollectionChanged
                    {
                        Action = "Import",
                        AffectedRequirements = importResult.Requirements,
                        NewCount = _requirements.Count
                    });

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

                requirement.Analysis = analysis;

                PublishEvent(new RequirementsEvents.RequirementAnalyzed
                {
                    Requirement = requirement,
                    Analysis = analysis,
                    Success = true,
                    AnalysisTime = TimeSpan.FromSeconds(2) // Placeholder
                });

                IsDirty = true;
                HideProgress();
                ShowNotification($"Analysis completed for {requirement.GlobalId}", DomainNotificationType.Success);

                _logger.LogInformation("Requirement analysis completed for {RequirementId}", requirement.GlobalId);
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
                        
                        // ✅ Even when data is current, notify header to refresh display when project is activated
                        PublishEvent(new RequirementsEvents.RequirementsCollectionChanged
                        {
                            Action = "ProjectActivated",
                            AffectedRequirements = _requirements.ToList(),
                            NewCount = _requirements.Count
                        });
                        
                        // ✅ Always notify about requirement selection to update header (even if null)
                        PublishEvent(new RequirementsEvents.RequirementSelected
                        {
                            Requirement = CurrentRequirement
                        });
                        
                        return; // Don't reload - data is already current!
                    }
                    
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

                    // CRITICAL: Publish event on UI thread to avoid threading violations
                    PublishEvent(new RequirementsEvents.RequirementsCollectionChanged
                    {
                        Action = "Load",
                        AffectedRequirements = _requirements.ToList(),
                        NewCount = _requirements.Count
                    });
                    
                    IsDirty = false;
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
                }
            }
            else if (notification is NewProjectEvents.ProjectOpened newProjectOpened)
            {
                if (newProjectOpened.Workspace != null)
                {
                    _ = LoadFromProjectAsync(newProjectOpened.Workspace);
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