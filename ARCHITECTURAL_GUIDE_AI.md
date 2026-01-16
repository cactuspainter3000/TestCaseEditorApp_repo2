# Comprehensive AI Architectural Guide 🤖
> **Complete implementation patterns, dependency chains, and decision trees for AI agents**  
> **Purpose**: Single source of truth for architectural implementation patterns

---

## 🚨 CRITICAL DOMAIN VIEW RULE

**⚠️ FOR ANY DOMAIN VIEW CREATION**: 
- **NEVER CREATE CUSTOM VIEWS** - Always copy authentic views from TestCaseGeneration domain
- **Follow Domain View Creation Chain** (see section below) - Missing steps cause build failures
- **TestCaseGeneration is the reference implementation** - All domains copy from this source

---

## 🎯 FAIL-FAST ARCHITECTURE PRINCIPLES

| **Principle** | **Implementation** | **Enforcement** |
|---------------|--------------------|-----------------| 
| Constructor injection | All ViewModels require mediator injection | Compile-time failure without mediator |
| Startup validation | Configuration errors caught at startup | Runtime failures prevented |
| Type-safe communication | Domain events strongly typed | Wrong event types can't cross domains |
| Architectural violations | Caught at compile/startup time | Never at runtime |
| Dependency chains | Complete DI chain validation | Missing links cause startup failure |

---

## 🏢 DOMAIN IMPLEMENTATION STATUS

| **Domain** | **Header** | **Main** | **Navigation** | **Status** | **Source Pattern** |
|------------|------------|----------|----------------|------------|-------------------|
| TestCaseGeneration | ✅ | ✅ | ✅ | **Reference Implementation** | Original domain |
| Dummy | ✅ | ✅ | ✅ | **Complete** | Created as blueprint |
| Requirements | ✅ | ✅ | ✅ | **Complete** | Copied from TestCaseGeneration |
| WorkspaceManagement | ✅ | ✅ | ❌ | **Partial** | TBD |
| TestCaseCreation | ✅ | ❌ | ❌ | **Partial** | TBD |

**🎯 Pattern**: All new domains should copy authentic views from TestCaseGeneration, never fabricate custom views

---

## ⚠️ DOMAIN MIGRATION LESSONS LEARNED

### **Critical Failure Points in Requirements Domain Migration (Jan 2026)**

**❌ Failed Approach: Hybrid ViewModels**
- Mixed Requirements navigation + TestCaseGeneration headers
- Event domain mismatches (RequirementsEvents ≠ TestCaseGenerationEvents)
- Incomplete data initialization chains
- Complex cross-domain event coordination

**🔍 Root Causes Identified:**
1. **Event System Fragmentation**: Each domain has separate event namespaces, making cross-domain communication complex
2. **Incomplete ViewModel Copying**: Missed critical initialization logic from source ViewModels
3. **Data Flow Assumptions**: Assumed event subscriptions would be sufficient without verifying complete data chains
4. **Integration Testing Gaps**: No validation of actual requirement data display until runtime

---

## 📋 REQUIREMENTS DOMAIN MIGRATION - DETAILED EXECUTION PLAN

> **Current Status: Jan 16, 2026 - Planning Phase**  
> **Safe Revert Point**: Commit `379e43b` - "SAFE REVERT POINT: Third attempt at Requirements domain refactoring"

### **Phase 1: Deep Source Analysis** 

| Step | Task | Status | Notes |
|------|------|--------|--------|
| 1.1 | Analyze TestCaseGenerator_VM complete property list | ✅ **COMPLETE** | Found: 20+ properties including VisibleChips, Requirements, SelectedRequirement, IsMetaSelected, IsTablesSelected, IsParagraphsSelected, HasTables, HasParagraphs, BulkActionsVisible, and 13 ICommand properties |
| 1.2 | Map TestCaseGenerator_VM data initialization chains | ✅ **COMPLETE** | Key chain: Event → OnRequirementSelected() → _selectedRequirement = value → UpdateVisibleChipsFromRequirement() → VisibleChips populated with chips for all requirement fields |
| 1.3 | Document TestCaseGenerator_VM event subscription patterns | ✅ **COMPLETE** | 3 subscriptions: RequirementSelected, RequirementsCollectionChanged, WorkflowStateChanged with proper cleanup |
| 1.4 | Identify ALL UI binding requirements from views | ✅ **COMPLETE** | Key bindings: IsMetaSelected, IsTablesSelected, IsParagraphsSelected, IsAnalysisSelected, VisibleChipsWithValuesCount, BulkActionsVisible, SelectAllVisibleCommand, ClearAllVisibleCommand, AnalysisVM.*, HasMeta, HasTables, HasParagraphs, HasAnalysis |
| 1.5 | Map cross-domain event dependencies | ✅ **COMPLETE** | Critical cross-domain consumers: SideMenuViewModel, NavigationViewModel, TestCaseGeneratorNotificationViewModel - ALL depend on TestCaseGenerationEvents. Requirements domain MUST publish to both RequirementsEvents AND TestCaseGenerationEvents for compatibility |

### **Phase 2: Complete ViewModel Replication**

| Step | Task | Status | Notes |
|------|------|--------|--------|
| 2.1 | Copy TestCaseGenerator_VM → Requirements_MainViewModel (COMPLETE) | ✅ **COMPLETE** | Copied complete functionality: chip system, event handling, command structure, tab selections, content loading. Build succeeds with 0 errors. |
| 2.2 | Copy TestCaseGenerator_NavigationVM → Requirements_NavigationViewModel (COMPLETE) | 🔲 **Pending** | Include ALL navigation logic |
| 2.3 | Verify ALL XAML bindings have matching ViewModel properties | 🔲 **Pending** | Cross-reference step 1.4 findings |
| 2.4 | Implement complete data initialization chain | 🔲 **Pending** | Copy ALL initialization logic from source |
| 2.5 | Test Requirements ViewModels in isolation (unit tests) | 🔲 **Pending** | Verify data flow before UI integration |

### **Phase 3: All-at-Once Switch**

| Step | Task | Status | Notes |
|------|------|--------|--------|
| 3.1 | Update ViewConfigurationService (ALL Requirements ViewModels) | 🔲 **Pending** | NO hybrid approaches |
| 3.2 | Build and verify zero errors | 🔲 **Pending** | Must succeed before testing |
| 3.3 | Test complete Requirements section functionality | 🔲 **Pending** | Verify actual requirement data display |
| 3.4 | Verify navigation updates headers correctly | 🔲 **Pending** | Test Next/Previous buttons |
| 3.5 | Verify all tabs and chip displays work correctly | 🔲 **Pending** | Test Details, Tables, etc. |

### **Phase 4: Event System Validation**

