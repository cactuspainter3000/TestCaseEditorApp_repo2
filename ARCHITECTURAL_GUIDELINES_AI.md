# AI Agent Architectural Guidelines 🤖
> **Optimized for AI pattern matching and immediate decision making**

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
| Update header content | Domain mediator | `HeaderWorkspace = headerVM` | Domain owns header ViewModel |
| Update main view | Domain mediator | `MainWorkspace = mainVM` | Domain owns main ViewModel |
| Update navigation | Domain mediator | `NavigationWorkspace = navVM` | Domain owns navigation ViewModel |
| Update title | Domain mediator | `TitleWorkspace = titleVM` | Domain owns title ViewModel |
| Handle side menu click | SideMenuWorkspace | Internal menu state | Triggers ViewAreaCoordinator |
| Share data between workspaces | ❌ NEVER DIRECT | Domain mediator coordination | NO workspace-to-workspace calls |

### **Cross-Domain Decision Matrix**
| **Scenario** | **❌ NEVER DO** | **✅ ALWAYS DO** |
|--------------|------------------|------------------|
| Need workspace info in TestCaseGeneration | Direct WorkspaceManagement calls | Use own domain state + listen for broadcasts |
| Need to update UI after save | Cross-domain event subscriptions | Mediator sets own state → broadcasts locally |
| Show progress indicators | Cross-domain progress updates | Use `IDomainUICoordinator` with domain context |
| Update multiple workspaces | Direct workspace assignments | ViewAreaCoordinator.SetAllWorkspaces() |
| Cross-workspace communication | Direct ViewModel-to-ViewModel calls | Domain mediator coordination |

### **CorrelationId Requirements**
| **Workflow Type** | **CorrelationId Required** | **Implementation** |
|-------------------|-----------------------------|--------------------| 
| Long-running operations | ✅ YES | Include in request/response |
| Multi-step workflows | ✅ YES | Echo in all related events |
| Cross-domain coordination | ✅ YES | For traceability |
| Simple notifications | ❌ NO | Fire-and-forget events |

---

## ⚡ INSTANT DECISION TREES

### **Where Does This Code Go?**
```
🤔 I need to implement [FEATURE]

├── Contains business logic for ONE domain?
│   └── ✅ `MVVM/Domains/{DomainName}/`
│
├── Used by multiple domains equally?
│   └── ✅ `Services/` or `MVVM/Utils/`
│
├── Domain-specific UI concern?
│   └── ✅ `MVVM/Domains/{DomainName}/ViewModels/`
│
└── Infrastructure/coordination?
    └── ✅ Root level folders
```

### **How Should ViewModels Communicate?**
```
🤔 ViewModel needs to [ACTION]

├── Within same domain?
│   └── ✅ `mediator.PublishEvent()` or direct mediator call
│
├── Across domains?
│   └── ✅ `mediator.BroadcastToAllDomains()` or coordinator
│
├── UI-only change?
│   └── ✅ Direct ViewModel property/command
│
└── Business state change?
    └── ✅ ALWAYS go through domain mediator
```

### **Event vs Direct Call?**
```
🤔 Should this be an event or direct call?

├── Fire-and-forget notification?
│   └── ✅ Event: `mediator.PublishEvent()`
│
├── Need result/response?
│   └── ✅ Direct call: `var result = mediator.DoSomething()`
│
├── Multiple listeners might care?
│   └── ✅ Event: `mediator.PublishEvent()`
│
└── Single action with immediate response?
    └── ✅ Direct call
```

