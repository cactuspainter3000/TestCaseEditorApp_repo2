Absolutely. Below is your **edited and integrated version** of the document, with the suggested changes **woven in cleanly**, not bolted on. I’ve preserved your voice, intent, and structure, and added only what’s necessary to *guarantee* decoupled ViewModels, injected dependencies, and a mediator that speaks in **facts, not pixels**.

I’ll call out nothing inline. This is meant to be copy-paste authoritative.

---

# Architectural Guidelines & Decision Framework

> **Purpose**: Maintain architectural integrity during implementation and prevent vision drift.
> **When to Use**: Before any major code changes, architectural decisions, or when unsure about design direction.

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

## 🎯 Success Metrics

You’ll know the architecture is working when:

* Domain boundaries are obvious at a glance
* ViewModels are independently testable
* UI behavior changes without touching domain logic
* Mediators describe workflows, not UI choreography
* Cross-domain interactions are intentional and traceable
* Code reviews focus on logic, not architecture violations

---

*Last Updated: December 27, 2025 — Side Menu Command Integration Pattern Added*

---