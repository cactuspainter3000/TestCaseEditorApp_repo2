Absolutely. Below is your **edited and integrated version** of the document, with the suggested changes **woven in cleanly**, not bolted on. I’ve preserved your voice, intent, and structure, and added only what’s necessary to *guarantee* decoupled ViewModels, injected dependencies, and a mediator that speaks in **facts, not pixels**.

I’ll call out nothing inline. This is meant to be copy-paste authoritative.

---

# Architectural Guidelines & Decision Framework (Human Version)

> **Purpose**: Maintain architectural integrity during implementation and prevent vision drift.  
> **When to Use**: Before any major code changes, architectural decisions, or when unsure about design direction.  
> **AI Version**: See `ARCHITECTURAL_GUIDE_AI.md` for comprehensive AI-optimized patterns, implementation chains, and systematic validation.

---

## 🎯 Core Principles (The "Why")

### **Fail-Fast Architecture**

* Make architectural violations **impossible**, not just discouraged
* Constructor injection enforces dependencies (no ViewModels without mediators)
* Startup validation catches configuration errors before they cause runtime issues
* Type-safe communication prevents wrong event types crossing domain boundaries
* Architectural violations should surface at **compile time or startup**, never at runtime

### **Domain Alignment with User Experience**

* Code structure mirrors user mental model (main menu sections)
* **TestCaseGeneration Domain** = Everything under "Test Case Generator" menu
* **TestFlow Domain** = Everything under "Test Flow Generator" menu
* Domain boundaries are clear and obvious to developers and users

### **Shared Infrastructure, Isolated Domain Logic**

* Common concerns (LLM, persistence, notifications, UI coordination) are shared services
* Domain-specific business logic stays within domain boundaries
* Cross-domain communication is explicit, intentional, and type-safe

### **Developer Experience & Maintainability**

* Good patterns are easier than bad patterns
* Architectural intent is obvious from code structure
* Future developers can quickly understand domain boundaries and communication patterns
* Debugging and reasoning about system behavior is straightforward

---

## ✅ Pre-Change Decision Checklist

**Before implementing any feature, refactoring, or architectural change, verify:**

* [ ] **Domain Alignment**: Does this belong in TestCaseGeneration, TestFlow, or Shared layer?
* [ ] **File Organization**: Is this in the correct folder (domain-specific vs shared)?
* [ ] **Fail-Fast Compliance**: Will architectural violations be caught at startup/construction time?
* [ ] **Mediator Pattern**: Are ViewModels using domain mediators for communication?
* [ ] **Shared Services**: Is this using shared infrastructure appropriately (not duplicating)?
* [ ] **Type Safety**: Are domain events strongly typed and staying within domain boundaries?
* [ ] **Event Purity**: Do messages describe **facts or intents**, not UI mechanics?
* [ ] **UI Coordination**: Does UI feedback go through `IDomainUICoordinator` with domain context?
* [ ] **Dependency Injection**: Are dependencies injected, not hard-coded or directly instantiated?
* [ ] **Future Developer Clarity**: Will this be obvious to someone new to the codebase?

---

## 🏗️ Domain Boundaries & Responsibilities

### **TestCaseGeneration Domain**

**Scope**: Entire "Test Case Generator" main menu section

* Requirements: Import, analysis, management, search/filter
* Assumptions: Selection, management, persistence
* Questions: LLM-generated clarifying questions, answers
* Test Case Creation: Generation, validation, editing
* Export: Test cases to various formats

**Key Components**:

* `TestCaseGenerationMediator`
* `TestCaseGenerationEvents`
* Domain ViewModels (`TestCaseGeneratorViewModel`, `RequirementsViewModel`, etc.)
* Domain Services (`RequirementService`, `RequirementAnalysisService`)

---

### **TestFlow Domain**

**Scope**: Entire "Test Flow Generator" main menu section

* Flow Creation
* Flow Validation
* Flow Export

**Key Components**:

* `TestFlowMediator`
* `TestFlowEvents`
* Domain ViewModels and Services

---

### **Shared Infrastructure**

**Scope**: Cross-cutting concerns used by multiple domains

**Models**

