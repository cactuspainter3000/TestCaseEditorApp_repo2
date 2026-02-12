using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.MVVM.Domains.TestCaseCreation.Models
{
    /// <summary>
    /// Represents a requirement reference under a test case
    /// </summary>
    public partial class RequirementReference : ObservableObject
    {
        [ObservableProperty] private string _requirementId = string.Empty;
        [ObservableProperty] private string _requirementTitle = string.Empty;
        [ObservableProperty] private bool _isExpanded;
        [ObservableProperty] private Requirement? _requirementDetails;

        public RequirementReference(Requirement requirement)
        {
            RequirementDetails = requirement ?? throw new ArgumentNullException(nameof(requirement));
            RequirementId = requirement.GlobalId ?? "N/A";
            RequirementTitle = requirement.Name ?? requirement.Description ?? "Untitled Requirement";
        }

        /// <summary>
        /// Display text for the requirement reference
        /// </summary>
        public string DisplayText => RequirementId;
    }

    /// <summary>
    /// Represents a test case group with its associated requirements for hierarchical display
    /// </summary>
    public partial class TestCaseGroup : ObservableObject
    {
        [ObservableProperty] private bool _isExpanded;
        [ObservableProperty] private string _testCaseId = string.Empty;
        [ObservableProperty] private string _testCaseTitle = string.Empty;
        [ObservableProperty] private TestCase? _testCaseDetails;
        
        public ObservableCollection<RequirementReference> AssociatedRequirements { get; }

        public TestCaseGroup(TestCase testCase)
        {
            TestCaseDetails = testCase ?? throw new ArgumentNullException(nameof(testCase));
            TestCaseId = testCase.Id ?? "N/A";
            TestCaseTitle = testCase.Name ?? "Untitled Test Case";
            AssociatedRequirements = new ObservableCollection<RequirementReference>();
        }

        /// <summary>
        /// Display text for the test case group header
        /// </summary>
        public string GroupHeader => $"Test Case: {TestCaseId}";

        /// <summary>
        /// Combined title for tooltip/expanded view
        /// </summary>
        public string CombinedTitle => $"{TestCaseTitle} (associated with {AssociatedRequirements.Count} requirements)";

        /// <summary>
        /// Add a requirement reference to this test case group
        /// </summary>
        public void AddRequirement(Requirement requirement)
        {
            if (requirement != null)
            {
                AssociatedRequirements.Add(new RequirementReference(requirement));
            }
        }
    }
}