### **Workspace Coordination Decisions**
```
🤔 Need to update what user sees?

├── Single workspace change?
│   ├── Header content? → Domain sets HeaderWorkspace ViewModel
│   ├── Main content? → Domain sets MainWorkspace ViewModel  
│   ├── Navigation? → Domain sets NavigationWorkspace ViewModel
│   ├── Title/project context? → Domain sets TitleWorkspace ViewModel
│   └── Menu state? → SideMenuWorkspace handles internally
│
├── Domain switch (user clicks main menu)?
│   └── ✅ ViewAreaCoordinator.SetAllWorkspaces() - coordinated switch
│
├── Cross-workspace data sharing?
│   └── ✅ Domain mediator coordination - NO direct workspace-to-workspace
│
└── Workspace state conflicts?
    └── ✅ Each workspace controlled ONLY by its assigned ViewModel
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

### **STOP ✋ If Message Contains:**
- View names, control names, XAML references
- "Navigate to", "Select item", "Focus on"  
- UI coordinates, scroll positions, indices
- Dialog/window instructions

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
public class MyMediator : BaseDomainMediator<MyEvents>
{
    private bool _isDirty;
    public bool IsDirty 
    { 
        get => _isDirty;
        set 
        {
            if (SetProperty(ref _isDirty, value))
            {
                PublishEvent(new WorkflowStateChanged { PropertyName = nameof(IsDirty), NewValue = value });
            }
        }
    }
}
```

### **Template: Save Command Implementation**
```csharp
// ✅ CORRECT - Command triggers save, updates own domain state
SaveCommand = new AsyncRelayCommand(async () => 
{
    try 
    {
        await workspaceMediator.SaveProjectAsync();
        // Update OWN domain state - mediator will broadcast
        mediator.IsDirty = false;
    }
    catch (Exception ex) 
    {
        // Handle error
    }
});
```

### **Template: Cross-Domain Broadcast**
```csharp
// ✅ CORRECT - For cross-domain notifications only
BroadcastToAllDomains(new ProjectSavedNotification 
{ 
    WorkspacePath = path,
    SavedAt = DateTime.Now 
});
```

### **Template: Domain Switching (Side Menu Selection)**
```csharp
// ✅ CORRECT - Coordinated workspace switch
public class SideMenuViewModel 
{
    public void SelectDomain(string domainName)
    {
        // Use ViewAreaCoordinator for ALL workspace switches
        await viewAreaCoordinator.SetAllWorkspaces(domainName);
        
        // ❌ NEVER do workspace-to-workspace direct calls
        // ❌ MainWorkspace = new SomeViewModel();  
        // ❌ HeaderWorkspace.UpdateFor(domain);
    }
}
```

### **Template: Workspace Update (Domain Receives Control)**
```csharp
// ✅ CORRECT - Domain mediator updates its workspaces
public class TestCaseGenerationMediator 
{
    public void UpdateWorkspaces()
    {
        // Update MY domain's workspaces only
        HeaderWorkspace = serviceProvider.GetService<TestCaseGenerator_HeaderVM>();
        MainWorkspace = serviceProvider.GetService<RequirementAnalysisViewModel>();
        NavigationWorkspace = serviceProvider.GetService<TestCaseNavViewModel>();
        TitleWorkspace = serviceProvider.GetService<TestCaseGenerator_TitleVM>();
        
        // Publish event so other domains know what happened
        PublishEvent(new WorkspaceActivated { Domain = "TestCaseGeneration" });
    }
}
```

### **Template: Side Menu Command Integration**
```csharp
// ✅ CORRECT - Data-driven menu with command integration
// 1. Create command in SideMenuViewModel
public ICommand MyNavigationCommand { get; private set; } = null!;

// 2. Initialize with mediator access
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

// 3. Assign to menu item after creation
var myDropdown = CreateDropdown("my-section", "📋", "My Section", "Description");
myDropdown.Command = MyNavigationCommand;
```

**CRITICAL**: Template must include command binding:
```xml
<ToggleButton Command="{Binding Command}"
              CommandParameter="{Binding CommandParameter}" />
```

### **Template: IDomainUICoordinator Usage**
```csharp
// ✅ CORRECT - Domain-aware UI coordination
mediator.ShowProgress("Importing requirements...", 45);
// Results in: "Test Case Generator: Importing requirements... 45%"

mediator.Notify("Analysis complete", NotificationSeverity.Success);
// Results in domain-contextualized notification

var confirmed = await mediator.RequestConfirmation("Delete requirement?");
// Returns semantic result, not UI-specific response
```

