using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.MVVM.Domains.Requirements.Models;
using TestCaseEditorApp.Services;

namespace TestCaseEditorApp.MVVM.Domains.Requirements.Services
{
    /// <summary>
    /// Service for managing requirement edit sessions.
    /// Handles loading/saving workspace.json, merging Jama + local edits, auto-save.
    /// </summary>
    public class RequirementEditSessionService
    {
        private readonly ILogger<RequirementEditSessionService> _logger;
        private readonly string _workspacePath;
        private RequirementEditWorkspace? _currentWorkspace;

        // JSON options for serialization
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter() }
        };

        public RequirementEditSessionService(ILogger<RequirementEditSessionService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            var appFolder = AppStoragePaths.WorkspaceDataDirectory;
            if (!Directory.Exists(appFolder))
            {
                Directory.CreateDirectory(appFolder);
            }
            _workspacePath = Path.Combine(appFolder, "requirement-workspace.json");
        }

        /// <summary>
        /// Load or create workspace for a project
        /// </summary>
        public async Task<RequirementEditWorkspace> LoadOrCreateWorkspaceAsync(int projectId, int? targetContainerId = null)
        {
            try
            {
                if (File.Exists(_workspacePath))
                {
                    var json = await File.ReadAllTextAsync(_workspacePath);
                    _currentWorkspace = JsonSerializer.Deserialize<RequirementEditWorkspace>(json, JsonOptions);
                    _logger.LogInformation("[RequirementEditSessionService] Loaded workspace with {Count} requirements", 
                        _currentWorkspace?.Requirements.Count ?? 0);
                }
                else
                {
                    _currentWorkspace = new RequirementEditWorkspace 
                    { 
                        ProjectId = projectId,
                        TargetContainerId = targetContainerId
                    };
                    _logger.LogInformation("[RequirementEditSessionService] Created new workspace");
                }

                // Update project/container if provided
                if (_currentWorkspace != null)
                {
                    _currentWorkspace.ProjectId = projectId;
                    if (targetContainerId.HasValue)
                    {
                        _currentWorkspace.TargetContainerId = targetContainerId;
                    }
                    _currentWorkspace.Timestamp = DateTime.UtcNow;
                }

                return _currentWorkspace ?? new();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RequirementEditSessionService] Failed to load workspace");
                _currentWorkspace = new RequirementEditWorkspace { ProjectId = projectId, TargetContainerId = targetContainerId };
                return _currentWorkspace;
            }
        }

        /// <summary>
        /// Merge Jama requirements with workspace edits.
        /// Creates edit session for each Jama requirement, overlays any local edits.
        /// </summary>
        public RequirementEditWorkspace MergeJamaRequirements(
            IEnumerable<Requirement> jamaRequirements,
            int projectId,
            int? targetContainerId = null)
        {
            if (_currentWorkspace == null)
            {
                _currentWorkspace = new RequirementEditWorkspace { ProjectId = projectId, TargetContainerId = targetContainerId };
            }

            var jamaIds = new HashSet<string>();

            foreach (var req in jamaRequirements)
            {
                if (string.IsNullOrWhiteSpace(req.ApiId)) 
                    continue;

                jamaIds.Add(req.ApiId);

                // Check if we already have an edit session for this requirement
                var existingSession = _currentWorkspace.Requirements.FirstOrDefault(r => r.JamaKey == req.ApiId);

                if (existingSession != null)
                {
                    // Update original fields from Jama (in case it changed)
                    existingSession.OriginalName = req.Name;
                    existingSession.OriginalDescription = req.Description;
                    existingSession.JamaKey = req.ApiId;
                }
                else
                {
                    // Create new edit session
                    if (!int.TryParse(req.ApiId, out var jamaIntId))
                        jamaIntId = req.ApiId.GetHashCode(); // Fallback if apiId is not numeric

                    var session = new RequirementEditSession
                    {
                        JamaId = jamaIntId,
                        JamaKey = req.ApiId,
                        OriginalName = req.Name,
                        CurrentName = req.Name,
                        OriginalDescription = req.Description,
                        CurrentDescription = req.Description
                    };
                    _currentWorkspace.Requirements.Add(session);
                }
            }

            // Remove sessions for requirements that no longer exist in Jama
            _currentWorkspace.Requirements.RemoveAll(r => !jamaIds.Contains(r.JamaKey) && !r.CommittedToJama);

            _logger.LogInformation("[RequirementEditSessionService] Merged {Count} Jama requirements, workspace has {Total} total", 
                jamaRequirements.Count(), _currentWorkspace.Requirements.Count);

            return _currentWorkspace;
        }

        /// <summary>
        /// Get or create an edit session for a requirement
        /// </summary>
        public RequirementEditSession GetOrCreateSession(Requirement requirement)
        {
            if (_currentWorkspace == null)
            {
                throw new InvalidOperationException("Workspace not loaded. Call LoadOrCreateWorkspaceAsync first.");
            }

            if (string.IsNullOrWhiteSpace(requirement.ApiId))
            {
                throw new ArgumentException($"Invalid requirement ApiId: {requirement.ApiId}");
            }

            var existing = _currentWorkspace.Requirements.FirstOrDefault(r => r.JamaKey == requirement.ApiId);
            if (existing != null)
                return existing;

            var jamaIntId = int.TryParse(requirement.ApiId, out var id) ? id : requirement.ApiId.GetHashCode();
            var session = new RequirementEditSession
            {
                JamaId = jamaIntId,
                JamaKey = requirement.ApiId,
                OriginalName = requirement.Name,
                CurrentName = requirement.Name,
                OriginalDescription = requirement.Description,
                CurrentDescription = requirement.Description
            };

            _currentWorkspace.Requirements.Add(session);
            return session;
        }

        /// <summary>
        /// Auto-save a requirement edit
        /// </summary>
        public async Task AutoSaveAsync(RequirementEditSession session, string editedBy)
        {
            if (_currentWorkspace == null)
                throw new InvalidOperationException("Workspace not loaded");

            session.LastEdited = DateTime.UtcNow;
            session.LastSaved = DateTime.UtcNow;
            session.EditedBy = editedBy;

            _logger.LogInformation("[RequirementEditSessionService] Auto-saving requirement {JamaId}", session.JamaId);

            try
            {
                await SaveWorkspaceAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RequirementEditSessionService] Failed to auto-save");
            }
        }

        /// <summary>
        /// Save entire workspace to disk
        /// </summary>
        public async Task SaveWorkspaceAsync()
        {
            if (_currentWorkspace == null)
            {
                _logger.LogWarning("[RequirementEditSessionService] No workspace to save");
                return;
            }

            try
            {
                var json = JsonSerializer.Serialize(_currentWorkspace, JsonOptions);
                await File.WriteAllTextAsync(_workspacePath, json);
                _logger.LogInformation("[RequirementEditSessionService] Saved workspace to {Path}", _workspacePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RequirementEditSessionService] Failed to save workspace");
                throw;
            }
        }

        /// <summary>
        /// Stage a requirement for commit
        /// </summary>
        public async Task StageForCommitAsync(int jamaId)
        {
            var session = _currentWorkspace?.Requirements.FirstOrDefault(r => r.JamaId == jamaId);
            if (session == null)
                throw new KeyNotFoundException($"Requirement {jamaId} not found in workspace");

            session.Stage();
            await SaveWorkspaceAsync();
            _logger.LogInformation("[RequirementEditSessionService] Staged requirement {JamaId}", jamaId);
        }

        /// <summary>
        /// Unstage a requirement
        /// </summary>
        public async Task UnstageAsync(int jamaId)
        {
            var session = _currentWorkspace?.Requirements.FirstOrDefault(r => r.JamaId == jamaId);
            if (session == null)
                throw new KeyNotFoundException($"Requirement {jamaId} not found in workspace");

            session.Unstage();
            await SaveWorkspaceAsync();
            _logger.LogInformation("[RequirementEditSessionService] Unstaged requirement {JamaId}", jamaId);
        }

        /// <summary>
        /// Mark requirements as committed
        /// </summary>
        public async Task MarkAsCommittedAsync(IEnumerable<int> jamaIds, string committedBy)
        {
            var count = 0;
            foreach (var jamaId in jamaIds)
            {
                var session = _currentWorkspace?.Requirements.FirstOrDefault(r => r.JamaId == jamaId);
                if (session != null)
                {
                    session.MarkCommitted(committedBy);
                    count++;
                }
            }

            await SaveWorkspaceAsync();
            _logger.LogInformation("[RequirementEditSessionService] Marked {Count} requirements as committed", count);
        }

        /// <summary>
        /// Get all staged requirements
        /// </summary>
        public List<RequirementEditSession> GetStagedRequirements() =>
            _currentWorkspace?.GetStagedRequirements() ?? new();

        /// <summary>
        /// Get all unsaved requirements
        /// </summary>
        public List<RequirementEditSession> GetUnsavedRequirements() =>
            _currentWorkspace?.GetUnsavedRequirements() ?? new();

        /// <summary>
        /// Clear/reset entire workspace
        /// </summary>
        public Task ClearWorkspaceAsync()
        {
            try
            {
                if (File.Exists(_workspacePath))
                {
                    File.Delete(_workspacePath);
                }
                _currentWorkspace = new();
                _logger.LogInformation("[RequirementEditSessionService] Cleared workspace");
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RequirementEditSessionService] Failed to clear workspace");
                throw;
            }
        }

        /// <summary>
        /// Convert edit session back to Requirement for Jama import
        /// </summary>
        public Requirement ToRequirement(RequirementEditSession session) =>
            new()
            {
                ApiId = session.JamaKey,
                Item = session.JamaKey,
                Name = session.CurrentName ?? session.OriginalName ?? "",
                Description = session.CurrentDescription ?? session.OriginalDescription ?? ""
            };
    }
}
