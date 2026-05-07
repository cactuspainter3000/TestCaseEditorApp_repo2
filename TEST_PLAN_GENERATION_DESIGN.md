# Test Plan Generation - Design Document

## Overview
This document outlines the design for implementing Test Plan generation functionality in the Test Case Editor App. The goal is to intelligently group generated test cases into logical Jama Test Plans.

## Problem Statement
- Currently, the app exports all test cases to a single test plan
- Users need to manually organize test cases into logical test plans for Jama
- "Find Similar Groups" feature exists but isn't connected to plan generation
- Need to decide: user-heavy upfront, user-heavy after LLM, or LLM-suggested with refinement?

## Recommended Approach: Hybrid LLM-Suggested + User Refinement

### Why This Works Best
1. **Leverages existing architecture** - "Find Similar Groups" semantic similarity detection already exists
2. **Reduces cognitive load** - Users don't design plans from scratch
3. **Maintains control** - Users can refine LLM suggestions before committing
4. **Scalable** - Works with 10 requirements or 1000+
5. **Aligns with Jama's model** - Test Plans are organizational containers with names

## Proposed Workflow

```
1. Generate Test Cases (existing)
   ↓
2. LLM Suggests Test Plans (NEW)
   - Groups test cases by: verification method, subsystem, risk level, feature area
   - Suggests plan names like "Boundary Scan Validation" or "Power-On Self-Test Suite"
   ↓
3. User Reviews Plan Assignments (NEW UI)
   - Drag-and-drop test cases between plans
   - Rename plans
   - Merge/split plans
   - Create new plans
   ↓
4. Export to Jama (enhanced)
   - Each test case assigned to its plan in CSV "Test Plan" column
```

## Implementation Strategy

### Phase 1: LLM-Suggested Plans (Minimal User Effort)

#### New Models

```csharp
namespace TestCaseEditorApp.MVVM.Domains.TestCaseCreation.Models
{
    /// <summary>
    /// Represents a test plan grouping of test cases for Jama organization
    /// </summary>
    public class TestPlan
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty; // "Verification Method", "Feature Area", "Subsystem"
        public List<string> TestCaseIds { get; set; } = new();
        public string Rationale { get; set; } = string.Empty; // Why LLM grouped these
        public int Priority { get; set; } = 0; // For execution order
        public DateTime CreatedDateUtc { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// LLM response for test plan suggestions
    /// </summary>
    public class TestPlanSuggestionResponse
    {
        public List<TestPlan> TestPlans { get; set; } = new();
        public string GeneratedPrompt { get; set; } = string.Empty;
        public string LLMResponse { get; set; } = string.Empty;
        public Dictionary<string, string> UnassignedTestCases { get; set; } = new(); // TestCaseId -> Reason
    }
}
```

#### New Service Interface

```csharp
namespace TestCaseEditorApp.MVVM.Domains.TestCaseCreation.Services
{
    public interface ITestPlanGenerationService
    {
        /// <summary>
        /// Analyzes generated test cases and suggests logical test plan groupings
        /// </summary>
        Task<TestPlanSuggestionResponse> SuggestTestPlansAsync(
            IEnumerable<LLMTestCase> testCases,
            IEnumerable<Requirement> requirements,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Re-analyzes test cases with user feedback to improve suggestions
        /// </summary>
        Task<TestPlanSuggestionResponse> RefineTestPlanSuggestionsAsync(
            IEnumerable<LLMTestCase> testCases,
            IEnumerable<TestPlan> existingPlans,
            string userFeedback,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Validates that all test cases are assigned to exactly one plan
        /// </summary>
        (bool isValid, List<string> errors) ValidatePlanAssignments(
            IEnumerable<TestPlan> plans,
            IEnumerable<LLMTestCase> testCases);
    }
}
```

#### LLM Prompt Strategy

```markdown
**System Prompt:**
You are an expert test engineer analyzing test cases for a Jama test management system.
Your task is to organize test cases into logical test plans based on best practices.

**User Prompt:**
Analyze the following test cases and group them into logical test plans for execution.

Consider these grouping factors:
1. Verification method (Test, Inspection, Demonstration, Analysis, etc.)
2. Related subsystems/components (e.g., Boundary Scan, Power Management, Communication)
3. Execution order dependencies
4. Test environment requirements
5. Risk level (critical safety vs. functional)
6. Test phase (unit, integration, system)

For each suggested test plan, provide:
- Name: Concise and descriptive (e.g., "Boundary Scan Coverage Tests")
- Description: Purpose and scope (1-2 sentences)
- Category: Primary grouping factor (e.g., "Verification Method: Test")
- TestCaseIds: List of test case IDs to include
- Rationale: Brief explanation of why these cases belong together
- Priority: Suggested execution order (1=highest)

Return valid JSON in this format:
{
  "testPlans": [
    {
      "name": "Plan Name",
      "description": "Plan purpose and scope",
      "category": "Verification Method: Test",
      "testCaseIds": ["TC-001", "TC-002"],
      "rationale": "These test cases validate boundary scan coverage",
      "priority": 1
    }
  ]
}

**Test Cases:**
[JSON array of test cases with id, name, description, steps, coveredRequirementIds]

**Requirements Context:**
[JSON array of requirements for additional context]
```