**❌ FORBIDDEN UI Coordinator calls**:
```csharp
// These violate domain boundaries
OpenImportDialog();
SwitchToTab("Assumptions");
SelectRow(5);
FocusSearchBox();
```
```

### **Template: Cross-Workspace Communication**
```csharp
// ✅ CORRECT - Via domain mediator events
public void OnRequirementSelected(RequirementSelectedEvent evt)
{
    // Update multiple workspaces in MY domain
    navigationWorkspace.HighlightRequirement(evt.Requirement);
    headerWorkspace.ShowRequirementTitle(evt.Requirement);
    mainWorkspace.DisplayRequirementDetails(evt.Requirement);
    
    // NO direct calls to other domain workspaces
}
```

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
5. NO cross-domain workspace calls

---

## 🔍 DEBUGGING GUIDE

### **Save Icon Not Updating?**
1. ✅ Is ViewModel listening to `WorkflowStateChanged`?
2. ✅ Is mediator publishing event when `IsDirty` changes?
3. ✅ Is binding path correct: `HeaderWorkspace.IsDirty`?
4. ✅ Check MainViewModel workspace assignment

### **Workspace Not Switching?**
1. ✅ Is side menu calling `viewAreaCoordinator.SetAllWorkspaces()`?
2. ✅ Does domain mediator implement `UpdateWorkspaces()` method?
3. ✅ Are workspace ViewModels registered in DI container?
4. ✅ Check MainViewModel workspace properties assignment

### **Cross-Domain Communication Failing?**
1. ✅ Using `BroadcastToAllDomains()` not direct calls?
2. ✅ Other domains subscribed to broadcast events?
3. ✅ Event payload contains all necessary data?
4. ✅ No direct domain-to-domain mediator calls?

### **Cross-Workspace Updates Not Working?**
1. ✅ Events published within SAME domain only?
2. ✅ Multiple ViewModels in domain subscribed to same event?
3. ✅ No direct workspace-to-workspace method calls?
4. ✅ ViewAreaCoordinator used for domain switching only?

---

## 🔧 XAML/WPF DEBUGGING PATTERNS

### **"View Not Showing Up" Decision Tree**
```
🤔 ViewModel assigned but view not rendering?

├── DataTemplate exists?
│   ├── Check ResourceDictionary registration in App.xaml
│   ├── Verify x:Key matches ViewModel type exactly
│   └── Ensure DataTemplate targets correct ViewModel
│
├── ViewModel inheritance correct?
│   ├── Inherits from BaseDomainViewModel?
│   ├── Constructor injection working?
│   └── IsRegistered property = true?
│
├── StaticResource references valid?
│   ├── Check /Resources/ and /Styles/ folders
│   ├── Verify ResourceDictionary merged in App.xaml
│   └── Validate converter registrations
│
└── Binding path issues?
    ├── Check DataContext assignment
    ├── Verify property names match exactly
    └── Test with design-time DataContext
```

### **"Converter Missing" Quick Fix**
```xaml
<!-- 1. Create converter class -->
public class ZeroToVisibilityConverter : IValueConverter { ... }

<!-- 2. Register in App.xaml ResourceDictionary -->
<Application.Resources>
    <conv:ZeroToVisibilityConverter x:Key="ZeroToVisibilityConverter" />
</Application.Resources>

<!-- 3. Use in XAML -->
<Border Visibility="{Binding Count, Converter={StaticResource ZeroToVisibilityConverter}}" />
```

### **Common XAML Issues & Solutions**
| **Problem** | **Cause** | **Solution** |
|-------------|-----------|---------------|
| View blank/empty | DataTemplate not found | Check ResourceDictionary registration |
| Converter not found | Missing App.xaml registration | Add to Application.Resources |
| Binding fails silently | Wrong property name | Enable binding debugging |
| StaticResource error | Missing resource key | Check /Resources/ folder imports |
| Design-time errors | Missing d:DataContext | Add DesignInstance reference |

---

## 🔍 IMPLEMENTATION DISCOVERY PATTERNS

### **"Find Working Example First" Protocol**
**MANDATORY: Before implementing ANY new component, find existing working example**

```
🎯 I need to implement [NEW COMPONENT]

1. 🔍 **Search for similar working implementations**
   grep -r "similar functionality" --include="*.cs" 
   Search: ViewModels, event patterns, service usage
   