* Core entities: `Requirement`, `TestCase`, `GeneratedTestCase`
* Project models: `Workspace`, `ValidationMethod`
* UI models: `ToastNotification`

**Services**

* LLM abstraction: `ITextGenerationService`, `LlmFactory`
* Persistence: `IPersistenceService`, `WorkspaceService`
* Notifications: `NotificationService`, `ToastNotificationService`
* UI coordination: `IDomainUICoordinator`

**Architecture Components**

* `BaseDomainMediator<TEvents>`
* `BaseDomainViewModel`
* `ViewModelFactory`

---

## 📡 Mediator Message Contract Rules (Critical)

### **Message Purpose**

The mediator communicates **what happened**, **what is requested**, or **what completed**.
It never communicates **how the UI should behave**.

### **Allowed Message Content**

* Domain IDs and domain models / DTOs
* Domain states and results
* Validation outcomes and errors
* Progress indicators
* Timestamps
* Correlation IDs

### **Forbidden Message Content**

Messages must **never** contain:

* View or control names
* XAML references
* UI element identifiers
* Selection indices or row numbers
* Scroll positions, focus targets, coordinates
* Dialog, window, or navigation instructions
* Phrases like “open”, “select”, “navigate”, “focus”

> **Litmus Test**
> If a message only makes sense when you know the XAML, it is **not** a domain event.

---

## 🧭 Event Taxonomy (Required)

All mediator communication must fall into one of these categories:

### **Notifications (Fire-and-Forget)**

* Domain facts that occurred
* No response expected

Examples:

* `RequirementImported`
* `TestCasesGenerated`
* `WorkspaceLoaded`

---

### **Requests (Response Required)**

* Domain actions that return a result
* Must include `CorrelationId`

Examples:

* `GenerateTestCasesRequest → GenerateTestCasesResult`
* `ValidateTestFlowRequest → ValidationResult`

---

### **Commands (Domain Intent)**

* Express intent to perform a domain action
* Still UI-agnostic

Examples:

* `StartImport`
* `BeginValidation`
* `ApplyAssumptions`

---

## 🔁 Correlation & Traceability Rules

* Any long-running or multi-step workflow **must** include a `CorrelationId`
* All responses must echo the originating `CorrelationId`
* Correlation IDs are required for:

  * Requests/responses
  * Cross-domain coordination
  * Debugging and diagnostics

---

## 🧠 Mediator Responsibilities

**Mediators may:**

* Publish domain events
* Orchestrate domain workflows
* Coordinate services within a domain
* Request cross-domain actions explicitly

**Mediators may not:**

* Reference Views or UI constructs
* Store UI state
* Dictate UI behavior
* Act as a global god-object

### **Mediator State Rule**

* Mediators may hold **only transient workflow state** (e.g., in-flight operations)
* Persistent state belongs in services or models

---

## 🎛️ UI Coordination Rules

### **IDomainUICoordinator Responsibilities**

* Show progress with domain context
* Display notifications with severity
* Request user confirmation and return semantic results

**Examples (Allowed):**

* `ShowProgress(domain, message, percent)`
* `Notify(domain, severity, message)`
* `RequestConfirmation(domain, question) → bool`

**Examples (Forbidden):**

* `OpenImportDialog()`
* `SwitchToTab("Assumptions")`
* `SelectRow(5)`
* `FocusSearchBox()`

### **Navigation & Dialogs**

* Navigation and dialog orchestration belong to the **App Shell / UI Composition layer**
* Domain logic must never control navigation directly

---

## 🔧 Required Implementation Patterns

### **ViewModel Creation**

```csharp
public class SomeTestCaseViewModel : BaseDomainViewModel
{
    public SomeTestCaseViewModel(
        ITestCaseGenerationMediator mediator,
        ILogger<SomeTestCaseViewModel> logger)
        : base(mediator, logger)
    {
    }
}
```

### **Domain Communication**

```csharp
mediator.PublishEvent(new TestCaseGenerationEvents.RequirementSelected
{
    RequirementId = req.Id,
    SelectedBy = SelectionSource.User
});
```

### **Cross-Domain Communication**

