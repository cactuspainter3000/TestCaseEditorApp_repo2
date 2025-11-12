using System.Linq;
using System.Collections.Generic;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.MVVM.ViewModels
{
    // Partial to add initialization/update helpers so the header VM owns presentation logic.
    public partial class TestCaseCreatorHeaderViewModel
    {
        // Populate observable properties from a context object.
        public void Initialize(TestCaseCreatorHeaderContext? ctx)
        {
            WorkspaceName = ctx?.WorkspaceName ?? string.Empty;

            UpdateRequirements(ctx?.Requirements);

            StatusHint = "Test Case Creator";

            // Wire commands (MainViewModel supplies the ICommand/IRelayCommand instances)
            ImportWordCommand = ctx?.ImportCommand;
            LoadWorkspaceCommand = ctx?.LoadWorkspaceCommand;
            SaveWorkspaceCommand = ctx?.SaveWorkspaceCommand;
            ReloadCommand = ctx?.ReloadCommand;
            ExportAllToJamaCommand = ctx?.ExportAllToJamaCommand;
            HelpCommand = ctx?.HelpCommand;

            OpenRequirementsCommand = ctx?.OpenRequirementsCommand;
            OpenWorkspaceCommand = ctx?.OpenWorkspaceCommand;
            SaveCommand = ctx?.SaveCommand;
        }

        // Helper to recompute the count of requirements with test cases.
        public void UpdateRequirements(IEnumerable<Requirement>? reqs)
        {
            try
            {
                RequirementsWithTestCasesCount = reqs?.Count(r =>
                {
                    try
                    {
                        return (r != null) && ((r.GeneratedTestCases != null && r.GeneratedTestCases.Count > 0) || r.HasGeneratedTestCase);
                    }
                    catch { return false; }
                }) ?? 0;
            }
            catch
            {
                RequirementsWithTestCasesCount = 0;
            }
        }

        // New: header-controlled mapping from Requirement -> visible fields.
        // Keeps presentation logic inside the header VM.
        public void SetCurrentRequirement(Requirement? req)
        {
            // Name (short title) -> CurrentRequirementName
            CurrentRequirementName = req?.Name ?? string.Empty;

            // Use the strongly-typed Description property from Requirement for the summary.
            CurrentRequirementSummary = req?.Description ?? string.Empty;
        }
    }
}