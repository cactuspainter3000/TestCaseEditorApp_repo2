# ViewModel Consolidation Plan

## Summary
Your ViewModels folder has significant duplication and fragmentation across 39 files. Here's a systematic plan to clean up and consolidate.

---

## 🔴 CRITICAL ISSUE #1: TestCaseCreatorHeaderViewModel (5 Files → 1 File)

### Current State
**5 separate partial files** with duplicate properties:

1. **TestCaseCreatorHeaderViewModel.cs** (main)
   - Defines: `IsLlmConnected`, `IsLlmBusy`, `WorkspaceName`, etc.
   - Has: `OllamaStatusMessage`, `OllamaStatusColor` computed properties

2. **TestCaseCreatorHeaderViewModel.Bindings.cs**
   - Defines: `TitleText`, `RequirementDescription`, `RequirementMethod`, etc.
   - Has: Property change handlers

3. **TestCaseCreatorHeaderViewModel.Additions.cs**
   - Has: `Initialize()`, `UpdateRequirements()`, `SetCurrentRequirement()`

4. **TestCaseCreatorHeaderViewModel.LlmConnection.cs**
   - Has: `AttachConnectionManager()`, `DetachConnectionManager()`

5. **TestCaseCreatorHeaderViewModel.Ollama.cs** ⚠️ **DUPLICATE!**
   - Re-defines: `IsLlmBusy`, `IsLlmConnected` (already in main file!)
   - Re-defines: `OllamaStatusMessage`, `OllamaStatusColor`

### ⚠️ Problems
- **IsLlmBusy** and **IsLlmConnected** defined TWICE (main + Ollama)
- Property change handlers defined in multiple places
- No single source of truth for the VM's state
- Hard to maintain and understand

### ✅ Solution
**Merge all 5 files into TestCaseCreatorHeaderViewModel.cs**

#### Step 1: Keep TestCaseCreatorHeaderViewModel.cs and enhance it
```csharp
public partial class TestCaseCreatorHeaderViewModel : ObservableObject
{
    // All state properties in one place
    [ObservableProperty] private string titleText = "Create Test Case";
    [ObservableProperty] private bool isLlmConnected;      // Single definition
    [ObservableProperty] private bool isLlmBusy;           // Single definition
    [ObservableProperty] private string? workspaceName = "Workspace";
    [ObservableProperty] private string? currentRequirementName;
    [ObservableProperty] private int requirementsWithTestCasesCount;
    [ObservableProperty] private string? statusHint;
    [ObservableProperty] private string? currentRequirementSummary;
    [ObservableProperty] private string requirementDescription = string.Empty;
    [ObservableProperty] private string requirementMethod = string.Empty;
    [ObservableProperty] private VerificationMethod? requirementMethodEnum = null;
    [ObservableProperty] private string statusMessage = string.Empty;
    [ObservableProperty] private bool requirementDescriptionHighlight = false;
    [ObservableProperty] private bool requirementMethodHighlight = false;

    // All commands
    public IRelayCommand? OpenRequirementsCommand { get; set; }
    // ... etc

    // All methods
    public void Initialize(TestCaseCreatorHeaderContext? ctx) { /* from Additions */ }
    public void UpdateRequirements(IEnumerable<Requirement>? reqs) { /* from Additions */ }
    public void SetCurrentRequirement(Requirement? req) { /* from Additions */ }
    public void AttachConnectionManager() { /* from LlmConnection */ }
    public void DetachConnectionManager() { /* from LlmConnection */ }
    
    // All computed properties
    public string OllamaStatusMessage => /* ... */;
    public Brush OllamaStatusColor => /* ... */;
}
```

#### Step 2: Delete these 4 files:
- ❌ TestCaseCreatorHeaderViewModel.Bindings.cs
- ❌ TestCaseCreatorHeaderViewModel.Additions.cs
- ❌ TestCaseCreatorHeaderViewModel.LlmConnection.cs
- ❌ TestCaseCreatorHeaderViewModel.Ollama.cs

---

## 🔴 CRITICAL ISSUE #2: MainViewModel (3 Files → 1 File)

### Current State
**3 separate partial files** with duplicate properties:

1. **MainViewModel.cs** (2000+ lines - TOO LARGE!)
   - Has everything including TestFlowSteps, SapStatus

2. **MainViewModel.Bindings.cs**
   - Re-defines: `TestFlowSteps`, `SapStatus`, `SapForegroundStatus`

3. **MainViewModel.SideMenuProps.cs**
   - Re-defines: `TestFlowSteps`, `SapStatus`, `SapForegroundStatus` again!

### ⚠️ Problems
- **TestFlowSteps** defined THREE times!
- **SapStatus** defined THREE times!
- 2000+ line main file is unmanageable
- Duplicate property definitions cause compiler conflicts

### ✅ Solution
**Keep MainViewModel.cs, delete the other 2 files**

The properties in Bindings.cs and SideMenuProps.cs are already in MainViewModel.cs. Simply delete:
- ❌ MainViewModel.Bindings.cs
- ❌ MainViewModel.SideMenuProps.cs

---

## 🟡 MEDIUM PRIORITY: Header ViewModels Consolidation

### Current State
You have **5 different header ViewModels**:

1. **TestCaseCreatorHeaderViewModel** - Complex, feature-rich (keep this)
2. **WorkspaceHeaderViewModel** - Window controls + workspace info
3. **TitleBarViewModel** - Window controls only
4. **TestCaseHeaderViewModel** - Empty stub (3 no-op commands)
5. **TestFlowHeaderViewModel** - Empty stub (2 no-op commands)