### Phase 2: Refine UI

#### New ViewModel: TestPlanOrganizerViewModel

```csharp
namespace TestCaseEditorApp.MVVM.Domains.TestCaseCreation.ViewModels
{
    /// <summary>
    /// Manages test plan organization and assignment
    /// </summary>
    public partial class TestPlanOrganizerViewModel : ObservableRecipient
    {
        private readonly ILogger<TestPlanOrganizerViewModel> _logger;
        private readonly ITestPlanGenerationService _planGenerationService;
        private CancellationTokenSource? _cancellationTokenSource;

        [ObservableProperty]
        private ObservableCollection<TestPlan> testPlans = new();

        [ObservableProperty]
        private ObservableCollection<LLMTestCase> allTestCases = new();

        [ObservableProperty]
        private TestPlan? selectedPlan;

        [ObservableProperty]
        private ObservableCollection<LLMTestCase> selectedPlanTestCases = new();

        [ObservableProperty]
        private bool isGenerating = false;

        [ObservableProperty]
        private string statusMessage = "Ready to organize test plans";

        public IAsyncRelayCommand GeneratePlanSuggestionsCommand { get; }
        public IAsyncRelayCommand RefreshSuggestionsCommand { get; }
        public IRelayCommand CreateNewPlanCommand { get; }
        public IRelayCommand<TestPlan> RenamePlanCommand { get; }
        public IRelayCommand<TestPlan> DeletePlanCommand { get; }
        public IRelayCommand<TestPlan> MergePlansCommand { get; }
        public IAsyncRelayCommand<LLMTestCase> MoveTestCaseCommand { get; }
        public IRelayCommand CancelCommand { get; }

        // Implementation methods...
    }
}
```

#### New View: TestPlanOrganizerView.xaml

**Layout:**
```
┌─────────────────────────────────────────────────────────────┐
│ Test Plan Organization                [Generate Suggestions] │
├─────────────────┬───────────────────────────────────────────┤
│ Test Plans (5)  │ Boundary Scan Validation Plan             │
│                 │ ─────────────────────────────────────────  │
│ ▶ Boundary Scan │ Description: Validates JTAG boundary scan  │
│   Tests (23)    │ Test Cases: 23                             │
│                 │                                            │
│ ▶ Power-On Self │ ┌──────────────────────────────────────┐ │
│   Test (12)     │ │ ☑ TC-001: Verify Tier 1 Boundary... │ │
│                 │ │ ☑ TC-002: Test XJTAG boundary...     │ │
│ ▢ Communication │ │ ☐ TC-003: Validate edge connector... │ │
│   Protocol (8)  │ │   [Move to...] [Remove from plan]    │ │
│                 │ └──────────────────────────────────────┘ │
│ [+ New Plan]    │                                            │
└─────────────────┴───────────────────────────────────────────┘
```

### Phase 3: Enhanced Export

#### Modify Export Service

```csharp
// In RequirementService.cs or new TestPlanExportService.cs

public string ExportTestCasesToCsvWithPlans(
    IEnumerable<TestPlan> testPlans,
    IEnumerable<LLMTestCase> testCases,
    IEnumerable<Requirement> requirements,
    string outPath,
    string jamaProject)
{
    // CSV columns: Project, Test Plan, Name, Description, Associated Requirements, 
    // Test Step Number, Test Step Action, Test Step Expected Result, Test Step Notes, Tags

    // Build lookup: TestCaseId -> TestPlan
    var testCaseToPlan = new Dictionary<string, string>();
    foreach (var plan in testPlans)
    {
        foreach (var tcId in plan.TestCaseIds)
        {
            testCaseToPlan[tcId] = plan.Name;
        }
    }

    // Export each test case with its assigned plan name
    foreach (var testCase in testCases)
    {
        var planName = testCaseToPlan.GetValueOrDefault(testCase.Id, "Unassigned");
        // Write CSV rows with planName in "Test Plan" column
    }
}
```

### Phase 4: Persistence

#### Add to Workspace Model

```csharp
// In MVVM/Models/Workspace.cs
public class Workspace
{
    // Existing properties...
    
    /// <summary>Test plan assignments for generated test cases</summary>
    public List<TestPlan>? TestPlans { get; set; }
    
    /// <summary>Test case to plan assignment map (TestCaseId -> PlanId)</summary>
    public Dictionary<string, string>? TestCaseAssignments { get; set; }
}
```