| Step | Task | Status | Notes |
|------|------|--------|--------|
| 4.1 | Verify RequirementsEvents publish correctly | 🔲 **Pending** | Add debugging logs |
| 4.2 | Test cross-domain communication (if needed) | 🔲 **Pending** | May need dual event publishing |
| 4.3 | Verify all workspace switching works | 🔲 **Pending** | Test from other sections to Requirements |
| 4.4 | Full end-to-end testing | 🔲 **Pending** | Complete user workflow validation |

### **Success Criteria**
- ✅ Requirements section displays actual requirement data (not placeholders)
- ✅ Navigation buttons update main content correctly  
- ✅ All tabs (Details, Tables, Supplemental Info, LLM Analysis) function
- ✅ Header/title updates when navigating between requirements
- ✅ No functional regression from working baseline

### **Failure Protocol**
- 🚨 **Any step failure**: Document specific error and revert to commit `379e43b`
- 🚨 **Any functional regression**: Immediate revert and analysis
- 🚨 **Build failures**: Fix immediately before proceeding

---

## 🏗️ COMPLETE IMPLEMENTATION CHAINS

### **New ViewModel Implementation Chain**
```
📋 New ViewModel Request
│
├── 🎯 **Core ViewModel** (REQUIRED)
│   ├── Create: `/MVVM/Domains/{Domain}/ViewModels/{Domain}_{Purpose}VM.cs`
│   ├── Inherit: `BaseDomainViewModel`
│   ├── Constructor: `(I{Domain}Mediator mediator, ILogger<VM> logger)`
│   └── Register: App.xaml.cs `services.AddTransient<VM>()`
│
├── 🖥️ **View Creation** (REQUIRED FOR DOMAIN UI)
│   ├── **NEVER CREATE CUSTOM VIEWS** - Always copy from TestCaseGeneration
│   ├── Source: Find equivalent in `/MVVM/Domains/TestCaseGeneration/Views/`
│   ├── Copy: Both `.xaml` and `.xaml.cs` files to new domain
│   ├── Update: All namespace and class references to new domain
│   ├── Analyze: `grep` copied XAML for ALL property bindings
│   ├── Match: Ensure ViewModel has every property referenced in XAML
│   └── Validate: Build with zero errors before proceeding
│
├── 🔄 **Event Subscriptions** (IF NEEDED)
│   ├── Subscribe: In ViewModel constructor via mediator
│   ├── Handlers: Private methods for event handling
│   └── Cleanup: Unsubscribe in Dispose
│
└── 🎨 **Converters/Resources** (IF NEEDED)
    ├── Create: Converter classes implementing IValueConverter
    ├── Register: App.xaml `<conv:ConverterName x:Key="ConverterKey" />`
    └── Reference: View uses `{StaticResource ConverterKey}`
```

### **Domain View Creation Chain** ⭐ **CRITICAL PATTERN**
```
🏗️ New Domain Views Request
│
├── 🔍 **Source Discovery** (MANDATORY FIRST STEP)
│   ├── Identify: Equivalent views in TestCaseGeneration domain
│   ├── Pattern: `TestCaseGeneratorRequirements_View.xaml` → `{Domain}MainView.xaml`
│   ├── Pattern: `TestCaseGenerator_NavigationControl.xaml` → `{Domain}NavigationView.xaml`
│   └── **NEVER**: Create custom views from scratch
│
├── 📋 **File Copying** (EXACT DUPLICATION)
│   ├── Copy: Both `.xaml` and `.xaml.cs` files
│   ├── Rename: To match domain naming convention
│   ├── Update: All namespace declarations
│   ├── Update: All class names and references
│   └── Clean: Remove any domain-specific event handlers
│
├── 🔍 **Property Analysis** (PREVENT BUILD FAILURES)
│   ├── Command: `grep -r "Binding.*}" {copied}.xaml`
│   ├── Extract: ALL property names referenced in XAML
│   ├── List: Every binding, including UI-specific properties
│   └── Document: Required properties for ViewModel
│
├── 🎯 **ViewModel Creation** (COMPLETE PROPERTY MATCHING)
│   ├── Inherit: `BaseDomainViewModel`
│   ├── Add: ALL properties found in XAML analysis
│   ├── Include: UI-specific properties (RequirementsDropdown, etc.)
│   ├── Constructor: `(I{Domain}Mediator mediator, ILogger<VM> logger)`
│   └── Initialize: Any complex properties in constructor
│
├── 🔗 **Registration Chain** (COMPLETE 4-STEP PROCESS)
│   ├── DI: `App.xaml.cs` - `services.AddTransient<ViewModel>()`
│   ├── DataTemplate: `MainWindow.xaml` - ViewModel to View mapping
│   ├── ViewConfig: `ViewConfigurationService` - include in workspace method
│   └── Using: Add all required namespace references
│
└── ✅ **Validation** (ZERO-TOLERANCE)
    ├── Build: Must succeed with 0 errors
    ├── Properties: All XAML bindings have matching ViewModel properties
    ├── Navigation: Test workspace switching renders correctly
    └── Clean: No duplicate or backup files exist
```

### **Cross-Domain Communication Chain**
```
🌐 Cross-Domain Request
│
├── 🔍 **Audit Existing** (MANDATORY FIRST STEP)
│   ├── Check: `HandleBroadcastNotification` implementations
│   ├── Search: Existing event types and broadcasts
│   └── Validate: Not already handled
│
├── 📡 **Broadcasting Domain** (Source)
│   ├── Method: `mediator.BroadcastToAllDomains(new EventName { ... })`
│   ├── Event: Create event class with required data
│   └── Timing: Broadcast after state change complete
│
└── 👂 **Receiving Domain** (Target)
    ├── Handler: Add to `HandleBroadcastNotification` method
    ├── Processing: Update domain state based on event
    └── UI Updates: Trigger local events for ViewModels
```

### **Complete New Domain Chain**
```
🏢 New Domain Request
│
├── 📁 **Folder Structure**
│   ├── `/MVVM/Domains/{DomainName}/`
│   ├── `/Mediators/`, `/ViewModels/`, `/Views/`, `/Events/`, `/Services/`
│   └── Follow existing domain patterns
│
├── 🧠 **Mediator Setup**
│   ├── Interface: `I{Domain}Mediator`
│   ├── Implementation: `{Domain}Mediator : BaseDomainMediator<{Domain}Events>`
│   ├── Events: `{Domain}Events` static class with event classes
│   └── Register: App.xaml.cs DI registration
│
├── 🔗 **Integration Points**
│   ├── Domain Coordinator: Register in App.xaml.cs startup
│   ├── Side Menu: Add menu item with navigation command
│   ├── View Configuration: Add to ViewConfigurationService
│   └── Workspace Assignment: Handle in MainViewModel
│
└── 🧪 **Validation**
    ├── Build: Ensure clean compilation
    ├── Navigation: Test side menu → domain switch
    ├── Events: Test domain-specific events work
    └── Cross-Domain: Test broadcasts work
```