```csharp
await mediator.RequestCrossDomainAction(
    new RequestTestFlowValidation
    {
        TestCases = generatedTestCases,
        CorrelationId = correlationId
    });
```

### **Side Menu Command Integration**

**Adding Commands to Data-Driven Menu Items:**

1. **Create Command Property** in SideMenuViewModel:
```csharp
public ICommand MyNavigationCommand { get; private set; } = null!;
```

2. **Initialize Command** with mediator access:
```csharp
private void InitializeCommands()
{
    MyNavigationCommand = new RelayCommand(NavigateToMySection);
}

private void NavigateToMySection()
{
    if (_navigationMediator != null)
    {
        var viewModel = new MyViewModel();
        _navigationMediator.SetMainContent(viewModel);
    }
}
```

3. **Assign Command to Menu Item** after creation:
```csharp
// In InitializeDataDrivenTestCaseGenerator()
var myDropdown = CreateDropdown("my-section", "📋", "My Section", "Description", 
    // ... child items
);

// Set command after creation
myDropdown.Command = MyNavigationCommand;
```

**✅ CRITICAL**: The dropdown template (`MenuContentTemplates.xaml`) must include command binding:
```xml
<ToggleButton Command="{Binding Command}"
              CommandParameter="{Binding CommandParameter}" />
```

**✅ This enables both expand/collapse AND navigation from a single click**

---

## 🚨 Architectural Red Flags

**STOP immediately if you encounter:**

* ViewModels without mediator injection
* Direct ViewModel-to-ViewModel references
* UI-specific data in domain events
* Mediators issuing UI commands
* Domain logic in shared services
* Shared utilities placed in domain folders
* Domain helpers placed in shared folders
* Events crossing domains without explicit contracts

---

## 🚨 **Critical Migration Lessons (Hard-Won Knowledge)**

> **Context**: Lessons learned from requirements navigation refactoring and other architectural migrations.
> **Purpose**: Prevent future "harrowing" migration experiences by committing to architectural patterns upfront.

### **The Migration Trap: Why Hybrid Approaches Fail**

**❌ WHAT DOESN'T WORK (Tried and Failed):**

1. **Keeping Legacy for Reference**
   - Creates confusion about which pattern is "correct"
   - Developers copy old patterns instead of new ones
   - Mixed architectures are harder to debug than either pure approach

2. **Gradual/Systematic Migration** 
   - Hybrid old/new communication creates architectural debt
   - Incomplete dependency chains break at runtime
   - Threading issues from mixed patterns
   - "Almost working" code that's harder to fix than starting fresh

3. **Side-by-Side Implementation**
   - Maintaining two systems simultaneously
   - Complex cutover processes
   - Duplicate effort that doesn't reduce risk

### **✅ WHAT ACTUALLY WORKS: Full Architectural Commitment**

**The Pattern That Succeeds:**
```markdown
1. **Understand the new architecture fully** (study domain mediator patterns)
2. **Implement the new pattern completely** (don't try to preserve legacy)
3. **Follow dependency injection chains end-to-end** (MainViewModel → ViewModelFactory → Domain ViewModels → Mediators)
4. **Test cross-domain communication early** (BroadcastToAllDomains, UI thread marshaling)
5. **Delete legacy code completely** (no mixed patterns)
```

### **Migration Decision Framework**

**When facing legacy code that needs refactoring:**

🔴 **RED FLAG**: "Let's keep the old code and gradually migrate"
🟡 **YELLOW FLAG**: "Let's build alongside the old system"  
✅ **GREEN LIGHT**: "Let's implement the new architecture pattern completely"

### **Document Parser Selection**

**Pattern**: Use file extension for format detection, not filename patterns

```csharp
// In WorkspaceManagementMediator
var preferJamaParser = documentPath.EndsWith(".docx", StringComparison.OrdinalIgnoreCase);

if (preferJamaParser)
{
    // Jama parser handles version history filtering
    importedRequirements = await Task.Run(() => 
        requirementService.ImportRequirementsFromJamaAllDataDocx(documentPath));
}
else
{
    // Generic parser for other document types
    importedRequirements = await Task.Run(() => 
        requirementService.ImportRequirementsFromWord(documentPath));
}
```

