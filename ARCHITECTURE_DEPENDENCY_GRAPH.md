# Architecture Dependency Graph 🔗

> **Purpose**: Visual map of complete implementation chains to prevent incomplete implementations  
> **Usage**: Before implementing any component, trace the complete dependency chain

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
├── 🖥️ **View Registration** (REQUIRED FOR UI)
│   ├── Create: `/MVVM/Domains/{Domain}/Views/{Domain}_{Purpose}View.xaml`
│   ├── DataTemplate: Add to App.xaml or ResourceDictionary
│   ├── Naming: `<DataTemplate DataType="{x:Type vm:{Domain}_{Purpose}VM}">`
│   └── Validate: App.xaml.Resources.MergedDictionaries includes view
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

### **Critical Registration Points**

| **Registration Location** | **What Gets Registered** | **Validation Method** |
|--------------------------|-------------------------|----------------------|
| **App.xaml.cs DI** | ViewModels, Mediators, Services | Build fails if missing dependencies |
| **App.xaml Resources** | Converters, Global styles | Runtime fails if StaticResource missing |
| **App.xaml ResourceDictionary** | DataTemplates for Views | Views don't render if missing |
| **Domain Coordinator** | Domain mediators for cross-communication | Cross-domain events fail if not registered |

---

## ⚠️ CRITICAL COMPLETION CHECKPOINTS

### **Before Committing Any Implementation**

#### ✅ **ViewModel Implementation Checklist**
- [ ] ViewModel created in correct domain folder
- [ ] Inherits from `BaseDomainViewModel`
- [ ] Constructor takes `I{Domain}Mediator` and `ILogger<VM>`
- [ ] Registered in App.xaml.cs with `AddTransient<VM>()`
- [ ] DataTemplate created for ViewModel type
- [ ] DataTemplate registered in ResourceDictionary
- [ ] App.xaml includes ResourceDictionary (if new file)
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

This dependency graph shows me the **complete implementation chains** I need to follow, which should prevent the incomplete implementation pattern I've been falling into!