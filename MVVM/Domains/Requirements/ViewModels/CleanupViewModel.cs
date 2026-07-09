using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using TestCaseEditorApp.MVVM.Domains.Requirements.Models;
using TestCaseEditorApp.MVVM.Domains.Requirements.Services;
using TestCaseEditorApp.MVVM.Domains.Requirements.Mediators;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.Services;

namespace TestCaseEditorApp.MVVM.Domains.Requirements.ViewModels
{
    /// <summary>
    /// ViewModel for the Cleanup Tab - manages requirement editing workflow.
    /// Orchestrates RequirementEditSessionService, handles UI state, and coordinates staging/commit operations.
    /// </summary>
    public partial class CleanupViewModel : ObservableObject
    {
        private readonly RequirementEditSessionService _editSessionService;
        private readonly JamaConnectService _jamaService;
        private readonly IRequirementsMediator _mediator;
        private readonly ILogger<CleanupViewModel> _logger;

        // ===== Properties =====
        [ObservableProperty]
        private ObservableCollection<RequirementEditSession> requirements = new();

        [ObservableProperty]
        private RequirementEditSession? currentRequirement;

        [ObservableProperty]
        private bool isStagedFilterActive;

        [ObservableProperty]
        private int stagedCount;

        [ObservableProperty]
        private int unsavedCount;

        [ObservableProperty]
        private bool isLoading;

        [ObservableProperty]
        private string statusMessage = "Ready";