### ⚠️ Problems
- TitleBarViewModel and WorkspaceHeaderViewModel both have Close/Minimize/MaximizeRestore
- TestCaseHeaderViewModel and TestFlowHeaderViewModel are unused stubs

### ✅ Solution

**Option A: Merge TitleBarViewModel into WorkspaceHeaderViewModel**
```csharp
// WorkspaceHeaderViewModel already has:
// - Close/Minimize/MaximizeRestore commands
// - Workspace state (name, current requirement, etc.)
// TitleBarViewModel adds nothing new
```

**Option B: Keep both if they serve different UI areas**
- TitleBarViewModel: Simple title bar only
- WorkspaceHeaderViewModel: Rich header with workspace context
- TestCaseCreatorHeaderViewModel: Test case creation context

**Delete these stub files:**
- ❌ TestCaseHeaderViewModel.cs (3 empty command stubs)
- ❌ TestFlowHeaderViewModel.cs (2 empty command stubs)

---

## 🟡 MEDIUM PRIORITY: ClarifyingQuestionsViewModel (2 Files → 1 File)

### Current State
**2 partial files**:

1. **ClarifyingQuestionsViewModel.cs** (main implementation)
2. **ClarifyingQuestionsViewModel.Commands.cs** (command initialization)

### ⚠️ Problems
- Commands.cs only has InitializeCommands() and helper methods
- Unnecessary file split for a relatively small VM

### ✅ Solution
**Merge Commands.cs into main file**

Move the InitializeCommands() method and supporting logic into ClarifyingQuestionsViewModel.cs. Delete:
- ❌ ClarifyingQuestionsViewModel.Commands.cs

---

## 🟡 MEDIUM PRIORITY: RequirementsViewModel (2 Files → 1 File)

### Current State
**2 partial files**:

1. **RequirementsViewModel.cs** (main implementation)
2. **RequirementsViewModel.Bindings.cs** (just one property: HasMeta)

### ⚠️ Problems
- Bindings.cs has only ONE property
- Pointless file split

### ✅ Solution
**Merge Bindings.cs into main file**

Add `[ObservableProperty] private bool hasMeta;` to RequirementsViewModel.cs. Delete:
- ❌ RequirementsViewModel.Bindings.cs

---

## 🟢 LOW PRIORITY: No-Op Service Stubs

### Current State
**3 stub service files in ViewModels folder**:

1. **NoOpFileDialogService.cs**
2. **NoOpPersistenceService.cs**
3. **NoOpRequirementService.cs**

### ⚠️ Problems
- These are services, not ViewModels
- Should be in Services folder or Tests folder

### ✅ Solution
**Move to appropriate folder**

Either:
- Move to `Services/Testing/` folder
- Move to `Tests/` folder
- Delete if only used for design-time (MainViewModel has its own copies)

---

## 📋 Implementation Checklist

### Phase 1: Critical Consolidations (Do First!)
- [ ] **TestCaseCreatorHeaderViewModel**: Merge 5 files → 1
  - [ ] Copy all content into TestCaseCreatorHeaderViewModel.cs
  - [ ] Remove duplicate IsLlmBusy/IsLlmConnected from Ollama.cs
  - [ ] Delete 4 partial files
  - [ ] Build and test

- [ ] **MainViewModel**: Delete 2 duplicate partial files
  - [ ] Verify properties exist in MainViewModel.cs
  - [ ] Delete MainViewModel.Bindings.cs
  - [ ] Delete MainViewModel.SideMenuProps.cs
  - [ ] Build and test

### Phase 2: Medium Priority Cleanup
- [ ] **Header VMs**: Delete stub files
  - [ ] Delete TestCaseHeaderViewModel.cs
  - [ ] Delete TestFlowHeaderViewModel.cs
  - [ ] Consider merging TitleBarViewModel into WorkspaceHeaderViewModel

- [ ] **ClarifyingQuestionsViewModel**: Merge Commands partial
  - [ ] Move InitializeCommands() to main file
  - [ ] Delete ClarifyingQuestionsViewModel.Commands.cs

- [ ] **RequirementsViewModel**: Merge Bindings partial
  - [ ] Add HasMeta property to main file
  - [ ] Delete RequirementsViewModel.Bindings.cs

### Phase 3: Cleanup
- [ ] Move NoOp services to appropriate folder
- [ ] Update any references in .csproj if needed
- [ ] Run full rebuild and tests

---

## Expected Outcome

### Before
- 39 ViewModel files
- 5 files for TestCaseCreatorHeaderViewModel
- 3 files for MainViewModel
- Multiple duplicate property definitions
- Hard to navigate and maintain

### After
- ~25-30 ViewModel files (-30%)
- 1 file for TestCaseCreatorHeaderViewModel
- 1 file for MainViewModel
- Single source of truth for all properties
- Clear, maintainable structure

---

## Risk Assessment

### Low Risk ✅
- Deleting stub files (TestCaseHeaderViewModel, TestFlowHeaderViewModel)
- Deleting duplicate partial files (after verifying main file has all content)

### Medium Risk ⚠️
- Merging TestCaseCreatorHeaderViewModel (many references, but well-defined)
- Moving NoOp services (may need to update using statements)

### How to Mitigate
1. **Use Git**: Commit before each change
2. **Build frequently**: After each file deletion, rebuild
3. **Test incrementally**: Don't merge everything at once
4. **Keep backups**: The partial files you're deleting

---

## Need Help?

Would you like me to:
1. **Generate the merged TestCaseCreatorHeaderViewModel.cs** file for you?
2. **Show you exactly which properties to keep** from each partial?
3. **Create a script to automate** the file deletions?
4. **Start with just one consolidation** as a proof of concept?

Let me know and I'll help you execute this plan! 🚀
