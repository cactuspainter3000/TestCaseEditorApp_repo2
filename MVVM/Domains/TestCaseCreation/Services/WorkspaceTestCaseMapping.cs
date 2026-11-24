using System.Collections.ObjectModel;
using System.Text;
using TestCaseEditorApp.MVVM.Domains.TestCaseCreation.Models;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.MVVM.Domains.TestCaseCreation.Services;

public static class WorkspaceTestCaseMapping
{
    public static TestCase ToWorkspaceTestCase(this LLMTestCase source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var text = new StringBuilder(source.Description);
        text.AppendLine().AppendLine($"Preconditions: {source.Preconditions}");
        foreach (var step in source.Steps)
        {
            text.AppendLine().AppendLine($"{step.StepNumber}. {step.Action}")
                .AppendLine($"Expected: {step.ExpectedResult}")
                .AppendLine($"Test data: {step.TestData}")
                .AppendLine($"Notes: {step.Notes}");
        }
        text.AppendLine().AppendLine($"Expected result: {source.ExpectedResult}")
            .AppendLine($"Postconditions: {source.Postconditions}")
            .AppendLine($"Notes: {source.Notes}");
        return new TestCase
        {
            Id = source.Id,
            Name = source.Title,
            Status = "AI Draft",
            TestCaseText = text.ToString(),
            Tags = string.Join(", ", source.Tags),
            Steps = new ObservableCollection<TestStep>(source.Steps.Select(step => new TestStep
            {
                StepNumber = step.StepNumber,
                StepAction = step.Action,
                StepExpectedResult = step.ExpectedResult,
                StepNotes = $"Test data: {step.TestData}\n{step.Notes}"
            }))
        };
    }
}