---

## 🔍 DEPENDENCY DISCOVERY MAP

### **Find Required Dependencies By Component Type**

| **Component** | **Search Pattern** | **Dependencies to Copy** |
|---------------|-------------------|-------------------------|
| **New ViewModel** | `grep -r "BaseDomainViewModel" --include="*.cs"` | Constructor pattern + mediator injection + logger |
| **New View** | `grep -r "DataTemplate" App.xaml` | ResourceDictionary registration + x:Key pattern |
| **New Converter** | `grep -r "IValueConverter" --include="*.cs"` | App.xaml registration + StaticResource usage |
| **New Domain Event** | `find . -name "*Events.cs" -path "*/Domains/*"` | Event class structure + property patterns |
| **Cross-Domain Communication** | `grep -r "HandleBroadcastNotification" --include="*.cs"` | Broadcast handling patterns + event types |
| **New Mediator** | `grep -r "BaseDomainMediator" --include="*.cs"` | Constructor dependencies + registration pattern |
| **Domain View Creation** | `find . -name "*_VM.cs" -path "*/TestCaseGeneration/*"` | Authentic view source + ViewModel properties + DataTemplate mapping |
| **Workspace Navigation** | `grep -r "NavigationView" --include="*.xaml"` | Navigation controls + dropdown properties + event handlers |

### **Critical Registration Points**

| **Registration Location** | **What Gets Registered** | **Validation Method** |
|--------------------------|-------------------------|----------------------|
| **App.xaml.cs DI** | ViewModels, Mediators, Services | Build fails if missing dependencies |
| **App.xaml Resources** | Converters, Global styles | Runtime fails if StaticResource missing |
| **App.xaml ResourceDictionary** | DataTemplates for Views | Views don't render if missing |
| **MainWindow.xaml DataTemplates** | ViewModel-to-View mapping | Workspace content fails to render if missing |
| **ViewConfigurationService** | Workspace ViewModel assignments | Navigation fails if ViewModels not included |
| **Domain Coordinator** | Domain mediators for cross-communication | Cross-domain events fail if not registered |

---

## 🔗 IMPLEMENTATION GUIDES

### **Configurable Workspace Architecture**
📋 **Implementation Guide**: [`CONFIGURABLE_WORKSPACE_IMPLEMENTATION_PLAN.md`](CONFIGURABLE_WORKSPACE_IMPLEMENTATION_PLAN.md)

**Complete roadmap for flexible, configuration-driven workspace management:**
- ✅ **Phase 1-4 Implementation**: Step-by-step migration strategy  
- ✅ **Legacy Removal Timeline**: Safe deprecation and cleanup process
- ✅ **Configuration Examples**: Default, tablet, embedded modes
- ✅ **Future-Proof Architecture**: Support for any workspace sharing pattern

**Use this for**: Project domain modernization, multi-mode applications, flexible UI architectures

---

## 🧭 EVENT TAXONOMY (Required)

| **Event Type** | **Purpose** | **Response** | **Example** |
|----------------|-------------|--------------|-------------|
| Notifications | Fire-and-forget facts | None expected | `RequirementImported`, `TestCasesGenerated` |
| Requests | Actions requiring result | Must include CorrelationId | `GenerateTestCasesRequest → Result` |
| Commands | Domain intent to act | UI-agnostic action | `StartImport`, `BeginValidation` |

**FORBIDDEN EVENT CONTENT** ❌:
- View/control names, XAML references
- UI element identifiers, selection indices  
- Navigation instructions ("open", "select", "focus")
- Scroll positions, coordinates, dialog instructions

**Litmus Test**: If message only makes sense knowing the XAML → NOT a domain event

---

## ⚠️ CRITICAL COMPLETION CHECKPOINTS

### **Before Committing Any Implementation**

#### ✅ **ViewModel Implementation Checklist**
- [ ] **Found working example** (`grep` for similar ViewModel in same domain)
- [ ] **Copied using statements** (EXACT imports from working example)
- [ ] **Verified event structures** (read event class definitions before writing handlers)
- [ ] **Checked working example's DI registration** (search App.xaml.cs pattern)
- [ ] ViewModel created in correct domain folder
- [ ] Inherits from `BaseDomainViewModel`
- [ ] Constructor takes `I{Domain}Mediator` and `ILogger<VM>`
- [ ] Registered in App.xaml.cs with `AddTransient<VM>()`
- [ ] DataTemplate created for ViewModel type
- [ ] DataTemplate registered in ResourceDictionary
- [ ] App.xaml includes ResourceDictionary (if new file)
- [ ] **No factory methods exist** (`grep -r "CreateYourVM\|new YourVM"` returns no results)
- [ ] **No direct instantiation** (all creation goes through DI container)
- [ ] Build succeeds without errors
- [ ] View renders when ViewModel is assigned

#### ✅ **Cross-Domain Communication Checklist**
- [ ] Searched for existing `HandleBroadcastNotification` implementations
- [ ] Verified functionality doesn't already exist
- [ ] Event class created with all required data
- [ ] Broadcasting domain calls `BroadcastToAllDomains(event)`
- [ ] Receiving domain handles in `HandleBroadcastNotification`
- [ ] Local domain events triggered for ViewModels
- [ ] End-to-end functionality tested
- [ ] No direct domain-to-domain dependencies created

#### ✅ **New Converter/Resource Checklist**
- [ ] Converter class implements IValueConverter properly
- [ ] Converter registered in App.xaml Application.Resources
- [ ] x:Key matches StaticResource references in views
- [ ] ConvertBack implemented if two-way binding needed
- [ ] Error handling for null/invalid values
- [ ] Design-time support added

---

## 🚨 IMMEDIATE PATTERN MATCHING

### **State Management Quick Lookup**
| **I need to...** | **Domain Owner** | **Implementation Pattern** | **Event Flow** |
|-------------------|------------------|----------------------------|----------------|
| Update dirty state | TestCaseGeneration | `mediator.IsDirty = value` | `WorkflowStateChanged` → ViewModels update |
| Show save button | UI reflects state | ViewModel binds to mediator state | No direct action needed |
| Save project | Any ViewModel can trigger | `SaveCommand` → `workspaceMediator.Save()` → `mediator.IsDirty = false` | Local domain update |
| Handle analysis results | TestCaseGeneration | `mediator.IsDirty = true` (data changed) | `WorkflowStateChanged` → UI updates |
| Navigation state | Domain-specific | `mediator.CurrentView = X` | Intra-domain event |

### **Workspace Coordination Quick Lookup**
| **I need to...** | **Coordinator** | **Implementation Pattern** | **Communication** |
|-------------------|------------------|----------------------------|-------------------|
| Switch domains (main menu) | ViewAreaCoordinator | `SetAllWorkspaces(config)` | Coordinated 5-workspace switch |

---

