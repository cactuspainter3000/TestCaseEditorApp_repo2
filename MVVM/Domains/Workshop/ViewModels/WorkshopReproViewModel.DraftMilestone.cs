using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.Services;

namespace TestCaseEditorApp.MVVM.Domains.Workshop.ViewModels;

public partial class DraftRequirementSelection : ObservableObject
{
    public DraftRequirementSelection(Requirement requirement) => Requirement = requirement;
    public Requirement Requirement { get; }
    [ObservableProperty] private bool isSelected;
    [ObservableProperty] private string result = string.Empty;
}

public partial class WorkshopReproViewModel
{
    public ObservableCollection<DraftRequirementSelection> DraftRequirements { get; } = new();
    public ObservableCollection<JamaDraftOption> DraftItemTypes { get; } = new();
    public ObservableCollection<JamaDraftOption> DraftContainers { get; } = new();
    public ObservableCollection<JamaDraftOption> DraftRelationshipTypes { get; } = new();
    [ObservableProperty] private JamaDraftOption? selectedDraftItemType;
    [ObservableProperty] private JamaDraftOption? selectedDraftContainer;
    [ObservableProperty] private JamaDraftOption? selectedDraftRelationshipType;
    [ObservableProperty] private string draftWorkflowStatus = "Load requirements, check the subset to cover, then generate local drafts.";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditDraftSelection))]
    private bool isDraftWorkflowBusy;
    public bool CanEditDraftSelection => !IsDraftWorkflowBusy && !IsGeneratingTestCases;
    public string DraftSelectionSummary => $"Checked requirements: {DraftRequirements.Count(row => row.IsSelected)} / {DraftRequirements.Count}";
    [ObservableProperty] private string draftPublicationPreview = string.Empty;
    private Workspace? _draftSelectionWorkspace;
    private Workspace? _draftOptionsWorkspace;
    private DraftPublishPlan? _draftPublishPlan;
    private const string DraftPending = "AI Draft - creation outcome unconfirmed";
    private const string DraftCreated = "AI Draft - created, link pending";
    private const string DraftPublished = "AI Draft - published and linked";
    private sealed record DraftPublishEntry(Requirement Requirement, TestCase TestCase, string RequirementApiId,
        string ExistingCaseApiId, string PublicationStatus, string Name, string Description);
    private sealed record DraftPublishPlan(Workspace Workspace, int ProjectId, JamaDraftOption Container,
        JamaDraftOption ItemType, JamaDraftOption RelationshipType, List<DraftPublishEntry> Entries);

    private bool CanRunDraftWorkflow() => !IsDraftWorkflowBusy && !IsGeneratingTestCases;

    partial void OnIsDraftWorkflowBusyChanged(bool value)
    {
        GenerateTestCasesCommand.NotifyCanExecuteChanged();
        LoadDraftRequirementsCommand.NotifyCanExecuteChanged();
        GenerateSelectedDraftsCommand.NotifyCanExecuteChanged();
        LoadDraftDestinationsCommand.NotifyCanExecuteChanged();
        PreviewDraftPublicationCommand.NotifyCanExecuteChanged();
        PublishDraftsCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanRunDraftWorkflow))]
    private void LoadDraftRequirements()
    {
        InvalidateDraftPreview();
        DraftRequirements.Clear();
        _draftSelectionWorkspace = _workspaceContext.CurrentWorkspace;
        foreach (var requirement in _mediator.Requirements)
        {
            var row = new DraftRequirementSelection(requirement)
            {
                Result = $"Existing local cases: {requirement.GeneratedTestCases.Count}"
            };
            row.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(DraftRequirementSelection.IsSelected))
                {
                    InvalidateDraftPreview();
                    OnPropertyChanged(nameof(DraftSelectionSummary));
                }
            };
            DraftRequirements.Add(row);
        }
        OnPropertyChanged(nameof(DraftSelectionSummary));
        DraftWorkflowStatus = $"Loaded {DraftRequirements.Count} requirements. Check only the requirements needed for this milestone.";
    }

    [RelayCommand(IncludeCancelCommand = true, CanExecute = nameof(CanRunDraftWorkflow))]
    private async Task GenerateSelectedDraftsAsync(CancellationToken ct)
    {
        InvalidateDraftPreview();
        IsDraftWorkflowBusy = true;
        var currentProgress = "Preparing draft generation...";
        var started = DateTime.UtcNow;
        var heartbeat = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        heartbeat.Tick += (_, _) =>
            DraftWorkflowStatus = $"{currentProgress} Elapsed: {DateTime.UtcNow - started:hh\\:mm\\:ss}. Completed requirements are checkpointed.";
        try
        {
            var workspace = RequireDraftWorkspace();
            var selected = DraftRequirements.Where(row => row.IsSelected).ToList();
            if (selected.Count == 0) throw new InvalidOperationException("Check at least one requirement.");
            var workspaceName = workspace.AnythingLLMWorkspaceSlug ?? workspace.AnythingLLMWorkspaceName;
            if (string.IsNullOrWhiteSpace(workspaceName))
                throw new InvalidOperationException("This project has no AnythingLLM workspace association.");
            CheckpointDrafts(workspace);
            _testCaseGenerationService.SetWorkspaceContext(workspaceName);
            heartbeat.Start();
            var completed = 0;
            var skipped = 0;
            var failed = 0;
            foreach (var row in selected)
            {
                ct.ThrowIfCancellationRequested();
                EnsureDraftWorkspace(workspace);
                var requirement = row.Requirement;
                if (requirement.GeneratedTestCases.Count > 0)
                {
                    row.Result = "Skipped: already has local cases (resume does not regenerate).";
                    skipped++;
                    continue;
                }
                var ordinal = ++completed;
                var progress = new Progress<string>(message =>
                {
                    currentProgress = $"{ordinal}/{selected.Count}: {message}";
                });
                currentProgress = $"{ordinal}/{selected.Count}: generating {requirement.Item}. This may take several minutes.";
                DraftWorkflowStatus = currentProgress;
                List<TestCase> additions;
                try
                {
                    var input = (requirement.Item, requirement.ApiId, requirement.Name, requirement.Description);
                    var generated = await _testCaseGenerationService.GenerateTestCasesForSingleRequirementAsync(
                        requirement, (message, _, _) => ((IProgress<string>)progress).Report(message), ct);
                    ct.ThrowIfCancellationRequested();
                    EnsureDraftWorkspace(workspace);
                    if (input != (requirement.Item, requirement.ApiId, requirement.Name, requirement.Description))
                        throw new InvalidOperationException("The requirement changed during generation; results were not attached.");
                    if (generated.Count == 0)
                        throw new InvalidDataException("No valid cases returned. Check AnythingLLM and generation logs.");
                    additions = AddGeneratedDrafts(requirement, generated);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) when (ReferenceEquals(_workspaceContext.CurrentWorkspace, workspace))
                {
                    row.Result = $"Failed: {ex.Message}";
                    TestCaseEditorApp.Services.Logging.Log.Error(ex, "Selected draft generation failed");
                    failed++;
                    continue;
                }
                _mediator.UpdateRequirement(requirement, new[] { nameof(Requirement.GeneratedTestCases) });
                // A failed checkpoint stops the run, rather than losing a whole batch.
                CheckpointDrafts(workspace);
                row.Result = $"Saved {additions.Count} draft case(s).";
            }
            DraftWorkflowStatus = $"Generation finished: {completed - failed} processed successfully, {skipped} skipped, {failed} failed. " +
                "Successful cases were saved locally. Review cases in Requirement Record > Test Case before publication.";
        }
        catch (OperationCanceledException)
        {
            DraftWorkflowStatus = "Generation canceled. Previously completed requirements remain saved; resume skips requirements with local cases.";
        }
        catch (Exception ex)
        {
            DraftWorkflowStatus = $"Generation stopped: {ex.Message}";
            TestCaseEditorApp.Services.Logging.Log.Error(ex, "Draft generation stopped");
        }
        finally
        {
            heartbeat.Stop();
            IsDraftWorkflowBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRunDraftWorkflow))]
    private async Task LoadDraftDestinationsAsync()
    {
        InvalidateDraftPreview();
        IsDraftWorkflowBusy = true;
        DraftItemTypes.Clear();
        DraftContainers.Clear();
        DraftRelationshipTypes.Clear();
        SelectedDraftItemType = SelectedDraftContainer = SelectedDraftRelationshipType = null;
        _draftOptionsWorkspace = null;
        try
        {
            var workspace = RequireDraftWorkspace();
            var projectId = GetPublishProjectId(workspace)
                ?? throw new InvalidOperationException("No unambiguous Jama project ID is saved.");
            var options = await _jamaConnectService.GetDraftPublicationOptionsAsync(projectId);
            EnsureDraftWorkspace(workspace);
            foreach (var type in options.ItemTypes) DraftItemTypes.Add(type);
            foreach (var container in options.Containers) DraftContainers.Add(container);
            foreach (var type in options.RelationshipTypes) DraftRelationshipTypes.Add(type);
            _draftOptionsWorkspace = workspace;
            DraftWorkflowStatus = $"Read Jama project {projectId}: {options.Containers.Count} existing case sets. " +
                "Choose the item type, compatible destination set, and relationship type. No remote changes were made.";
        }
        catch (Exception ex)
        {
            DraftWorkflowStatus = $"Could not load destinations: {ex.Message}";
            TestCaseEditorApp.Services.Logging.Log.Error(ex, "Draft destination discovery failed");
        }
        finally { IsDraftWorkflowBusy = false; }
    }

    [RelayCommand(CanExecute = nameof(CanRunDraftWorkflow))]
    private void PreviewDraftPublication()
    {
        InvalidateDraftPreview();
        try
        {
            var workspace = RequireDraftWorkspace();
            if (!ReferenceEquals(workspace, _draftOptionsWorkspace))
                throw new InvalidOperationException("Load destinations from the current Jama project first.");
            var projectId = GetPublishProjectId(workspace)
                ?? throw new InvalidOperationException("No unambiguous Jama project ID is saved.");
            var container = SelectedDraftContainer ?? throw new InvalidOperationException("Choose an existing destination set.");
            var itemType = SelectedDraftItemType ?? throw new InvalidOperationException("Choose the test-case item type.");
            var relationship = SelectedDraftRelationshipType ?? throw new InvalidOperationException("Choose the relationship type.");
            if (container.ChildItemType != itemType.Id)
                throw new InvalidOperationException("The selected set does not accept the selected case item type.");
            var entries = DraftRequirements.Where(row => row.IsSelected).SelectMany(row =>
                row.Requirement.GeneratedTestCases.Where(test => test.Status.StartsWith("AI Draft", StringComparison.Ordinal) &&
                    test.Status != DraftPublished).Select(test => BuildDraftEntry(row.Requirement, test))).ToList();
            if (entries.Count == 0) throw new InvalidOperationException("No unpublished AI draft cases belong to the checked requirements.");
            if (entries.Any(entry => entry.TestCase.Status == DraftPending && string.IsNullOrWhiteSpace(entry.TestCase.ApiId)))
                throw new InvalidOperationException("A previous creation has an unknown outcome. Inspect Jama and reconcile that case before retrying; it will not be automatically recreated.");
            if (entries.Any(entry => !int.TryParse(entry.Requirement.ApiId, out var id) || id <= 0))
                throw new InvalidOperationException("Every selected requirement must have a numeric Jama item ID.");
            if (entries.Any(entry => !string.IsNullOrWhiteSpace(entry.TestCase.ApiId) &&
                (!int.TryParse(entry.TestCase.ApiId, out var id) || id <= 0)))
                throw new InvalidOperationException("A previously created draft has an invalid Jama item ID. Reconcile it before publication.");
            if (entries.Select(entry => entry.TestCase).Distinct().Count() != entries.Count)
                throw new InvalidOperationException("The same case is attached more than once. Resolve duplicate requirement records before publishing.");
            CheckpointDrafts(workspace);
            _draftPublishPlan = new(workspace, projectId, container, itemType, relationship, entries);
            DraftPublicationPreview = $"Project: {workspace.JamaProjectName} (ID {projectId})\n" +
                $"Existing destination: {container}\nItem type: {itemType}\n" +
                $"Relationship: {relationship}; direction: requirement -> draft test case\n" +
                $"{entries.Count} case(s). Names are prefixed [AI DRAFT]; Jama workflow status is NOT automatically changed.\n" +
                "No folder creation, root fallback, or automatic creation retry. Inspect content before confirming.\n\n" +
                string.Join("\n\n", entries.Select(entry => $"{entry.Requirement.Item} -> {entry.Name}\n{entry.Description}"));
            DraftWorkflowStatus = "Preview ready. Review the project, set, relationship direction, and case contents. Publish is a separate action.";
        }
        catch (Exception ex)
        {
            DraftWorkflowStatus = $"Preview unavailable: {ex.Message}";
            TestCaseEditorApp.Services.Logging.Log.Error(ex, "Draft publication preview failed");
        }
    }

    [RelayCommand(CanExecute = nameof(CanRunDraftWorkflow))]
    private async Task PublishDraftsAsync()
    {
        var plan = _draftPublishPlan;
        if (plan == null)
        {
            DraftWorkflowStatus = "Create and review a fresh publication preview first.";
            return;
        }
        InvalidateDraftPreview();
        IsDraftWorkflowBusy = true;
        try
        {
            EnsureDraftWorkspace(plan.Workspace);
            if (GetPublishProjectId(plan.Workspace) != plan.ProjectId ||
                SelectedDraftContainer != plan.Container || SelectedDraftItemType != plan.ItemType ||
                SelectedDraftRelationshipType != plan.RelationshipType ||
                plan.Entries.Any(entry => !DraftRequirements.Any(row => row.IsSelected && row.Requirement == entry.Requirement) ||
                    !entry.Requirement.GeneratedTestCases.Contains(entry.TestCase) ||
                    BuildDraftEntry(entry.Requirement, entry.TestCase) != entry))
                throw new InvalidOperationException("The selection, case content, or destination changed. Review a new preview.");
            await _jamaConnectService.ValidateDraftDestinationAsync(plan.ProjectId, plan.Container.Id, plan.ItemType.Id,
                plan.Entries.Select(entry => int.Parse(entry.Requirement.ApiId)).ToList());
            var completed = 0;
            foreach (var entry in plan.Entries)
            {
                EnsureDraftWorkspace(plan.Workspace);
                if (GetPublishProjectId(plan.Workspace) != plan.ProjectId ||
                    BuildDraftEntry(entry.Requirement, entry.TestCase) != entry)
                    throw new InvalidOperationException("The case or requirement changed during publication. Review a new preview.");
                var test = entry.TestCase;
                if (test.Status == DraftPublished) continue;
                if (string.IsNullOrWhiteSpace(test.ApiId))
                {
                    if (test.Status == DraftPending)
                        throw new InvalidOperationException("Creation outcome is unknown. Inspect Jama before retrying.");
                    test.Status = DraftPending;
                    CheckpointDrafts(plan.Workspace);
                    DraftWorkflowStatus = $"Creating case {completed + 1}/{plan.Entries.Count}: {entry.Name}";
                    var id = await _jamaConnectService.CreateDraftTestCaseAsync(
                        plan.ProjectId, plan.Container.Id, plan.ItemType.Id, entry.Name, entry.Description);
                    test.ApiId = id.ToString();
                    test.Status = DraftCreated;
                    CheckpointDrafts(plan.Workspace);
                }
                await _jamaConnectService.LinkDraftTestCaseAsync(plan.ProjectId,
                    int.Parse(entry.RequirementApiId), int.Parse(test.ApiId), plan.RelationshipType.Id);
                test.Status = DraftPublished;
                CheckpointDrafts(plan.Workspace);
                completed++;
                _mediator.UpdateRequirement(entry.Requirement, new[] { nameof(Requirement.GeneratedTestCases) });
            }
            DraftWorkflowStatus = $"Published and linked {completed} AI draft case(s) to Jama project {plan.ProjectId}. Local item IDs were saved. Verify the first result in Jama.";
        }
        catch (Exception ex)
        {
            DraftWorkflowStatus = $"Publication stopped: {ex.Message} " +
                "Earlier results remain saved. Do not retry unknown creations; inspect Jama first. Known created cases can resume linking without recreation.";
            TestCaseEditorApp.Services.Logging.Log.Error(ex, "Draft publication stopped");
        }
        finally { IsDraftWorkflowBusy = false; }
    }

    private static DraftPublishEntry BuildDraftEntry(Requirement requirement, TestCase test) => new(
        requirement, test, requirement.ApiId, test.ApiId, test.Status, $"[AI DRAFT] {test.Name}",
        $"AI-generated draft - engineering review required.\nLocal case ID: {test.Id}\n" +
        $"Source requirement: {requirement.Item} (Jama item {requirement.ApiId})\n\n{test.TestCaseText}");

    private Workspace RequireDraftWorkspace()
    {
        var workspace = _workspaceContext.CurrentWorkspace ?? throw new InvalidOperationException("Open a saved project first.");
        if (!ReferenceEquals(workspace, _draftSelectionWorkspace))
            throw new InvalidOperationException("Load the requirement selection for this project first.");
        return workspace;
    }

    private void EnsureDraftWorkspace(Workspace workspace)
    {
        if (!ReferenceEquals(_workspaceContext.CurrentWorkspace, workspace) ||
            DraftRequirements.Any(row => row.IsSelected && !_mediator.Requirements.Contains(row.Requirement)))
            throw new InvalidOperationException("The project or selected requirements changed. Work stopped without sending further requests.");
    }

    private void CheckpointDrafts(Workspace workspace)
    {
        EnsureDraftWorkspace(workspace);
        var path = _workspaceContext.CurrentWorkspaceInfo?.Path;
        if (string.IsNullOrWhiteSpace(path)) throw new InvalidOperationException("No saved project path is available.");
        var saved = ProjectWorkspaceSave.Prepare(path, _mediator.Requirements);
        _draftPersistence.Save(path, saved);
    }

    private void InvalidateDraftPreview()
    {
        _draftPublishPlan = null;
        DraftPublicationPreview = string.Empty;
    }
}