## Alternative Approaches (Evaluated but Not Recommended)

### Option A: User-Heavy Upfront ❌
**Flow:** User creates plans → Generate test cases → Manually assign to plans

**Cons:**
- Requires users to understand what test cases will be generated
- Too much cognitive load early in the process
- Disrupts current generation workflow

### Option B: Pure LLM with No Editing ❌
**Flow:** Generate test cases → LLM assigns to plans automatically → Export

**Cons:**
- Inflexible for domain-specific knowledge
- Users lose control and trust
- Hard to fix mistakes

### Option C: Tag-Based (Middle Ground) ⚠️
**Flow:** Generate test cases → LLM suggests tags → User creates plans from tags

**Pros:**
- More flexible than pure LLM
- Tags can be reused across projects

**Cons:**
- Requires extra step to create plans from tags
- Less intuitive than direct plan assignment

## Quick Win Implementation Path

### v1.0: LLM Smart Suggestions (MVP)
**Timeline:** 1-2 weeks

1. Create `TestPlan` model and `ITestPlanGenerationService`
2. Implement LLM prompt for plan suggestions
3. Add "Generate Test Plans" button to LLMTestCaseGeneratorView
4. Display suggested plans in new tab (read-only)
5. Add "Accept All Plans" button → populates TestPlans collection
6. Enhance CSV export to use TestPlan assignments

**Success Criteria:**
- LLM can suggest 3-7 logical plans for typical 20-50 test case set
- Export CSV contains test plan names in "Test Plan" column
- Plans are persisted in workspace file

### v2.0: Plan Refinement UI
**Timeline:** 2-3 weeks

1. Create `TestPlanOrganizerViewModel` and `TestPlanOrganizerView`
2. Implement drag-and-drop for moving test cases between plans
3. Add rename/delete/merge plan commands
4. Add "Create New Plan" with wizard
5. Implement "Re-run Suggestions" with context preservation

**Success Criteria:**
- Users can modify LLM suggestions without re-generating
- Drag-and-drop works smoothly
- Changes persist to workspace

### v3.0: Advanced Features
**Timeline:** 3-4 weeks

1. Plan templates (save common groupings)
2. AI learning from user edits
3. Plan history and version control
4. Bulk operations (merge, split, rebalance)
5. Plan execution order optimization
6. Dependencies between plans

## Integration Points

### Existing Services to Leverage
- `TestCaseDeduplicationService` → Already has similarity detection
- `AnythingLLMService` → LLM integration infrastructure
- `RAGContextService` → Can embed requirements for better grouping
- `RequirementService` → CSV export foundation

### New DI Registrations (App.xaml.cs)
```csharp
// Services
services.AddSingleton<ITestPlanGenerationService, TestPlanGenerationService>();

// ViewModels
services.AddSingleton<TestPlanOrganizerViewModel>();
```

### New DataTemplates (MainWindowResources.xaml)
```xaml
<DataTemplate DataType="{x:Type vm:TestPlanOrganizerViewModel}">
    <views:TestPlanOrganizerView/>
</DataTemplate>
```

### ViewConfiguration Changes
```csharp
// In ViewConfigurationService.cs
case "testplanorganizer":
    return CreateTestPlanOrganizerConfiguration(context);
```

## Success Metrics

### User Experience
- **Time savings:** Reduce manual plan organization from 30 min → 5 min
- **Accuracy:** 80%+ of LLM suggestions accepted without modification
- **Adoption:** 90%+ of users who generate test cases also use plan suggestions

### Technical
- **Performance:** Plan generation completes in <10 seconds for 100 test cases
- **Quality:** 95%+ of test cases assigned to appropriate plans
- **Reliability:** No unassigned test cases in export

## Future Enhancements

1. **Multi-workspace plans:** Reuse plans across projects
2. **Jama API integration:** Direct push to Jama Test Plan items
3. **Execution tracking:** Mark plans as "In Progress", "Completed"
4. **Test coverage heat map:** Visual representation of plan coverage by requirements
5. **AI-suggested execution order:** Based on dependencies and risk
6. **Plan comparison:** Compare current vs. previous plan structures

## Decision Log

| Date | Decision | Rationale |
|------|----------|-----------|
| 2026-02-10 | Hybrid LLM + User approach | Balances automation with control |
| 2026-02-10 | Phased implementation | Quick wins first, complex features later |
| 2026-02-10 | TestPlan as first-class model | Enables persistence and manipulation |

## References

- Jama Connect documentation: Test Plans as organizational containers
- Existing "Find Similar Groups" implementation in `TestCaseDeduplicationService.cs`
- Current CSV export in `RequirementService.ExportAllGeneratedTestCasesToCsv()`
- Project architecture in `ARCHITECTURAL_GUIDE_AI.md`