## 🚨 INCOMPLETE IMPLEMENTATION WARNING SIGNS

### **Red Flags That Indicate Missing Dependencies**

| **Symptom** | **Usually Missing** | **Find Complete Example** |
|-------------|--------------------|-----------------------|
| ViewModel assigned but view blank | DataTemplate registration | `grep -r "DataTemplate.*VM" App.xaml` |
| StaticResource not found error | Resource not in App.xaml | `grep -r "StaticResource.*ResourceName"` |
| Cross-domain events not firing | HandleBroadcastNotification missing | `grep -r "HandleBroadcastNotification" --include="*.cs"` |
| Converter not found | App.xaml converter registration | `grep -r "x:Key.*ConverterName" App.xaml` |
| Navigation doesn't work | ViewAreaCoordinator setup missing | Search existing navigation patterns |
| Build fails with DI errors | Service registration missing | Check App.xaml.cs service registration |

### **Completion Verification Commands**
```bash
# Verify ViewModel registration
grep -r "AddTransient.*YourViewModel" App.xaml.cs

# Verify DataTemplate exists  
grep -r "DataType.*YourViewModel" App.xaml

# Verify converter registration
grep -r "YourConverter.*x:Key" App.xaml  

# Verify event handling exists
grep -r "HandleBroadcastNotification" --include="*.cs" -A 10 -B 2

# Verify complete build
dotnet build --verbosity minimal
```

---

## 🗺️ DOMAIN INTERACTION MAP

```
┌─────────────────┐    ┌─────────────────┐    ┌─────────────────┐
│ TestCaseGeneration │    │ WorkspaceManagement │    │  TestCaseCreation  │
│                 │    │                 │    │                 │
│ • Requirements  │────│ • Project Ops   │────│ • Test Editing  │
│ • Assumptions   │    │ • File I/O      │    │ • Validation    │
│ • Questions     │    │ • Save/Load     │    │ • Export        │
│ • Generation    │    │                 │    │                 │
└─────────────────┘    └─────────────────┘    └─────────────────┘
         │                       │                       │
         └───────────────────────┼───────────────────────┘
                                 │
                    ┌─────────────────┐
                    │  TestFlow       │
                    │                 │
                    │ • Flow Diagrams │
                    │ • Validation    │
                    │ • Visualization │
                    └─────────────────┘
```

### **Cross-Domain Event Flows**
- **ProjectCreated/Opened/Closed**: WorkspaceManagement → All Domains
- **RequirementsImported**: WorkspaceManagement → TestCaseGeneration  
- **TestCasesGenerated**: TestCaseGeneration → TestCaseCreation
- **ValidationRequest**: Any Domain → TestFlow

---

## 💡 USAGE PATTERNS

### **For New Feature Implementation**
1. **Find Similar Component**: Use discovery map to find working example
2. **Trace Complete Chain**: Follow dependency graph for that component type  
3. **Copy All Dependencies**: Don't just copy the main file, copy ALL registrations
4. **Validate Each Step**: Check each registration point works
5. **Test End-to-End**: Ensure complete functionality before commit

### **For Debugging Issues**  
1. **Identify Component Type**: ViewModel, View, Event, etc.
2. **Check Completion Points**: Use checklists to find missing pieces
3. **Compare with Working Example**: Find similar working implementation
4. **Trace Registration Chain**: Verify each dependency link works
5. **Validate Fix**: Test complete functionality

---

## 🎯 DOMAIN OWNERSHIP TABLE

| **Concept** | **Owner Domain** | **How Others Access** |
|-------------|------------------|----------------------|
| Dirty State | TestCaseGeneration | Listen to broadcasts |
| Current Requirement | TestCaseGeneration | Request via coordinator |
| Save Operation | WorkspaceManagement | Any domain can trigger |
| Analysis State | TestCaseGeneration | Internal domain concern |
| Project Info | WorkspaceManagement | Broadcast on changes |
| UI Feedback | Domain that triggered action | Use `IDomainUICoordinator` |

---

## ⚡ COMMON SCENARIOS - EXACT IMPLEMENTATIONS

### **Scenario: After Analysis, Update Save Icon**
1. `RequirementAnalysisService` completes analysis
2. `TestCaseGenerationMediator.IsDirty = true`
3. Mediator publishes `WorkflowStateChanged`
4. `HeaderVM` receives event, updates `IsDirty` property
5. UI automatically updates via binding

### **Scenario: User Clicks Save Button**
1. HeaderVM `SaveCommand` executed
2. Command calls `workspaceMediator.SaveProjectAsync()`  
3. After success: `testCaseGenerationMediator.IsDirty = false`
4. Mediator publishes `WorkflowStateChanged`
5. HeaderVM updates, save icon changes

### **Scenario: Load New Project**
1. WorkspaceManagement loads project
2. Broadcasts `ProjectOpened` to all domains
3. TestCaseGeneration receives broadcast
4. Sets `IsDirty = false` (clean project)
5. All ViewModels update accordingly

### **Scenario: User Selects "Test Case Generator" from Side Menu**
1. Side menu calls `viewAreaCoordinator.SetAllWorkspaces("TestCaseGeneration")`
2. ViewAreaCoordinator calls `testCaseMediator.UpdateWorkspaces()`
3. TestCaseGeneration mediator creates/updates its 4 workspace ViewModels
4. MainViewModel.HeaderWorkspace = headerVM, MainWorkspace = analysisVM, TitleWorkspace = titleVM, etc.
5. UI automatically reflects new workspaces

### **Scenario: Cross-Workspace Update (Requirement Selected)**
1. User clicks requirement in NavigationWorkspace
2. NavigationVM calls `mediator.PublishEvent(RequirementSelected)`
3. Multiple ViewModels in SAME domain listen: HeaderVM, AnalysisVM
4. Each updates its own display based on selected requirement

---

## 🔍 IMPLEMENTATION DISCOVERY PATTERNS

### **MANDATORY First Step: Find Working Example**
```csharp
// Step 1: Find existing working ViewModel
// Search: grep -r "TestCaseGenerator.*VM" --include="*.cs"
// Found: TestCaseGenerator_HeaderVM.cs

// Step 2: Copy EXACT using statements first
using System;
using TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Mediators;
using TestCaseEditorApp.MVVM.Events; // <- CRITICAL: Copy all imports from working example

// Step 3: Copy EXACT constructor pattern 
public MyNew_ViewModel(ITestCaseGenerationMediator mediator, ILogger<MyNew_ViewModel> logger) 
    : base(mediator, logger)
{
    // Step 4: Copy EXACT event subscription pattern
    _mediator.Subscribe<TestCaseGenerationEvents.RequirementSelected>(OnRequirementSelected);
}

// Step 5: Check ACTUAL event structure before writing handlers
// Read the event class definition, don't assume properties exist
private void OnRequirementSelected(TestCaseGenerationEvents.RequirementSelected e)
{
    // Copy property access pattern from working example
}

// Step 6: Place methods inside main class, NOT in nested static classes
// Check file structure before adding methods
```

