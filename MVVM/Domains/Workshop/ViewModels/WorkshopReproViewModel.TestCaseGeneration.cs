using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.IO;
using TestCaseEditorApp.MVVM.Domains.TestCaseCreation.Services;
using TestCaseEditorApp.MVVM.Models;
using TestCaseEditorApp.MVVM.Domains.TestCaseCreation.Models;

namespace TestCaseEditorApp.MVVM.Domains.Workshop.ViewModels;

public partial class WorkshopReproViewModel
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GenerateTestCasesCommand))]
    private bool isGeneratingTestCases;

    [ObservableProperty]
    private string testCaseGenerationStatus = "Select a requirement and generate test cases. Save Project to keep the results.";

    private bool CanGenerateTestCases() => CurrentRequirement != null && !IsGeneratingTestCases && !IsDraftWorkflowBusy;

    partial void OnIsGeneratingTestCasesChanged(bool value)
    {
        OnPropertyChanged(nameof(CanEditDraftSelection));
        LoadDraftRequirementsCommand.NotifyCanExecuteChanged();
        GenerateSelectedDraftsCommand.NotifyCanExecuteChanged();
        LoadDraftDestinationsCommand.NotifyCanExecuteChanged();
        PreviewDraftPublicationCommand.NotifyCanExecuteChanged();
        PublishDraftsCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanGenerateTestCases))]
    private async Task GenerateTestCasesAsync()
    {
        var requirement = CurrentRequirement;
        var workspace = _workspaceContext.CurrentWorkspace;
        if (requirement == null || workspace == null)
        {
            TestCaseGenerationStatus = "Open a project and select a requirement before generating test cases.";
            return;
        }

        var workspaceName = workspace.AnythingLLMWorkspaceSlug ?? workspace.AnythingLLMWorkspaceName;
        if (string.IsNullOrWhiteSpace(workspaceName))
        {
            TestCaseGenerationStatus = "Generation failed: this project has no AnythingLLM workspace association.";
            return;
        }

        IsGeneratingTestCases = true;
        TestCaseGenerationStatus = $"Generating test cases for {requirement.Item}...";
        try
        {
            _testCaseGenerationService.SetWorkspaceContext(workspaceName);
            string lastProgress = string.Empty;
            var input = (requirement.Item, requirement.ApiId, requirement.Name, requirement.Description);
            var generated = await _testCaseGenerationService.GenerateTestCasesForSingleRequirementAsync(
                requirement, (message, _, _) => lastProgress = message);
            if (!ReferenceEquals(_workspaceContext.CurrentWorkspace, workspace) ||
                !_mediator.Requirements.Contains(requirement))
            {
                TestCaseGenerationStatus = "Generation results were not added because the project or requirement changed. Return to the intended project and try again.";
                return;
            }
            if (input != (requirement.Item, requirement.ApiId, requirement.Name, requirement.Description))
                throw new InvalidOperationException("The requirement changed during generation; results were not attached.");

            if (generated.Count == 0)
            {
                TestCaseGenerationStatus = $"No test cases were added. {lastProgress} Check the AnythingLLM connection and generation logs before retrying.";
                return;
            }

            var additions = AddGeneratedDrafts(requirement, generated);

            if (additions.Count > 0)
            {
                _mediator.UpdateRequirement(requirement, new[] { nameof(Requirement.GeneratedTestCases) });
                if (ReferenceEquals(CurrentRequirement, requirement))
                    SelectedTestCase = additions[0];
                RefreshLifecycleCounts();
                NotifyLifecycleStateUiChanged();
            }
            TestCaseGenerationStatus = $"Added {additions.Count} test case(s) to {requirement.Item}. " +
                $"Skipped {generated.Count - additions.Count} duplicate ID(s). Review the cases, then Save Project. Nothing was published to Jama.";
        }
        catch (Exception ex)
        {
            TestCaseGenerationStatus = $"Test-case generation failed: {ex.Message}";
            TestCaseEditorApp.Services.Logging.Log.Error(ex, "Workshop test-case generation failed");
        }
        finally
        {
            IsGeneratingTestCases = false;
        }
    }

    private static List<TestCase> AddGeneratedDrafts(Requirement requirement, List<LLMTestCase> generated)
    {
        if (generated.Any(testCase => string.IsNullOrWhiteSpace(testCase.Id) ||
            string.IsNullOrWhiteSpace(testCase.Title) || testCase.Steps.Count == 0 ||
            testCase.Steps.Any(step => string.IsNullOrWhiteSpace(step.Action) || string.IsNullOrWhiteSpace(step.ExpectedResult)) ||
            testCase.CoveredRequirementIds.Count != 1 || testCase.CoveredRequirementIds[0] != requirement.Item))
            throw new InvalidDataException("Returned cases have missing IDs, titles, steps, expected results, or incorrect requirement links. No cases were added.");
        var additions = generated.DistinctBy(testCase => testCase.Id)
            .Where(testCase => !requirement.GeneratedTestCases.Any(existing => existing.Id == testCase.Id))
            .Select(testCase => testCase.ToWorkspaceTestCase()).ToList();
        foreach (var testCase in additions)
            requirement.GeneratedTestCases.Add(testCase);
        return additions;
    }
}
