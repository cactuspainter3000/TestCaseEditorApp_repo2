# Architectural Guidelines & Decision Framework

> **Purpose**: Maintain architectural integrity during implementation and prevent vision drift.  
> **When to Use**: Before any major code changes, architectural decisions, or when unsure about design direction.

## 🎯 Core Principles (The "Why")

### **Fail-Fast Architecture**
- Make architectural violations **impossible**, not just discouraged
- Constructor injection enforces dependencies (no ViewModels without mediators)
- Startup validation catches configuration errors before they cause runtime issues
- Type-safe communication prevents wrong event types crossing domain boundaries

### **Domain Alignment with User Experience**
- Code structure mirrors user mental model (main menu sections)
- **TestCaseGeneration Domain** = Everything under "Test Case Generator" menu
- **TestFlow Domain** = Everything under "Test Flow Generator" menu  
- Domain boundaries are clear and obvious to developers and users

### **Shared Infrastructure, Isolated Domain Logic**
- Common concerns (LLM, persistence, notifications, UI coordination) are shared services
- Domain-specific business logic stays within domain boundaries
- Cross-domain communication is explicit and type-safe

### **Developer Experience & Maintainability**
- Good patterns are easier than bad patterns
- Architectural intent is obvious from code structure
- Future developers can quickly understand domain boundaries and communication patterns

---

## ✅ Pre-Change Decision Checklist

**Before implementing any feature, refactoring, or architectural change, verify:**

- [ ] **Domain Alignment**: Does this belong in TestCaseGeneration, TestFlow, or Shared layer?
- [ ] **File Organization**: Is this in the correct folder (domain-specific vs shared)?
- [ ] **Fail-Fast Compliance**: Will architectural violations be caught at startup/construction time?
- [ ] **Mediator Pattern**: Are ViewModels using domain mediators for communication?
- [ ] **Shared Services**: Is this using shared infrastructure appropriately (not duplicating)?
- [ ] **Type Safety**: Are domain events strongly typed and staying within domain boundaries?
- [ ] **UI Coordination**: Does UI feedback go through IDomainUICoordinator with domain context?
- [ ] **Dependency Injection**: Are dependencies injected, not hard-coded or directly instantiated?
- [ ] **Future Developer Clarity**: Will this be obvious to someone new to the codebase?

---

## 🏗️ Domain Boundaries & Responsibilities

### **TestCaseGeneration Domain**
**Scope**: Entire "Test Case Generator" main menu section
- **Requirements**: Import, analysis, management, search/filter
- **Assumptions**: Selection, management, persistence
- **Questions**: LLM-generated clarifying questions, answers
- **Test Case Creation**: Generation, validation, editing
- **Export**: Test cases to various formats

**Key Components**:
- `TestCaseGenerationMediator` (domain coordinator)
- `TestCaseGenerationEvents` (all domain events)
- `TestCaseGeneratorViewModel`, `RequirementsViewModel` (domain ViewModels)
- `RequirementService`, `RequirementAnalysisService` (domain services)

### **TestFlow Domain**  
**Scope**: Entire "Test Flow Generator" main menu section
- **Flow Creation**: Building test flow diagrams/workflows
- **Flow Validation**: Ensuring flow correctness and completeness
- **Flow Export**: Exporting flows to various formats

**Key Components**:
- `TestFlowMediator` (domain coordinator)
- `TestFlowEvents` (all domain events)
- Future TestFlow ViewModels and services

### **Shared Infrastructure**
**Scope**: Cross-cutting concerns used by multiple domains

**Models**:
- `Requirement`, `TestCase`, `GeneratedTestCase` - Core domain entities
- `Workspace`, `ValidationMethod` - Project-level models
- `ToastNotification` - UI infrastructure models

**Services**:
- `ITextGenerationService`, `LlmFactory` - LLM abstraction layer
- `IPersistenceService`, `WorkspaceService` - Data persistence
- `NotificationService`, `ToastNotificationService` - User notifications
- `IDomainUICoordinator` - UI coordination with domain context