2. 📋 **Audit complete implementation chain**
   ViewModel → DataTemplate → App.xaml registration → converter registration
   Event → subscription → handler → UI update
   
3. 📝 **Follow exact same pattern**
   Same file structure + same registrations + same wiring
   Deviate ONLY if requirements genuinely differ
   
4. ✅ **Validate end-to-end**
   Build → Run → Test actual functionality → Commit
```

### **Pre-Implementation Audit Checklist**
**Before implementing ANY new pattern:**

1. 🔍 **Search existing codebase first**
   ```
   grep -r "similar functionality" --include="*.cs"
   Search for: event names, method patterns, similar ViewModels
   ```

2. 📡 **Check existing broadcast mechanisms**
   ```
   Look for: HandleBroadcastNotification implementations
   Check: WorkspaceManagementEvents already handled
   Verify: Cross-domain events already exist
   ```

3. 🎯 **Validate event taxonomy**
   ```
   Existing events cover this? → Use existing
   New event needed? → Follow naming patterns
   Cross-domain required? → Check BroadcastToAllDomains usage
   ```

4. ⚡ **Simplicity validation**
   ```
   Requires new dependencies? → ⚠️  Check existing patterns first
   Needs factory changes? → 🚨 STOP - likely legacy approach
   Complex subscriptions? → 🚨 STOP - check broadcasts handle this
   ```

### **Implementation Examples Discovery Map**
| **I need to implement...** | **Find working example by searching...** | **Pay attention to...** |
|----------------------------|------------------------------------------|-------------------------|
| New ViewModel | `grep -r "BaseDomainViewModel" --include="*.cs"` | Constructor injection + domain folder + mediator usage |
| New View | `grep -r "DataTemplate" App.xaml` | ResourceDictionary registration + naming pattern |
| New Converter | `grep -r "IValueConverter" --include="*.cs"` | App.xaml registration + StaticResource usage |
| Cross-domain events | `grep -r "HandleBroadcastNotification" --include="*.cs"` | Existing broadcast handlers + event types |
| Domain communication | `grep -r "PublishEvent\|BroadcastToAllDomains" --include="*.cs"` | Event patterns + subscription methods |
| UI state updates | `grep -r "WorkflowStateChanged" --include="*.cs"` | Mediator state management + ViewModel reflection |

### **Example-Driven Implementation Template**
```csharp
// Step 1: Find existing working ViewModel
// Search: grep -r "TestCaseGenerator.*VM" --include="*.cs"
// Found: TestCaseGenerator_HeaderVM.cs

// Step 2: Follow exact same pattern
public class MyNew_ViewModel : BaseDomainViewModel 
{
    // Same constructor pattern as working example
    public MyNew_ViewModel(ITestCaseGenerationMediator mediator, ILogger<MyNew_ViewModel> logger) 
        : base(mediator, logger)
    {
        // Same initialization pattern as working example
    }
}

// Step 3: Follow same registration pattern
// Check: How is TestCaseGenerator_HeaderVM registered in App.xaml DataTemplates?
// Copy: Same ResourceDictionary entry with my ViewModel type
```

**🎯 Success Pattern**: If working example has A+B+C steps, new implementation needs A+B+C steps too.
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

### **Circular Dependency Resolution**
| **Problem Pattern** | **Solution Pattern** |
|---------------------|----------------------|
| Service A needs Service B ↔ Service B needs Service A | Break cycle at publishing boundary |
| Constructor injection circular reference | Use coordinator pattern for event publishing |  
| Both services need same dependency | Remove event publishing from constructors |

### **Document Parser Selection Pattern**
```csharp
// Use file extension, not filename patterns
var preferJamaParser = documentPath.EndsWith(".docx", StringComparison.OrdinalIgnoreCase);
if (preferJamaParser) {
    // Jama parser handles version history filtering
} else {
    // Generic parser for other formats  
}
```

### **Header Coordination Debug Pattern**
**Problem**: UI binding reads from one location, event system updates another
**Solution**: **Set Data → Trigger UI Update Event** (not reverse order)

```csharp
// ❌ Wrong: Event fires before data available
_navigationMediator.SetActiveHeader(header);  // Triggers UI update
HeaderArea.ShowTestCaseGeneratorHeader(header); // Sets data

