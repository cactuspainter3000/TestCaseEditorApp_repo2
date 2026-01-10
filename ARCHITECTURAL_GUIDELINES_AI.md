# AI Agent Architectural Guidelines 🤖
> **Optimized for AI pattern matching and immediate decision making**

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

### **Cross-Domain Decision Matrix**
| **Scenario** | **❌ NEVER DO** | **✅ ALWAYS DO** |
|--------------|------------------|------------------|
| Need workspace info in TestCaseGeneration | Direct WorkspaceManagement calls | Use own domain state + listen for broadcasts |
| Need to update UI after save | Cross-domain event subscriptions | Mediator sets own state → broadcasts locally |
| Show progress indicators | Cross-domain progress updates | Use `IDomainUICoordinator` with domain context |

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

---

## 🔍 DEBUGGING GUIDE

### **Save Icon Not Updating?**
1. ✅ Is ViewModel listening to `WorkflowStateChanged`?
2. ✅ Is mediator publishing event when `IsDirty` changes?
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

1. 🎯 **Which domain owns this?** (TestCaseGeneration/TestFlow/Shared)
2. 📡 **Event or direct call?** (Fire-and-forget vs need response)  
3. 🏠 **Where does code go?** (Domain folder vs shared)
4. 🔗 **How do ViewModels get data?** (Via domain mediator events)
5. ⚡ **Any cross-domain needs?** (Use coordinator/broadcast)

**If uncertain, ask:**
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