# TestCaseEditorApp - AI Agent Instructions

## Architecture Overview

**WPF Application (.NET 8)** with domain-driven architecture for test case generation and workflow management. Uses **dependency injection**, **mediator pattern**, and **MVVM** with fail-fast validation.

### Core Domains
- **TestCaseGeneration**: Requirements → Assumptions → Questions → Test Cases → Export
- **TestFlow**: Flow diagram creation and validation  
- **WorkspaceManagement**: Project/file operations
- **ChatGptExportAnalysis**: LLM integration analysis

## 🖥️ Workspace Architecture

### **4-Workspace Pattern**
- **MainWorkspace**: Primary content area
- **HeaderWorkspace**: Context-specific headers  
- **NavigationWorkspace**: Domain-specific navigation
- **SideMenuWorkspace**: Global menu coordination

### **MainViewModel Role**
- Simple container - sets up 4 workspace areas
- NO coordination logic
- Once workspace assigned → hands-off

### **Workspace Ownership**
- Each workspace controlled by its assigned ViewModel
- Cross-workspace communication via domain mediators ONLY
- No direct ViewModel-to-ViewModel communication

### **Workspace Switching**
- ViewAreaCoordinator manages coordinated workspace switches
- Side menu selection triggers all workspace updates
- **Cross-domain communication via existing broadcast mechanisms** (not direct subscriptions)

### **Legacy Architecture Warning**
- Avoid ViewAreaCoordinator injection in domain mediators
- Use existing `HandleBroadcastNotification` patterns
- Factory modifications usually indicate legacy approach

## Key Patterns & Conventions

### Domain Mediators (Critical Pattern)
Every domain has a mediator inheriting `BaseDomainMediator<TEvents>`:
```csharp
// Domain communication hub with fail-fast validation
public class TestCaseGenerationMediator : BaseDomainMediator<TestCaseGenerationEvents>
{
    // All domain ViewModels MUST be registered with their mediator
    // Constructor injection enforces dependencies
}
```

**ViewModels** must use domain mediators for cross-component communication - never directly reference other ViewModels.

### Fail-Fast Architecture
- Constructor injection prevents missing dependencies
- Startup validation catches configuration errors early
- `IsRegistered` property prevents unregistered ViewModel usage
- Type-safe communication prevents wrong event types crossing domain boundaries

### File Organization Rules
- **Domain-specific**: `MVVM/Domains/{DomainName}/ViewModels/`, `/Services/`, `/Mediators/`, `/Helpers/`, `/Converters/`
- **Shared infrastructure**: `Services/` (root level), `MVVM/Utils/`, `Converters/`, `Helpers/`
- **NO** placing domain ViewModels in `MVVM/ViewModels/` (legacy pattern being phased out)

### Component Classification Rules
**Move to Domain Folder If:**
- Contains business logic specific to one domain
- References domain-specific models or concepts
- Used primarily by one domain's ViewModels/Mediators

**Keep in Shared Folder If:**
- Pure utility function with no domain knowledge
- Used by multiple domains equally
- Infrastructure concern (logging, persistence, UI coordination)

## Critical Development Workflows

### Implementation Discovery Pattern (Critical First Step)
**BEFORE implementing ANY new feature:**

1. **Audit existing codebase** - Search for similar functionality patterns
2. **Check existing broadcasts** - Look for `HandleBroadcastNotification` implementations
3. **Validate complexity** - If requiring factory changes or complex dependencies, check simpler existing patterns
4. **Follow existing patterns** - 90% of requirements already have established patterns

**Golden Rule**: If implementation feels complex, audit what already exists first.

### Running Tests
```powershell
.\run-tests.ps1                    # All test projects
.\run-tests.ps1 -StopOnFailure    # Stop on first failure
```

### Building & Debugging
- Main project: `TestCaseEditorApp.csproj`
- Build warnings tracked in `build_warnings*.txt` files
- Test isolation via `Directory.Build.targets` (removes stray .deps.json files)

### LLM Integration Testing
Multiple integration test scripts exist:
- `test-enhanced-integration.ps1` - Full LLM pipeline testing
- `test-logic-conflicts.ps1` - Conflict resolution testing  
- `test-rag-status.ps1` - RAG system validation

### XAML/WPF Common Issues
**"View Not Showing" Troubleshooting:**
1. Check DataTemplate exists and is registered in App.xaml ResourceDictionary
2. Verify ViewModel inherits from BaseDomainViewModel
3. Validate StaticResource references (check /Resources/ and /Styles/ folders)
4. Ensure converter classes are registered in Application.Resources

**Required XAML Patterns:**
- Use StaticResource for ALL styling (never inline styles)
- Follow `{DomainName}_{Purpose}View.xaml` naming convention
- Include design-time DataContext for intellisense
- Register converters in App.xaml before use

## Service Layer Patterns

### LLM Services (AI Integration)
```csharp
// Factory pattern with environment-based selection
ITextGenerationService llm = LlmFactory.Create("ollama|openai|noop");

// Default: Ollama on localhost:11434 with phi4-mini model
// Set LLM_PROVIDER and OLLAMA_MODEL environment variables to override
```