// ✅ Correct: Data available before UI update  
HeaderArea.ShowTestCaseGeneratorHeader(header); // Sets data
_navigationMediator.SetActiveHeader(header);  // Triggers UI update
```

---
3. ✅ Is save command setting `mediator.IsDirty = false`?

### **Cross-Domain Communication Not Working?**
1. ✅ Use `BroadcastToAllDomains()` not `PublishEvent()`
2. ✅ Receiving domain listens to broadcast, not cross-domain Subscribe
3. ✅ Use coordinator for request/response patterns

### **ViewModel State Out of Sync?**
1. ✅ ViewModel should never manage its own business state
2. ✅ Always reflect mediator state via event subscription  
3. ✅ Mediator is single source of truth

---

## 🚀 QUICK START CHECKLIST

**Before implementing ANY feature:**

0. 🔍 **AUDIT EXISTING CODE FIRST** (Most Important)
1. 🎯 **Which domain owns this?** (TestCaseGeneration/TestFlow/Shared)
2. 📡 **Event or direct call?** (Fire-and-forget vs need response)  
3. 🏠 **Where does code go?** (Domain folder vs shared)
4. 🔗 **How do ViewModels get data?** (Via domain mediator events)
5. ⚡ **Any cross-domain needs?** (Use coordinator/broadcast)

**If uncertain, ask:**
> "Does existing code already handle this scenario?"
> 
> If yes: Follow existing pattern. If no: Ask next question.
> 
> "Does this ViewModel own this state, or just reflect it?"
> 
> Answer: ViewModels almost NEVER own state - they reflect mediator state.

---

## 💡 LESSON LEARNED - Save Icon Case Study

**❌ What I did wrong:**
- Tried to sync state across domains via cross-domain subscriptions
- Made HeaderVM manage its own state instead of reflecting mediator state
- Overcomplicated simple domain state management

**✅ Correct pattern that worked:**
- Save command sets `mediator.IsDirty = false`
- Mediator broadcasts `WorkflowStateChanged`  
- HeaderVM listens and updates UI
- Simple, clean, follows architecture

**🎯 Key insight:** 
> When confused about cross-domain communication, step back and ask: 
> "Which domain actually owns this state?" Usually the answer simplifies everything.

---

## 🎨 UI/VIEW PATTERNS (Required)

### **Global Style System**
**MANDATORY**: Use StaticResource references for ALL UI elements

```xaml
<!-- ✅ CORRECT - Use global styles -->
<UserControl Background="{StaticResource Brush.Background.Menu}">
    <Border Background="{StaticResource MenuBackground}"
            BorderBrush="{StaticResource CardBorderBrush}"
            BorderThickness="1"
            CornerRadius="8">
        <TextBox Style="{StaticResource MenuPopupTextBoxStyle}"/>
        <TextBlock Foreground="{StaticResource MenuForeground}"/>
    </Border>
</UserControl>

<!-- ❌ WRONG - Inline styles break consistency -->
<UserControl Background="#FF2D2D30">
    <TextBox FontSize="12" Background="White" BorderThickness="1"/>
</UserControl>
```

### **Required Style Categories**
| **Element Type** | **StaticResource Pattern** | **Examples** |
|------------------|----------------------------|--------------|
| Backgrounds | `Brush.Background.*` | `Brush.Background.Menu`, `MenuBackground` |
| Borders | `*BorderBrush` | `CardBorderBrush`, `MenuBorderBrush` |
| Text Colors | `*Foreground` | `MenuForeground`, `CardForeground` |
| Controls | `*Style` | `MenuPopupTextBoxStyle`, `CustomScrollBarStyle` |

### **View Naming Convention**
**Pattern**: `{DomainName}_{PurposeName}View.xaml`

```
✅ TestCaseCreation_MainView.xaml
✅ TestCaseCreation_EditorView.xaml
✅ TestCaseGenerator_HeaderView.xaml
❌ TestCaseView.xaml (too generic)
❌ TCCreationView.xaml (abbreviations)
```

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