**🎯 Preventive Pattern**: Copy **ALL** aspects (imports, signatures, placement) from working example before modifying anything.

### **Complete "Questions First" Checklist**
```
🤔 Before writing any code:

1. FIND: Which existing ViewModel is most similar?
   └── Copy its using statements EXACTLY

2. EXAMINE: What events does the working example use?
   └── Read the actual event class definitions

3. CHECK: Where are the working example's methods placed?
   └── Verify main class vs nested class context

4. VERIFY: How does the factory call the working example?
   └── Copy parameter order and types EXACTLY

5. VALIDATE: What DI registrations does the working example have?
   └── grep App.xaml.cs for AddTransient pattern

6. CONFIRM: Does working example have factory methods?
   └── grep for Create methods - if none exist, don't add them
```

### **Implementation Decision Tree**
```
🤔 I need to implement [FEATURE]

├── Does existing HandleBroadcastNotification cover this?
│   └── ✅ Add logic there, don't create new subscriptions
│
├── Do existing events already fire for this scenario?
│   └── ✅ Subscribe to existing events, don't create new ones
│
├── Is there similar functionality elsewhere?
│   └── ✅ Follow same pattern, don't invent new architecture
│
└── Is this truly new functionality?
    └── ✅ Follow templates, but audit dependencies first
```

### **Anti-Pattern: Overcomplication Detection**
```
🚨 STOP if implementation involves:

├── IViewAreaCoordinator injection → Use existing broadcast mechanism
├── Factory constructor changes → Check if broadcasts already handle this
├── New cross-domain subscriptions → Use BroadcastToAllDomains instead
├── Complex dependency chains → Look for simpler existing patterns
└── "But the guidelines say..." → Guidelines show patterns, not requirements
```

**🎯 Golden Rule**: 
> If implementation feels complex, step back and audit what already exists.
> 90% of the time, existing patterns already handle the requirement.

---

## 🔧 XAML/WPF TROUBLESHOOTING PATTERNS

### **"View Not Showing" Debug Sequence**
```
🚪 ViewModel assigned but view blank?

├── 🔍 Check DataTemplate exists
│   └── grep -r "DataTemplate.*YourVM" App.xaml
│
├── 🔍 Check ResourceDictionary registered  
│   └── App.xaml: <ResourceDictionary Source="/Your/Path" />
│
├── 🔍 Check ViewModel inheritance
│   └── Must inherit from BaseDomainViewModel
│
└── 🔍 Check mediator injection
    └── Constructor: (I{Domain}Mediator mediator, ILogger<VM> logger)
```

### **"StaticResource Not Found" Debug Sequence**
```
🚪 {StaticResource ConverterName} fails?

├── 🔍 Check converter registration
│   └── App.xaml: <conv:ConverterName x:Key="ConverterKey" />
│
├── 🔍 Check namespace declaration
│   └── xmlns:conv="clr-namespace:YourApp.Converters"
│
└── 🔍 Check key name exact match
    └── StaticResource name must match x:Key exactly
```

### **Required XAML Patterns**
- Use StaticResource for ALL styling (never inline styles)
- Follow `{DomainName}_{Purpose}View.xaml` naming convention
- Include design-time DataContext for intellisense
- Register converters in App.xaml before use

---

## 🚨 MIGRATION LESSONS (Hard-Won Knowledge)

### **What Fails: Mixed Architecture Patterns**
| **❌ NEVER DO** | **Why It Fails** | **✅ DO INSTEAD** |
|------------------|-------------------|--------------------| 
| Keep legacy + new code | Confusion about correct pattern | Full architectural commitment |
| Gradual/systematic migration | Hybrid communication breaks | Complete pattern implementation |
| Side-by-side implementation | Maintaining two systems | Delete legacy completely |

### **Migration Success Pattern**
1. **Understand architecture fully** → Study domain mediator patterns
2. **Implement completely** → Don't preserve legacy  
3. **Follow DI chains end-to-end** → MainViewModel → ViewModelFactory → Domain ViewModels
4. **Test cross-domain early** → BroadcastToAllDomains, UI thread marshaling
5. **Delete legacy completely** → No mixed patterns

---

## � DOMAIN VIEW CREATION LESSONS (Requirements Implementation)

**❌ Critical Mistakes to Avoid:**

### **Fabricated vs Authentic Views**
- **Problem**: Creating custom views from scratch instead of copying existing working patterns
- **Symptom**: Views that look different or have missing functionality compared to source domain
- **Solution**: Always copy authentic views from TestCaseGeneration domain as source material
- **Pattern**: `TestCaseGeneratorRequirements_View.xaml` → `RequirementsMainView.xaml`

### **ViewModel Property Mismatches**
- **Problem**: Copied XAML expects properties that don't exist in new ViewModel
- **Symptom**: Build errors like 'RequirementsDropdown' does not contain definition
- **Solution**: Copy ALL properties referenced by XAML, including UI-specific ones like dropdown controls
- **Validation**: `grep` copied XAML for property bindings and ensure ViewModel has matching properties

### **Incomplete DI Registration Chain**
- **Problem**: Missing any link in the registration chain causes runtime failures
- **Required Chain**: 
  1. ViewModel DI registration in `App.xaml.cs`
  2. DataTemplate mapping in `MainWindow.xaml` 
  3. ViewConfigurationService parameter addition
  4. Using statements for all referenced types
- **Validation**: Build must succeed with zero errors before testing

### **Code-Behind Reference Stale Types**
- **Problem**: `.xaml.cs` files still reference old ViewModel types after copying
- **Symptom**: Build errors about missing type references
- **Solution**: Update ALL type references in code-behind to match new ViewModel names
- **Pattern**: `TestCaseGenerator_NavigationVM` → `Requirements_NavigationViewModel`

### **Duplicate File Conflicts**
- **Problem**: Multiple versions of same file causing build conflicts
- **Symptom**: CS0102 errors about duplicate definitions
- **Solution**: Clean up ALL duplicate/backup files before building
- **Prevention**: Use git commits instead of backup files

**✅ Proven Success Pattern:**
1. **Copy Authentic Views**: Use TestCaseGeneration as source, never fabricate
2. **Match ALL Properties**: Ensure ViewModel has every property referenced in XAML
3. **Complete Registration Chain**: DI → DataTemplate → ViewConfiguration → Using statements
4. **Update All References**: Code-behind, namespaces, class names
5. **Clean Build Validation**: Zero errors required before testing UI
6. **Single File Policy**: Delete duplicates immediately

**🎯 Key Insight**: Domain views are NOT custom implementations - they are architectural copies with updated references

---

## �🚀 QUICK START CHECKLIST