**Why This Works**:
- Extension-based detection is more reliable than filename patterns
- Jama parser has specialized logic for filtering version history entries
- Most `.docx` requirements documents are Jama exports
- Preserves fallback for non-Jama documents

**Critical**: Jama documents contain version history entries that create duplicate requirements if processed by generic parsers.

---

## 🎓 Migration Lessons & Commitment Patterns

### **Hard-Learned Rules**

1. **Don't Mix Architectural Patterns**: Static mediators + Domain mediators = confusion
2. **Complete the DI Chain**: Missing links cause runtime failures that are hard to debug
3. **Trace Data Flow End-to-End**: Source → Transport → Destination → UI binding
4. **UI Thread Safety from Day One**: Cross-domain updates need `Dispatcher.Invoke`
5. **Validate Early**: Constructor injection with null checks prevents "works on my machine"
6. **Parser Selection Matters**: Wrong document parser can cause data duplication issues
7. **Avoid Circular Dependencies in DI**: Break cycles by separating creation from publishing concerns

### **Migration Success Indicators**

✅ **You're on the right track when:**
- New code follows domain patterns naturally
- Cross-domain communication works immediately  
- Dependency injection chains are complete and acyclic
- No mixed old/new patterns exist
- Build succeeds and features work without "quirks"
- Document imports work consistently regardless of filename
- Application starts without "Unable to resolve service" errors

❌ **Warning signs of migration trouble:**
- "Almost working" features that need constant tweaking
- Threading issues appearing during cross-domain communication
- Confusion about which communication pattern to use
- Complex workarounds to make old patterns work with new ones
- Inconsistent data imports from same documents
- **Circular dependency injection failures** - Services that need each other
- **"Unable to resolve service" runtime errors** - Broken DI registration chains

### **Circular Dependency Resolution Patterns** (January 4, 2026)

**Problem**: Circular dependencies in dependency injection prevent application startup
- Service A needs Service B
- Service B needs Service A or something Service A creates
- DI container can't resolve the circular reference

**✅ Successful Resolution Pattern:**
1. **Identify the minimal dependency**: What does each service actually use from the other?
2. **Break the cycle at the publishing boundary**: Remove event publishing from service constructors
3. **Use coordinator pattern**: Let a higher-level service handle the event publishing
4. **Prefer composition over injection**: Pass dependencies explicitly rather than injecting everything

**Example**: `ViewConfigurationService` ↔ `ViewAreaCoordinator` circular dependency
- **Problem**: Both needed `INavigationMediator` for event publishing
- **Solution**: `ViewConfigurationService` creates configuration, `ViewAreaCoordinator` publishes events
- **Result**: Clean separation of concerns, no circular dependency

---

## 🔍 Complex Debugging Methodology

### **Header Coordination Issue Case Study** (January 2, 2026)

**The Problem Pattern**: UI binding reads from one location, event system updates another location.

**Systematic Investigation Process**:

1. **Surface Symptom**: AnythingLLM status not displaying in Test Case Generator header
2. **First Layer**: Verified command assignment and execution (MessageBox debugging confirmed execution)
3. **Event Tracing**: Added Debug.WriteLine calls to trace navigation flow through multiple components
4. **Data Flow Analysis**: Discovered two separate header tracking systems:
   - `HeaderAreaViewModel.ActiveHeader` (UI binding source)  
   - `NavigationMediator._currentHeader` (event system storage)
5. **Timing Issue Discovery**: UI update event fired before data was available
6. **Root Cause**: Header coordination order was backwards

**The Solution Pattern**:
```csharp
// ❌ Wrong: Event fires before data is set
_navigationMediator.SetActiveHeader(header);  // Triggers UI update
HeaderArea.ShowTestCaseGeneratorHeader(header); // Sets the data

// ✅ Correct: Data available before UI update event  
HeaderArea.ShowTestCaseGeneratorHeader(header); // Sets the data
_navigationMediator.SetActiveHeader(header);  // Triggers UI update
```

**Key Architectural Lesson**: 
In mediator-based MVVM systems, **operation order is critical**:
- **Set Data → Trigger UI Update Event** ✅
- **Trigger UI Update Event → Set Data** ❌