        public CleanupViewModel(
            RequirementEditSessionService editSessionService,
            JamaConnectService jamaService,
            IRequirementsMediator mediator,
            ILogger<CleanupViewModel> logger)
        {
            _editSessionService = editSessionService;
            _jamaService = jamaService;
            _mediator = mediator;
            _logger = logger;

            // Notification when selected requirement changes
            PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(CurrentRequirement))
                {
                    _logger.LogInformation("[CleanupViewModel] Selected requirement: {Name}", 
                        CurrentRequirement?.DisplayName ?? "(none)");
                }
            };
        }

        /// <summary>
        /// Load requirements from Jama and sync with local workspace
        /// </summary>
        [RelayCommand]
        public async Task LoadRequirementsAsync()
        {
            try
            {
                IsLoading = true;
                StatusMessage = "Loading requirements...";

                var workspace = await _editSessionService.LoadOrCreateWorkspaceAsync(
                    projectId: 686, // TODO: Make configurable
                    targetContainerId: null);

                // Fetch current requirements from Jama (returns JamaItem list)
                var jamaItems = await _jamaService.GetRequirementsAsync(projectId: 686);
                _logger.LogInformation("[CleanupViewModel] Fetched {Count} requirements from Jama", jamaItems.Count);

                // Convert JamaItems to Requirements
                var requirements = jamaItems.Select(item => new Requirement
                {
                    ApiId = item.Id.ToString(),
                    GlobalId = item.GlobalId ?? "",
                    Item = item.Item ?? $"ITEM-{item.Id}",
                    Name = item.Name ?? "",
                    Description = item.Description ?? "",
                    Status = item.Status ?? ""
                }).ToList();

                // Merge with workspace
                var merged = _editSessionService.MergeJamaRequirements(requirements, 686, null);

                // Update UI collections
                Requirements.Clear();
                foreach (var req in merged.Requirements.OrderBy(r => r.DisplayName))
                {
                    Requirements.Add(req);
                }

                CurrentRequirement = Requirements.FirstOrDefault();
                UpdateCounters();

                StatusMessage = $"Loaded {Requirements.Count} requirements";
                _logger.LogInformation("[CleanupViewModel] LoadRequirementsAsync completed: {Count} total, {Unsaved} unsaved, {Staged} staged",
                    Requirements.Count, UnsavedCount, StagedCount);
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error loading requirements: {ex.Message}";
                _logger.LogError(ex, "[CleanupViewModel] Failed to load requirements");
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// Handle when a requirement is edited (name/description changed)
        /// </summary>
        [RelayCommand]
        public async Task OnEditSessionChangedAsync(RequirementEditSession session)
        {
            try
            {
                var editedBy = System.Environment.UserName;
                await _editSessionService.AutoSaveAsync(session, editedBy);
                UpdateCounters();
                StatusMessage = $"Auto-saved changes to {session.DisplayName}";
                _logger.LogInformation("[CleanupViewModel] Auto-saved requirement: {Name}", session.DisplayName);
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error saving changes: {ex.Message}";
                _logger.LogError(ex, "[CleanupViewModel] Failed to auto-save requirement");
            }
        }

        /// <summary>
        /// Stage a requirement for commit
        /// </summary>
        [RelayCommand]
        public async Task StageRequirementAsync(RequirementEditSession session)
        {
            try
            {
                await _editSessionService.StageForCommitAsync(session.JamaId);
                session.StagedForCommit = true;
                UpdateCounters();
                StatusMessage = $"Staged {session.DisplayName} for commit";
                _logger.LogInformation("[CleanupViewModel] Staged requirement: {Name}", session.DisplayName);
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error staging requirement: {ex.Message}";
                _logger.LogError(ex, "[CleanupViewModel] Failed to stage requirement");
            }
        }

        /// <summary>
        /// Unstage a requirement
        /// </summary>
        [RelayCommand]
        public async Task UnstageRequirementAsync(RequirementEditSession session)
        {
            try
            {
                await _editSessionService.UnstageAsync(session.JamaId);
                session.StagedForCommit = false;
                UpdateCounters();
                StatusMessage = $"Unstaged {session.DisplayName}";
                _logger.LogInformation("[CleanupViewModel] Unstaged requirement: {Name}", session.DisplayName);
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error unstaging requirement: {ex.Message}";
                _logger.LogError(ex, "[CleanupViewModel] Failed to unstage requirement");
            }
        }

        /// <summary>
        /// Reset a requirement to original (discard edits)
        /// </summary>
        [RelayCommand]
        public async Task ResetRequirementAsync(RequirementEditSession session)
        {
            try
            {
                session.ResetToOriginal();
                await _editSessionService.SaveWorkspaceAsync();
                UpdateCounters();
                StatusMessage = $"Reset {session.DisplayName} to original";
                _logger.LogInformation("[CleanupViewModel] Reset requirement: {Name}", session.DisplayName);
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error resetting requirement: {ex.Message}";
                _logger.LogError(ex, "[CleanupViewModel] Failed to reset requirement");
            }
        }

        /// <summary>
        /// Commit staged requirements back to Jama
        /// </summary>
        [RelayCommand]
        public async Task CommitStagedRequirementsAsync()
        {
            try
            {
                var staged = _editSessionService.GetStagedRequirements();
                if (staged.Count == 0)
                {
                    StatusMessage = "No requirements staged for commit";
                    return;
                }

                // Check for unsaved changes
                var unsaved = staged.Where(r => r.HasChanges && !r.LastSaved.HasValue).ToList();
                if (unsaved.Any())
                {
                    StatusMessage = $"⚠️  {unsaved.Count} staged requirement(s) have unsaved changes. Save first?";
                    _logger.LogWarning("[CleanupViewModel] Attempted commit with unsaved changes: {Count}", unsaved.Count);
                    return;
                }

                IsLoading = true;
                StatusMessage = $"Committing {staged.Count} requirement(s) to Jama...";

                // Convert to Requirement models
                var reqs = staged.Select(s => _editSessionService.ToRequirement(s)).ToList();

                // Import to Jama - returns (CreatedCount, FailedCount) tuple
                var (createdCount, failedCount) = await _jamaService.ImportRequirementsToJamaAsync(
                    projectId: 686,
                    requirements: reqs,
                    preferredParentContainerId: null);

                if (failedCount == 0)
                {
                    var committedBy = System.Environment.UserName;
                    var jamaIds = staged.Select(r => r.JamaId).ToList();
                    await _editSessionService.MarkAsCommittedAsync(jamaIds, committedBy);

                    // Update UI state
                    foreach (var session in staged)
                    {
                        session.CommittedToJama = true;
                        session.CommittedAt = DateTime.UtcNow;
                    }

                    UpdateCounters();
                    StatusMessage = $"✅ Successfully committed {createdCount} requirement(s)";
                    _logger.LogInformation("[CleanupViewModel] CommitStagedRequirementsAsync succeeded: {Count} committed", createdCount);
                }
                else
                {
                    StatusMessage = $"❌ Commit partially failed: {createdCount} created, {failedCount} failed";
                    _logger.LogError("[CleanupViewModel] Commit failed: Created={Created}, Failed={Failed}", createdCount, failedCount);
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"❌ Error committing requirements: {ex.Message}";
                _logger.LogError(ex, "[CleanupViewModel] Exception during commit");
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// Toggle the staged filter view
        /// </summary>
        [RelayCommand]
        public void ToggleStagedFilter()
        {
            IsStagedFilterActive = !IsStagedFilterActive;
            StatusMessage = IsStagedFilterActive ? $"Showing {StagedCount} staged requirement(s)" : "Showing all requirements";
            _logger.LogInformation("[CleanupViewModel] Staged filter toggled: {Active}", IsStagedFilterActive);
        }

        /// <summary>
        /// Update counters for UI display
        /// </summary>
        private void UpdateCounters()
        {
            StagedCount = Requirements.Count(r => r.StagedForCommit && !r.CommittedToJama);
            UnsavedCount = Requirements.Count(r => r.HasChanges && !r.LastSaved.HasValue);
        }
    }
}