### **Before ANY Implementation**
- [ ] **Find Similar**: `grep` for similar existing functionality first
- [ ] **Trace Dependencies**: Follow complete implementation chain  
- [ ] **Check Broadcasts**: Does HandleBroadcastNotification already handle this?
- [ ] **Validate Complexity**: If complex, look for simpler existing patterns
- [ ] **FOR DOMAIN VIEWS**: Always find TestCaseGeneration equivalent first

### **For New ViewModel**
- [ ] Inherit from `BaseDomainViewModel`
- [ ] Constructor: `(I{Domain}Mediator mediator, ILogger<VM> logger)`
- [ ] Register in App.xaml.cs: `services.AddTransient<VM>()`
- [ ] Create DataTemplate with correct DataType
- [ ] Add ResourceDictionary to App.xaml if new file

### **For Domain View Creation (FOLLOW CHAIN ABOVE)**
- [ ] **Find Source**: Identify TestCaseGeneration equivalent view
- [ ] **Copy Files**: Both .xaml and .xaml.cs to new domain
- [ ] **Analyze XAML**: `grep` for ALL property bindings before creating ViewModel
- [ ] **Match Properties**: Ensure ViewModel has every property referenced in XAML
- [ ] **Complete Chain**: DI → DataTemplate → ViewConfig → Using statements
- [ ] **Validate Build**: Zero errors required before testing UI

### **For Cross-Domain Communication**
- [ ] Search for existing `HandleBroadcastNotification` patterns
- [ ] Use `BroadcastToAllDomains()` from sending domain
- [ ] Add handler in receiving domain's `HandleBroadcastNotification`
- [ ] Test end-to-end event flow

### **For UI Issues**
- [ ] Check DataTemplate registration first
- [ ] Verify StaticResource keys match registrations
- [ ] Ensure proper ViewModel inheritance
- [ ] Validate converter registration in App.xaml

### **For Domain View Creation (NEW)**
- [ ] **Copy Authentic Views**: Use TestCaseGeneration as source, never fabricate custom views
- [ ] **Identify ALL Properties**: `grep` XAML for all property bindings before creating ViewModel
- [ ] **Match Property Types**: Ensure ViewModel properties match exact types expected by XAML
- [ ] **Complete DI Chain**: ViewModel registration → DataTemplate → ViewConfiguration → Using statements
- [ ] **Update All References**: Code-behind, namespaces, class names in all copied files  
- [ ] **Clean Duplicates**: Remove any backup/duplicate files before building
- [ ] **Validate Build**: Achieve zero build errors before testing UI functionality
- [ ] **Test Navigation**: Verify workspace switching renders all three areas correctly

---

## 💡 LESSON LEARNED - Save Icon Case Study

**❌ What We Did Wrong Initially:**
- Modified multiple ViewModels to directly track save state
- Created complex cross-ViewModel dependencies
- Mixed UI logic with domain state management

**✅ Correct Pattern That Works:**
1. **Single Source of Truth**: TestCaseGenerationMediator owns IsDirty state
2. **Event-Driven Updates**: Mediator broadcasts WorkflowStateChanged
3. **Reactive ViewModels**: HeaderVM simply reflects mediator state
4. **Clear Ownership**: WorkspaceManagement handles save operations

**🎯 Key Insight**: State flows in one direction: Domain → Events → ViewModels → UI

---

## 🎨 UI/VIEW PATTERNS (Required)

### **Template: Clean Domain View**
```xaml
<UserControl x:Class="TestCaseEditorApp.MVVM.Domains.{DomainName}.Views.{DomainName}_{Purpose}View"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:vm="clr-namespace:TestCaseEditorApp.MVVM.Domains.{DomainName}.ViewModels"
             mc:Ignorable="d"
             d:DesignHeight="600" d:DesignWidth="800"
             d:DataContext="{d:DesignInstance Type=vm:{DomainName}_{Purpose}VM}"
             Background="{StaticResource Brush.Background.Menu}">
    
    <Grid Margin="20">
        <Border Background="{StaticResource MenuBackground}"
                BorderBrush="{StaticResource CardBorderBrush}"
                BorderThickness="1"
                CornerRadius="8"
                Padding="16">
            <ScrollViewer VerticalScrollBarVisibility="Auto">
                <ScrollViewer.Resources>
                    <Style TargetType="ScrollBar" BasedOn="{StaticResource CustomScrollBarStyle}"/>
                </ScrollViewer.Resources>
                
                <!-- Your content here -->
                
            </ScrollViewer>
        </Border>
    </Grid>
</UserControl>
```

### **Style Consistency Rules**
1. **NEVER create inline styles** - use StaticResource only
2. **Follow existing naming patterns** - check `/Resources/` and `/Styles/` folders
3. **Maintain visual consistency** - views should look cohesive across domains
4. **Use design-time DataContext** - helps with XAML intellisense
5. **Include proper namespaces** - domain-specific ViewModel namespaces

### **Common Style Resources**
```xaml
<!-- Backgrounds -->
{StaticResource Brush.Background.Menu}
{StaticResource MenuBackground}
{StaticResource CardBackground}

<!-- Borders & Brushes -->
{StaticResource CardBorderBrush}
{StaticResource MenuBorderBrush}

<!-- Text & Foreground -->
{StaticResource MenuForeground}
{StaticResource CardForeground}
{StaticResource Text.Body}

<!-- Control Styles -->
{StaticResource MenuPopupTextBoxStyle}
{StaticResource CustomScrollBarStyle}
{StaticResource Button.Primary}
{StaticResource Button.Secondary}
```

---

## ✅ FINAL VALIDATION COMMANDS

```bash
# Verify complete implementation
dotnet build --verbosity minimal

# Check all registrations
grep -r "AddTransient.*ViewModel" App.xaml.cs
grep -r "DataTemplate" App.xaml
grep -r "HandleBroadcastNotification" --include="*.cs"

# Validate architectural compliance  
grep -r "BaseDomainViewModel" --include="*.cs"
grep -r "new.*Service" --include="*.cs" | grep -v "Test"  # Should find minimal results
```

**🎯 This guide transforms architectural review from subjective assessment to systematic validation with concrete checklists and discovery commands!**

---

## 🎯 "QUESTIONS FIRST, CODE SECOND" METHODOLOGY 

### **Critical Development Sequence**

**❌ Traditional Approach**: Jump to implementation → Hit issues → Debug and rework → Learn lessons too late

**✅ "Post-Mortem Driven Development"**:
1. **🔍 Analyze Existing Code**: Find solutions to similar problems, identify working vs problematic patterns
2. **🎓 Lessons Learned Q&A**: What worked well? What caused problems? How can this break? 
3. **🛠️ Plan Implementation**: Extend successful patterns, avoid problematic ones, clear priorities
4. **💻 Code with Confidence**: Armed with insights, following proven patterns, avoiding known pitfalls