**Architecture Components**:
- `BaseDomainMediator<TEvents>` - Foundation for domain mediators
- `BaseDomainViewModel` - Foundation for domain ViewModels
- `ViewModelFactory` - Dependency injection and mediator registration

---

## � Code Organization Patterns

### **Domain-Specific Components**

Domain-specific logic should be **co-located** with the domain that owns it:

```
MVVM/
  Domains/
    TestCaseGeneration/
      Mediators/TestCaseGenerationMediator.cs
      ViewModels/RequirementsViewModel.cs, TestCaseGeneratorViewModel.cs
      Services/RequirementAnalysisService.cs, ClarifyingQuestionService.cs
      Helpers/RequirementImportHelper.cs, TableConversionHelper.cs
      Converters/ImportWorkflowConverters.cs
      Events/TestCaseGenerationEvents.cs
    TestFlow/
      Mediators/TestFlowMediator.cs
      ViewModels/TestFlowViewModel.cs
      Services/TestFlowValidationService.cs
      Helpers/FlowDiagramHelper.cs
      Events/TestFlowEvents.cs
```

### **Shared Infrastructure**

Cross-cutting concerns stay in **root folders** for easy discovery:

```
Services/           # Shared services used by multiple domains
  ITextGenerationService.cs, LlmFactory.cs
  NotificationService.cs, WorkspaceService.cs
  IDomainUICoordinator.cs, DomainUICoordinator.cs

Converters/         # Generic UI converters (not domain-specific)
  BoolToAngleConverter.cs, RelativeTimeConverter.cs

Helpers/            # Generic utilities (not domain-specific)
  FileNameHelper.cs, DialogHelper.cs
  
MVVM/
  Models/           # Shared domain entities
    Requirement.cs, TestCase.cs, Workspace.cs
  Utils/            # MVVM infrastructure
    BaseDomainMediator.cs, BaseDomainViewModel.cs
```

### **Migration Strategy**

**Current State → Target State:**