**Debugging Tools That Helped**:
- `Debug.WriteLine` for Visual Studio debug output (not Console.WriteLine)
- MessageBox debugging to confirm command execution
- Binding error analysis in WPF debug output
- Systematic component isolation (navigation → header creation → UI binding)

**Prevention Strategy**:
- When UI bindings read from ViewModels, ensure data is set before publishing change events
- Consider using a single source of truth rather than parallel tracking systems
- Always verify the order of operations in mediator patterns
- Use debug output strategically to trace complex event-driven flows

---

## 🎯 Success Metrics

You’ll know the architecture is working when:

* Domain boundaries are obvious at a glance
* ViewModels are independently testable
* UI behavior changes without touching domain logic
* Mediators describe workflows, not UI choreography
* Cross-domain interactions are intentional and traceable
* Code reviews focus on logic, not architecture violations

---

---

## 🚀 Save System Implementation Status

### **Phase 3 Undo System - COMPLETE** (December 30, 2025)

**Enterprise-Grade Save Functionality Achieved**:
- ✅ **Phase 1**: Basic save/load with cross-domain coordination
- ✅ **Phase 2**: Backup system, validation, atomic operations  
- ✅ **Phase 3**: Full undo system with UI integration

**Key Features Implemented**:
- Automatic backup creation before every save operation
- Pre-save validation with duplicate requirement detection  
- Atomic file operations preventing corruption
- Complete undo functionality leveraging backup system
- UI integration with undo button and smart state management
- Cross-domain event broadcasting for full UI refresh after undo

**Architecture Validated**:
- All unit tests pass with zero regressions
- Application builds and runs successfully
- Mediator pattern properly handles async undo operations
- Persistence services fully integrated with backup/restore

**Current Limitation**:
- ⚠️ **Real-world testing blocked** pending requirements editing implementation
- Cannot fully test save→edit→undo workflow until editing features exist
- Architectural foundation complete and ready for testing

**Next Steps**: 
- Implement requirements editing functionality to enable full workflow testing
- Validate undo system behavior with real user scenarios
- Consider Phase 4 enhancements (multiple undo levels UI, backup management)

---

## 🏗️ **Architectural Conformity Analysis** (January 4, 2026)

### **Recent Refactor: View Configuration System (Commits 9948f61, 311e31f, 6c43cd6)**

#### **✅ Architectural Conformity Assessment**

**Domain Alignment**: ✅ EXCELLENT
- `ViewConfigurationService` in shared Services layer (cross-domain concern)
- Configurable ViewModels follow proper infrastructure pattern
- Domain-specific ViewModels stay in domain boundaries
- Clear separation between view creation and view coordination

**Fail-Fast Architecture**: ✅ EXCELLENT  
- Constructor injection enforces all dependencies
- Circular dependency eliminated at compile time through architectural refactoring
- INavigationMediator registration with factory function prevents runtime failures
- Null safety checks throughout configurable ViewModels

**Dependency Injection**: ✅ EXCELLENT
- Acyclic dependency graph after refactoring
- Clean separation of concerns (creation vs publishing)
- ViewAreaCoordinator properly orchestrates without creating circular references
- All services resolve successfully at application startup

**Event-Driven Communication**: ✅ EXCELLENT
- ViewConfiguration events properly typed and domain-neutral
- Idempotent configuration updates prevent unnecessary UI churn
- Clean broadcast → subscription → update pattern
- Cross-domain communication through well-defined event contracts

**Developer Experience**: ✅ EXCELLENT
- Easy to add new sections by extending configuration factory methods
- View sharing rules explicit and configurable
- Clear debugging through structured logging
- Obvious extension points for future enhancements

#### **🎯 Lessons Learned Applied**

1. **Full Architectural Commitment**: Complete removal of circular dependencies rather than workarounds
2. **End-to-End DI Chain**: Factory registration patterns ensure clean service resolution
3. **Separation of Concerns**: Configuration creation separate from event publishing
4. **Shared Infrastructure Pattern**: View coordination as infrastructure, not domain logic

---

*Last Updated: January 4, 2026 — Circular Dependency Resolution Pattern & View Configuration Analysis Added*