### **Pre-Implementation Q&A Framework**

**Always Use Q&A For**:
- ✅ Cross-domain features (save, export, validation)
- ✅ New architectural patterns (edit services, validation layers)  
- ✅ LLM integration features (external data sources)
- ✅ Data persistence changes (file formats, storage patterns)

**Optional For**:
- 🤔 Simple UI-only changes within one domain
- 🤔 Adding fields to existing forms (no business logic changes)
- 🤔 Styling adjustments that don't affect behavior

### **Key Investigation Questions**
- ❓ **Data Flow**: Where does data come from and where does it go?
- ❓ **Domain Coordination**: Which mediators need involvement?
- ❓ **Error Handling**: What happens when things go wrong?
- ❓ **User Experience**: How does this fit existing patterns?
- ❓ **Future Extensibility**: What might we need to add later?
- ❓ **Data Integrity**: How do we prevent corruption and loss?

**🎯 Key Insight**: Have your "lessons learned session" BEFORE you code, not after you ship!

---

## 🚀 SUCCESS INDICATORS & WARNING SIGNS

### **✅ You're on the right track when:**
- New code follows domain patterns naturally
- Cross-domain communication works immediately  
- Dependency injection chains are complete and acyclic
- No mixed old/new patterns exist
- Build succeeds and features work without "quirks"
- Application starts without "Unable to resolve service" errors

### **❌ Warning signs of trouble:**
- "Almost working" features that need constant tweaking
- Threading issues during cross-domain communication
- Confusion about which communication pattern to use
- Complex workarounds to make old patterns work with new ones
- **Circular dependency injection failures** - Services that need each other
- **"Unable to resolve service" runtime errors** - Broken DI registration chains

---

## 🏗️ DOMAIN ORGANIZATION & MIGRATION PATTERNS

### **Domain File Structure Standards**
```
MVVM/
├── Domains/
│   ├── {DomainName}/
│   │   ├── ViewModels/          [Domain ViewModels]
│   │   ├── Views/               [Domain-specific views]
│   │   ├── Mediators/           [Domain coordination]
│   │   ├── Services/            [Domain services]
│   │   └── Events/              [Domain events]
│   └── [Other domains]/
├── ViewModels/                  [Shared/general ViewModels]
├── Models/                      [Shared models]
└── Views/                       [General views]
```

### **Naming Conventions**
- **Domain ViewModels**: `{Domain}_{Component}VM.cs`
- **Domain Views**: `{Domain}_{Purpose}View.xaml`  
- **Namespaces**: `TestCaseEditorApp.MVVM.Domains.{Domain}.ViewModels`
- **Events**: `{Domain}Events.{EventName}`

### **ViewModel Migration Risk Mitigation**

#### **High Priority Risks & Solutions**
| **Risk** | **Mitigation Strategy** | **Validation** |
|----------|------------------------|----------------|
| **XAML Binding Failures** | Systematic namespace updates | Build + runtime testing |
| **DataTemplate Resolution** | Verify ResourceDictionary registration | Test view rendering |
| **Performance Regression** | Monitor after each migration | Performance testing |
| **Namespace Conflicts** | Update using statements methodically | Build validation |

#### **Migration Checklist**
- [ ] File moved to correct domain folder
- [ ] Namespace updated to domain pattern
- [ ] Using statements updated in dependent files
- [ ] DataTemplate registration updated
- [ ] Build succeeds without warnings
- [ ] Runtime testing confirms view renders
- [ ] No "Unable to resolve service" errors

---

## 🔄 CROSS-DOMAIN WORKFLOW PATTERNS

### **File Import Cross-Domain Flow**
```
📁 User Action (Any Domain)
│
├── 📡 **WorkspaceManagement Domain**
│   ├── Handle file dialog
│   ├── BroadcastToAllDomains(ImportRequirementsRequest)
│   └── Set IsAppendMode flag
│
└── 👂 **TestCaseGeneration Domain**
    ├── Receive via HandleBroadcastNotification
    ├── Process requirements with scrubber service
    ├── Append vs Replace logic based on mode
    └── Publish domain-specific events
```

### **Service Integration vs Disconnected Creation**

| **❌ Disconnected Anti-Pattern** | **✅ Integrated Pattern** |
|-----------------------------------|---------------------------|
| Create service but don't use in workflow | Integrate service into actual import flow |
| Build infrastructure without coordination | Connect via mediator communication |
| Multiple UI entry points | Single entry point with domain coordination |
| Service location pattern | Constructor injection with mediator flow |

### **Cross-Domain Communication Workflow Template**
```csharp
// ✅ CORRECT: Initiating Domain
public async Task TriggerCrossDomainWorkflow()
{
    // 1. Handle local domain concerns first
    var localResult = await ProcessLocalLogic();
    
    // 2. Broadcast to all domains with complete data
    BroadcastToAllDomains(new WorkflowRequest 
    { 
        Data = localResult, 
        Mode = DetectedMode,
        CorrelationId = Guid.NewGuid() 
    });
}

// ✅ CORRECT: Receiving Domain
public void HandleBroadcastNotification(object notification)
{
    switch (notification)
    {
        case WorkflowRequest request:
            // 3. Process in receiving domain context
            ProcessWorkflow(request.Data, request.Mode);
            // 4. Publish domain-specific completion events
            PublishEvent(new WorkflowCompleted { CorrelationId = request.CorrelationId });
            break;
    }
}
```

---

## 🎨 UI MODERNIZATION PATTERNS

### **Extract → Design → Create → Preserve Methodology**

#### **UI Refactoring Decision Tree**
```
🤔 Should I modernize this UI component?

├── Is it hardcoded and repetitive?
│   ├── ✅ Extract patterns → Design data models → Create templates
│   └── ❌ Keep existing if working well
│
├── Does it need to be reusable?
│   ├── ✅ Data-driven approach with templates
│   └── ❌ Targeted improvement only
│
└── Can I preserve exact behavior?
    ├── ✅ Proceed with modernization
    └── ❌ Fix specific issues only
```

#### **Data-Driven vs Hardcoded Transformation**

| **Before (Hardcoded)** | **After (Data-Driven)** |
|------------------------|-------------------------|
| Conditional XAML with Id-based visibility | Clean data models + templates |
| Duplicate UI patterns across components | Reusable templates with data binding |
| Hard to maintain/extend | Declarative configuration |
| Mixed presentation/logic | Separated concerns |

### **Template Design Principles**
- **Exact styling match**: No visual regression allowed
- **Behavioral preservation**: All interactions work identically
- **Reusability**: Templates work across different contexts
- **Clean data binding**: No complex conditional logic in templates

---

## 🤝 SERVICE COORDINATION PATTERNS