### Application Services Facade
Use `IApplicationServices` to reduce constructor complexity:
```csharp
// Consolidates: RequirementService, PersistenceService, FileDialogService, etc.
public SomeViewModel(IApplicationServices appServices) { }
```

### Dependency Injection Setup
All services registered in `App.xaml.cs` using .NET Generic Host pattern with fail-fast validation.

## Data Flow Architecture

### Requirements Processing Pipeline
1. **Import** → `RequirementService.ImportFromFile()`
2. **Analysis** → `RequirementAnalysisService` with LLM integration
3. **Assumptions** → Domain mediator manages state
4. **Questions** → LLM-generated clarifying questions  
5. **Test Cases** → Generation with validation
6. **Export** → Multiple format support

### State Management
- Domain mediators hold workflow state (`_requirementAssumptions`, `_requirementQuestions`)
- ViewModels are stateless coordinators
- Persistence via `IPersistenceService` with JSON serialization

## Critical Anti-Patterns to Avoid

❌ **Direct ViewModel-to-ViewModel communication**  
✅ Use domain mediators for all cross-component communication

❌ **Placing domain ViewModels in `MVVM/ViewModels/`**  
✅ Domain-specific location: `MVVM/Domains/{Domain}/ViewModels/`

❌ **Hardcoded service instantiation**  
✅ Constructor injection with fail-fast validation

❌ **Domain-specific helpers/services/converters in shared folders**  
✅ Domain-specific components in domain folders

❌ **Cross-domain direct references**  
✅ Cross-domain communication via explicit contracts and coordinators

❌ **UI feedback without domain context**  
✅ Domain-specific error handling with UI coordinator

## ✅ Pre-Change Decision Checklist

Before implementing any feature, refactoring, or architectural change, verify:

- [ ] **Implementation Discovery**: Does existing code already handle this scenario?
- [ ] **Existing Patterns**: Are there similar implementations to follow?
- [ ] **Broadcast Check**: Does HandleBroadcastNotification already cover this?
- [ ] **Domain Alignment**: Does this belong in TestCaseGeneration, TestFlow, or Shared layer?
- [ ] **File Organization**: Is this in the correct folder (domain-specific vs shared)?
- [ ] **Fail-Fast Compliance**: Will architectural violations be caught at startup/construction time?
- [ ] **Mediator Pattern**: Are ViewModels using domain mediators for communication?
- [ ] **Shared Services**: Is this using shared infrastructure appropriately (not duplicating)?
- [ ] **Type Safety**: Are domain events strongly typed and staying within domain boundaries?
- [ ] **UI Coordination**: Does UI feedback go through IDomainUICoordinator with domain context?
- [ ] **Dependency Injection**: Are dependencies injected, not hard-coded or directly instantiated?
- [ ] **Future Developer Clarity**: Will this be obvious to someone new to the codebase?

## Required Implementation Patterns

### ViewModel Creation
```csharp
// ✅ CORRECT: Domain ViewModel with mediator injection
public class SomeTestCaseViewModel : BaseDomainViewModel
{
    public SomeTestCaseViewModel(ITestCaseGenerationMediator mediator, ILogger<SomeTestCaseViewModel> logger) 
        : base(mediator, logger)
    {
        // Mediator is enforced - architectural violation impossible
    }
}
```

### Domain Communication
```csharp
// ✅ CORRECT: Within domain using events
mediator.PublishEvent(new TestCaseGenerationEvents.RequirementSelected 
{ 
    Requirement = req, SelectedBy = "UserAction" 
});

// ✅ CORRECT: Cross-domain using existing broadcast mechanism
// Check HandleBroadcastNotification first before creating new subscriptions
mediator.BroadcastToAllDomains(notification);

// ✅ CORRECT: Use existing patterns
// Most cross-domain needs already handled by existing broadcasts
```

### UI Feedback
```csharp
// ✅ CORRECT: Domain-aware UI coordination
mediator.ShowProgress("Importing requirements...", 45);
// Results in: "Test Case Generator: Importing requirements... 45%"
```

## Configuration & Environment

### Key Files
- `Config/defaults.catalog.template.json` - Default configurations
- `Config/verification-methods.json` - Test verification methods
- `Config/RAG-*.md` - AI/RAG system optimization guides

### Environment Variables
- `LLM_PROVIDER`: ollama|openai|noop (default: ollama)
- `OLLAMA_MODEL`: Model name (default: phi4-mini:3.8b-q4_K_M)

## Current Modernization Status

**Phase 1** (In Progress): Moving ViewModels from legacy `MVVM/ViewModels/` to domain-specific locations
- ✅ RequirementsViewModel, TestCaseGeneratorViewModel moved
- 🔄 Additional TestCaseGenerator_* ViewModels being relocated

See `ARCHITECTURAL_MODERNIZATION_PLAN.md` for detailed migration status and `VIEWMODEL_CONSOLIDATION_PLAN.md` for duplicate file cleanup strategy.