1. **Services/RequirementAnalysisService.cs** → `MVVM/Domains/TestCaseGeneration/Services/`
2. **Services/ClarifyingQuestionService.cs** → `MVVM/Domains/TestCaseGeneration/Services/`
3. **Helpers/TableConversionHelper.cs** → `MVVM/Domains/TestCaseGeneration/Helpers/`
4. **Converters/ImportWorkflowConverters.cs** → `MVVM/Domains/TestCaseGeneration/Converters/`
5. **Import/** folder contents → `MVVM/Domains/TestCaseGeneration/Services/` (rename files appropriately)

**What Stays in Root:**
- Generic services: NotificationService, WorkspaceService, LlmFactory
- Generic helpers: FileNameHelper, DialogHelper  
- Generic converters: BoolToAngleConverter, RelativeTimeConverter
### **Decision Criteria: Domain-Specific vs Shared**

**Move to Domain Folder If:**
- ✅ Contains business logic specific to one domain (Requirements, TestCases, etc.)
- ✅ References domain-specific models or concepts
- ✅ Used primarily by one domain's ViewModels/Mediators
- ✅ Contains domain-specific validation or processing rules

**Keep in Shared Folder If:**
- ✅ Pure utility function with no domain knowledge
- ✅ Used by multiple domains equally
- ✅ Infrastructure concern (logging, persistence, UI coordination)
- ✅ Generic conversion or helper logic

**Examples:**
```csharp
// ✅ Domain-specific → TestCaseGeneration/Services/
public class RequirementAnalysisService 
{
    // Contains requirement-specific quality analysis logic
}

// ✅ Shared → Services/
public class NotificationService 
{
    // Generic notification infrastructure
}

// ✅ Domain-specific → TestCaseGeneration/Helpers/
public static class TableConversionHelper
{
    // Converts requirement tables to specific formats
}

// ✅ Shared → Helpers/
public static class FileNameHelper
{
    // Generic file name sanitization
}
```
---

## �🔧 Required Implementation Patterns

### **ViewModel Creation**
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

// ❌ WRONG: Direct instantiation without mediator
public class BadViewModel : ObservableObject
{
    // Missing mediator - violates architecture
}
```

### **Domain Communication**
```csharp
// ✅ CORRECT: Within domain using events
mediator.PublishEvent(new TestCaseGenerationEvents.RequirementSelected 
{ 
    Requirement = req, SelectedBy = "UserAction" 
});

// ✅ CORRECT: Cross-domain using coordinator
await mediator.RequestCrossDomainAction(new RequestTestFlowValidation 
{ 
    TestCases = generatedTestCases 
});

// ❌ WRONG: Direct cross-domain reference
// TestFlowService.DoSomething(); // Violates domain boundaries
```

### **UI Feedback**
```csharp
// ✅ CORRECT: Domain-aware UI coordination
mediator.ShowProgress("Importing requirements...", 45);
// Results in: "Test Case Generator: Importing requirements... 45%"

// ❌ WRONG: Direct global notifications without domain context
// NotificationService.ShowInfo("Importing..."); // Loses domain context
```

### **Service Access**
```csharp
// ✅ CORRECT: Dependency injection
public SomeService(ITextGenerationService llmService, ILogger<SomeService> logger)
{
    _llmService = llmService; // Injected, testable, configurable
}

// ❌ WRONG: Direct instantiation
// var llm = new OllamaTextGenerationService(); // Hard-coded, not configurable
```

---

## 🚨 Architectural Red Flags

**STOP and reconsider if you encounter:**

### **Pattern Violations**
- ViewModels created without mediator injection
- Direct instantiation of services instead of DI
- Business logic in Views or UI code
- Global state that should be domain-specific

### **Boundary Violations**  
- TestFlow domain directly referencing TestCaseGeneration logic
- Domain-specific code in shared services
- Shared models containing domain-specific business logic
- Cross-domain communication without explicit contracts
- **Domain-specific helpers/services/converters in shared folders**
- **Shared utilities in domain folders**

### **Communication Anti-Patterns**
- Direct ViewModel-to-ViewModel communication
- Events crossing domain boundaries without coordination
- UI feedback without domain context
- Bypassing mediator pattern for "convenience"

### **Maintainability Issues**
- Magic strings or hard-coded values
- Unclear ownership (which domain owns this?)
- Duplicate functionality across domains
- Missing fail-fast validation

---

## 📋 Implementation Phase Guidelines

### **Phase 5B: Core Architecture (Current)**
**Focus**: Establish foundational patterns and domain mediators
- Maintain strict adherence to patterns - exceptions create debt
- Every ViewModel must use mediator injection
- All UI feedback must go through domain coordination
- Cross-domain communication must be explicit and type-safe

### **Phase 6: Advanced Patterns (Future)**
**Focus**: Performance optimizations and advanced features
- Service lifetime clarification and optimization
- Event replay and debugging capabilities  
- Enhanced error recovery and fallback strategies
- Performance monitoring and optimization

---

## 🔄 Review Process

### **Before Major Changes**
1. **Read this document** - Refresh architectural principles and boundaries
2. **Apply decision checklist** - Verify alignment with core principles
3. **Consider domain impact** - Which domain(s) are affected?
4. **Plan communication** - How will domains coordinate?

### **After Implementation**
1. **Verify patterns** - Do new components follow required patterns?
2. **Test boundaries** - Are domain boundaries respected?
3. **Check fail-fast** - Are architectural violations caught early?
4. **Update guidelines** - Did we learn something that should be captured?

### **Red Flag Response**
If you encounter red flags:
1. **PAUSE** - Don't continue with the violation
2. **ASSESS** - Is this a legitimate architectural concern?
3. **CONSULT** - Review with team/architectural decision maker
4. **DECIDE** - Either fix the violation or update guidelines if needed

---

## 🎯 Success Metrics

**You'll know the architecture is working when:**
- New developers can quickly identify domain boundaries
- Architectural violations are caught at compile/startup time
- Cross-domain changes require explicit coordination (no accidental coupling)
- UI feedback clearly shows which domain is performing operations
- Adding new features follows established patterns naturally
- Code reviews focus on business logic, not architectural concerns

---

*Last Updated: December 18, 2025 - Phase 5A Complete, Phase 5B In Progress*