### **Smart Service Selection with Fallback**
```csharp
// ✅ CORRECT: Intelligent service coordination
public class SmartWorkflowService
{
    public async Task<WorkflowResult> ProcessAsync(WorkflowInput input)
    {
        // 1. Analyze input to determine optimal strategy
        var analysis = await _analyzer.AnalyzeAsync(input);
        
        // 2. Select primary service based on analysis
        var primaryService = _serviceSelector.GetOptimalService(analysis);
        
        // 3. Attempt primary processing
        var result = await primaryService.ProcessAsync(input);
        
        // 4. Fallback if primary fails
        if (!result.IsSuccess && _fallbackService != null)
        {
            result = await _fallbackService.ProcessAsync(input);
            result.Method = "Fallback";
        }
        
        return result;
    }
}
```

### **Error Message Transformation**

| **❌ Cryptic Messages** | **✅ Actionable Guidance** |
|-------------------------|-----------------------------|
| "0 requirements found" | "Found 15 requirement IDs in Word document. Use 'Import from Word' option." |
| "Import failed" | "Document format not recognized. Here's how to prepare your file..." |
| "Validation error" | "Missing requirement IDs. Expected format: PROJ-REQ_RC-001" |
| "Service unavailable" | "LLM service not responding. Try again or use offline mode." |

#### **Actionable Error Message Template**
```csharp
// ✅ CORRECT: Rich error information
public class ServiceResult<T>
{
    public bool IsSuccess { get; set; }
    public T Data { get; set; }
    public string UserFriendlyMessage { get; set; }  // What happened
    public string GuidanceMessage { get; set; }      // What to do about it
    public List<string> TroubleshootingSteps { get; set; }  // How to fix it
    public string TechnicalDetails { get; set; }     // For logging/support
}
```

---

## 🔥 ANTI-PATTERN DETECTION (Immediate Red Flags)

### **STOP ✋ If You See These Patterns:**

| **❌ Red Flag** | **🚨 Why Wrong** | **✅ Correct Pattern** |
|-----------------|------------------|------------------------|
| `ViewModel.Property = otherViewModel.Property` | Cross-ViewModel coupling | `mediator.State` → both ViewModels listen to events |
| `Subscribe<OtherDomainEvent>()` | Cross-domain subscription | Use `BroadcastToAllDomains()` + local subscription |
| `new SomeService()` in ViewModel | Missing dependency injection | Constructor injection only |
| `UpdateSaveStatus(mediator)` | ViewModel managing foreign state | ViewModel reflects own mediator state |
| UI properties in events | UI concerns in domain events | Domain data only in events |

### **🚨 CRITICAL: LLM Response Post-Processing Anti-Pattern**
**STOP ✋ Do NOT Post-Process LLM Responses - Fix the Prompt Instead**

| **❌ Wrong Approach** | **🚨 Why Wrong** | **✅ Correct Pattern** |
|----------------------|------------------|------------------------|
| Parser adds "smart fixes" to responses | App restructuring LLM data | LLM generates exact format needed |
| `ConvertFixToPastTense(fix)` in parser | Post-processing logic in parser | Prompt instructs LLM to use correct tense |
| `GenerateSmartFix()` when parsing | App inventing missing data | LLM provides complete response or uses `[brackets]` |
| Complex parsing with data manipulation | Parser doing data transformation | Simple extraction - parser just maps to objects |

**🎯 Core Principle**: 
> **LLM must generate responses in the exact format needed - parser only extracts/maps data.**  
> If parsing is complex or "fixing" responses → Fix the prompt, not the parser.  
> **"Garbage in, garbage out"** - Make input right, not output smart.

**✅ Correct Pattern:**
```csharp
// Simple extraction - LLM provides properly formatted responses
string fix = "";
if (fixPart.ToUpper().StartsWith("FIX:"))
{
    fix = fixPart.Substring(4).Trim(); // Just extract, no manipulation
}
```

### **STOP ✋ If Message Contains:**
- View names, control names, XAML references
- "Navigate to", "Select item", "Focus on"  
- UI coordinates, scroll positions, indices
- Dialog/window instructions

### **🚨 CRITICAL: Duplicate ViewModel Anti-Pattern**
**STOP ✋ Before Creating New ViewModels - Check for Existing Functionality**

| **❌ Common Mistake** | **🔍 How to Detect** | **✅ Correct Action** |
|----------------------|---------------------|---------------------|
| Creating `WorkspaceManagementViewModel` | Domain already has `WorkspaceProjectViewModel` | Use existing ViewModel - don't duplicate |
| DI registration fails with CS0246 | Type not found despite correct namespace | Check if ViewModel should exist at all |
| Multiple ViewModels per domain | `grep -r "ViewModel" MVVM/Domains/{Domain}/ViewModels/` | One focused ViewModel per domain concern |
| Disabled methods with TODO warnings | Methods contain `architectural violation removed` | Delete the entire ViewModel - it's a duplicate |
| Factory creates wrong ViewModel | Factory method exists but targets wrong class | Update factory to use correct existing ViewModel |

**🎯 Prevention Rule**: 
> **ALWAYS** audit existing ViewModels in target domain BEFORE creating new ones.  
> 95% of "new" ViewModels are duplicates of existing functionality.  
> Use `list_dir MVVM/Domains/{DomainName}/ViewModels/` first!

---

## 🎯 IMPLEMENTATION TEMPLATES

### **Template: ViewModel State Update**
```csharp
// ✅ CORRECT - ViewModel reflects mediator state
public class MyViewModel : BaseDomainViewModel
{
    private bool _isDirty;
    public bool IsDirty 
    { 
        get => _isDirty;
        set => SetProperty(ref _isDirty, value);
    }

    // Listen to mediator state changes
    private void OnWorkflowStateChanged(WorkflowStateChanged e)
    {
        if (e.PropertyName == nameof(IMediator.IsDirty))
        {
            IsDirty = (bool)e.NewValue;
        }
    }
}
```

### **Template: Mediator State Management**
```csharp
// ✅ CORRECT - Mediator owns state, broadcasts changes
public class TestCaseGenerationMediator : BaseDomainMediator<TestCaseGenerationEvents>
{
    private bool _isDirty;
    public bool IsDirty
    {
        get => _isDirty;
        set
        {
            if (_isDirty != value)
            {
                _isDirty = value;
                PublishEvent(new WorkflowStateChanged 
                { 
                    PropertyName = nameof(IsDirty), 
                    NewValue = value 
                });
            }
        }
    }
}
```

### **Template: Cross-Domain Communication**
```csharp
// ✅ CORRECT - Broadcast to all domains
mediator.BroadcastToAllDomains(new ProjectOpened 
{ 
    ProjectName = projectName, 
    FilePath = filePath 
});

// ✅ CORRECT - Handle in receiving domain
public void HandleBroadcastNotification(object notification)
{
    switch (notification)
    {
        case ProjectOpened e:
            UpdateProjectContext(e.ProjectName, e.FilePath);
            break;
    }
}
```
