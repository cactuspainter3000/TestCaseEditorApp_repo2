# Configurable Workspace Implementation Plan 🏗️

> **Purpose**: Complete implementation roadmap for flexible, configuration-driven workspace architecture  
> **Status**: Implementation Guide - Ready for Development  
> **Related**: `ARCHITECTURAL_GUIDE_AI.md` for architectural patterns

---

## 🎯 Vision: Flexible Workspace Architecture

### **Problem Statement**
Current architecture has **hardcoded workspace sharing patterns**:
- NavigationWorkspace always shared across domains
- Other workspaces always domain-specific
- No flexibility for different application modes or UI paradigms

### **Solution: Configuration-Driven Workspace Management**
Enable **any sharing pattern** through external configuration:
- ✅ **Fixed sharing** (current NavigationWorkspace pattern)
- ✅ **No sharing** (current domain-specific pattern)
- ✅ **Conditional sharing** (some domains share, others don't)
- ✅ **Mode-dependent sharing** (different patterns per application mode)
- ✅ **Runtime configuration** (change without code changes)

---

## 🏗️ Implementation Strategy

### **Phase 1: Foundation (Backward Compatible)**

#### **1. Create Configuration Abstractions**
```csharp
// Add to Services/ folder
public interface IWorkspaceConfigurationService 
{
    ViewConfiguration GetViewConfiguration(string domain);
    bool ShouldShareWorkspace(WorkspaceArea area, string domain);
}

public class ViewConfiguration 
{
    public string TitleView { get; set; } = "{Domain}_TitleView";
    public string HeaderView { get; set; } = "{Domain}_HeaderView"; 
    public string MainView { get; set; } = "{Domain}_MainView";
    public string NavigationView { get; set; } = "SideMenuView";  // Default shared
    public string NotificationView { get; set; } = "{Domain}_NotificationView";
}

public enum WorkspaceArea 
{
    Title,
    Header, 
    Main,
    Navigation,
    Notification
}
```

#### **2. Extend Existing ViewAreaCoordinator**
```csharp
// Modify existing ViewAreaCoordinator.cs
public class ViewAreaCoordinator 
{
    private readonly IWorkspaceConfigurationService _configService;
    // ... existing fields
    
    // Add new method alongside existing SetAllWorkspaces
    public void SetDomainWorkspaces(string domain) 
    {
        var config = _configService.GetViewConfiguration(domain);
        
        // Use configuration to resolve view names
        var titleViewName = config.TitleView.Replace("{Domain}", domain);
        var headerViewName = config.HeaderView.Replace("{Domain}", domain);
        var mainViewName = config.MainView.Replace("{Domain}", domain);
        var navigationViewName = config.NavigationView.Replace("{Domain}", domain);
        var notificationViewName = config.NotificationView.Replace("{Domain}", domain);
        
        // Call existing workspace assignment logic
        SetWorkspace(WorkspaceArea.Title, ResolveViewModel(titleViewName));
        SetWorkspace(WorkspaceArea.Header, ResolveViewModel(headerViewName));
        SetWorkspace(WorkspaceArea.Main, ResolveViewModel(mainViewName));
        SetWorkspace(WorkspaceArea.Navigation, ResolveViewModel(navigationViewName));
        SetWorkspace(WorkspaceArea.Notification, ResolveViewModel(notificationViewName));
    }
}
```

### **Phase 2: Default Configuration Implementation**

#### **3. Simple Configuration Service**
```csharp
public class DefaultWorkspaceConfigurationService : IWorkspaceConfigurationService 
{
    private readonly Dictionary<string, ViewConfiguration> _configurations;
    
    public DefaultWorkspaceConfigurationService() 
    {
        _configurations = new Dictionary<string, ViewConfiguration>
        {
            // Default: Current behavior (NavigationWorkspace shared, others domain-specific)
            ["Default"] = new(),
            
            // Project domain: Inherit default behavior
            ["Project"] = new(),
            
            // TestCaseGeneration domain: Inherit default behavior  
            ["TestCaseGeneration"] = new(),
            
            // Future: Custom configurations
            ["TabletMode"] = new() 
            {
                TitleView = "TouchTitleView",           // Shared title for tablet
                NavigationView = "TabNavigationView",   // Tab-based navigation
                NotificationView = "ToastNotificationView", // Shared notifications
                HeaderView = "{Domain}_TouchHeaderView",    // Domain-specific but touch-optimized
                MainView = "{Domain}_TouchMainView"         // Domain-specific but touch-optimized
            },
            
            ["EmbeddedMode"] = new() 
            {
                TitleView = "HostAppTitleView",         // Host application provides title
                NavigationView = "MinimalNavView",      // Minimal navigation for embedding
                NotificationView = "HostAppNotificationView", // Host handles notifications
                HeaderView = "CompactHeaderView",       // Shared compact header
                MainView = "{Domain}_EmbeddedView"      // Compact domain-specific views
            }
        };
    }
    
    public ViewConfiguration GetViewConfiguration(string domain) 
    {
        return _configurations.GetValueOrDefault(domain) ?? _configurations["Default"];
    }
    
    public bool ShouldShareWorkspace(WorkspaceArea area, string domain) 
    {
        var config = GetViewConfiguration(domain);
        
        return area switch 
        {
            WorkspaceArea.Navigation => !config.NavigationView.Contains("{Domain}"),
            WorkspaceArea.Title => !config.TitleView.Contains("{Domain}"),
            WorkspaceArea.Header => !config.HeaderView.Contains("{Domain}"),
            WorkspaceArea.Main => !config.MainView.Contains("{Domain}"),
            WorkspaceArea.Notification => !config.NotificationView.Contains("{Domain}"),
            _ => false
        };
    }
}
```

### **Phase 3: Registration & Integration**

#### **4. DI Registration (App.xaml.cs)**
```csharp
// Add to existing service registrations
services.AddSingleton<IWorkspaceConfigurationService, DefaultWorkspaceConfigurationService>();

// Update existing ViewAreaCoordinator registration
services.AddSingleton<ViewAreaCoordinator>(provider => 
    new ViewAreaCoordinator(
        provider.GetRequiredService<IViewModelFactory>(),
        provider.GetRequiredService<IWorkspaceConfigurationService>(),  // Add this parameter
        provider.GetRequiredService<ILogger<ViewAreaCoordinator>>()
    ));
```

#### **5. Update Existing Navigation**
```csharp
// Modify existing SideMenuViewModel navigation methods
private void NavigateToProject()
{
    SelectedSection = "Project";
    _navigationMediator.NavigateToSection("Project");
    
    // Use new configuration-driven approach
    _viewAreaCoordinator.SetDomainWorkspaces("Project");
}

private void NavigateToTestCaseGenerator() 
{
    SelectedSection = "TestCaseGeneration";  
    _navigationMediator.NavigateToSection("TestCaseGeneration");
    
    // Use new configuration-driven approach
    _viewAreaCoordinator.SetDomainWorkspaces("TestCaseGeneration");
}

private void NavigateToDummy()
{
    SelectedSection = "Dummy";
    _navigationMediator.NavigateToSection("Dummy");
    
    // Use new configuration-driven approach
    _viewAreaCoordinator.SetDomainWorkspaces("Dummy");
}
```

### **Phase 4: Advanced Configuration (Future)**

#### **6. Mode-Based Configuration**
```csharp
public interface IWorkspaceConfigurationService 
{
    ViewConfiguration GetViewConfiguration(string domain, string mode = "Default");
    void SetApplicationMode(string mode);
    string CurrentMode { get; }
}

public class ModeAwareWorkspaceConfigurationService : IWorkspaceConfigurationService 
{
    private string _currentMode = "Default";
    private readonly Dictionary<string, Dictionary<string, ViewConfiguration>> _modeConfigurations;
    
    public string CurrentMode => _currentMode;
    
    public void SetApplicationMode(string mode) 
    {
        _currentMode = mode;
        // Optionally trigger workspace refresh for all domains
    }
    
    public ViewConfiguration GetViewConfiguration(string domain, string mode = null) 
    {
        mode ??= _currentMode;
        return _modeConfigurations[mode]?.GetValueOrDefault(domain) 
               ?? _modeConfigurations["Default"][domain] 
               ?? new ViewConfiguration();
    }
}
```

#### **7. External Configuration Support**
```csharp
// Future enhancement: Load from JSON/config file
public class JsonWorkspaceConfigurationService : IWorkspaceConfigurationService 
{
    private Dictionary<string, ViewConfiguration> _configurations;
    
    public JsonWorkspaceConfigurationService(string configPath) 
    {
        var json = File.ReadAllText(configPath);
        _configurations = JsonSerializer.Deserialize<Dictionary<string, ViewConfiguration>>(json);
    }
    
    public void ReloadConfiguration() 
    {
        // Supports runtime configuration changes
        // Could watch file system for changes and auto-reload
    }
}
```

---

## 🧹 Legacy Code Removal Strategy

### **Deprecation Timeline**

| **Phase** | **Duration** | **Action** | **Risk** |
|-----------|--------------|------------|----------|
| **Deprecation** | 2-4 weeks | Mark legacy methods as `[Obsolete]` | 🟢 Low |
| **Parallel** | 2-4 weeks | Feature flag both implementations | 🟡 Medium |
| **Migration** | 2-4 weeks | Migrate all domains to new system | 🟡 Medium |
| **Removal** | 1-2 weeks | Delete legacy code entirely | 🟢 Low |

### **Legacy Code to Remove**

#### **From Project Domain:**
- ❌ `MVVM/ViewModels/ProjectViewModel.cs` (562 lines - replace with domain VMs)
- ❌ `MVVM/ViewModels/ProjectManagementViewModel.cs` (183 lines - merge into mediator)
- ❌ `MVVM/Views/ProjectView.xaml` (monolithic view - replace with workspace views)
- ❌ `MVVM/Utils/ProjectWorkflowMediator.cs` (legacy utility pattern)

#### **From ViewAreaCoordinator:**
- ❌ Hardcoded workspace assignments
- ❌ Legacy `SetAllWorkspaces` method (after migration complete)
- ❌ Manual workspace configuration logic

### **Safe Removal Process**
```csharp
// Step 1: Deprecate with clear migration path
[Obsolete("Use SetDomainWorkspaces() instead. Will be removed in v2.0")]
public void SetAllWorkspaces(WorkspaceConfiguration config) 
{
    _logger.LogWarning("SetAllWorkspaces is deprecated. Use SetDomainWorkspaces instead.");
    // ... existing implementation
}

// Step 2: Feature flag implementation
private void NavigateToProject()
{
    if (_featureFlags.UseConfigurableWorkspaces) 
    {
        _viewAreaCoordinator.SetDomainWorkspaces("Project");  // New
    }
    else 
    {
        _viewAreaCoordinator.SetAllWorkspaces(projectConfig); // Legacy
    }
}

// Step 3: After validation period - remove entirely
public void SetDomainWorkspaces(string domain) 
{
    // This becomes the single implementation
}
```

---

## ✅ Implementation Checklist

### **Phase 1: Foundation**
- [ ] Create `IWorkspaceConfigurationService` interface
- [ ] Create `ViewConfiguration` class
- [ ] Create `DefaultWorkspaceConfigurationService` implementation
- [ ] Add `SetDomainWorkspaces()` to `ViewAreaCoordinator`
- [ ] Register new service in DI container

### **Phase 2: Integration**
- [ ] Update navigation methods to use new system
- [ ] Add feature flag for gradual rollout
- [ ] Test with existing domains (TestCaseGeneration, Dummy)
- [ ] Validate no regression in current functionality

### **Phase 3: Project Domain Migration**
- [ ] Create Project domain workspace ViewModels
- [ ] Create Project domain workspace Views  
- [ ] Implement Project domain mediator
- [ ] Update Project navigation to use new system
- [ ] Test complete Project domain functionality

### **Phase 4: Validation & Cleanup**
- [ ] Performance testing (startup time, navigation speed)
- [ ] Feature parity validation (all existing features work)
- [ ] User acceptance testing
- [ ] Mark legacy methods as deprecated
- [ ] Remove legacy code after validation period

### **Future Enhancements**
- [ ] Mode-based configuration (tablet, embedded, etc.)
- [ ] External JSON configuration support
- [ ] Runtime configuration reload
- [ ] Configuration validation and error handling

---

## 🎯 Success Criteria

### **Technical Goals**
✅ **Flexibility**: Support any workspace sharing pattern  
✅ **Maintainability**: Single source of truth for workspace configuration  
✅ **Performance**: No regression in navigation speed or startup time  
✅ **Reliability**: Fail-fast validation of configuration  
✅ **Testability**: Configuration can be mocked and tested independently  

### **Architectural Goals**
✅ **Backward Compatibility**: Existing functionality preserved during migration  
✅ **Clean Architecture**: Remove legacy anti-patterns and technical debt  
✅ **Domain Separation**: Proper domain boundaries with mediator pattern  
✅ **Future-Proof**: Ready for new application modes and UI paradigms  

### **Business Goals**
✅ **Reduced Development Time**: New domains follow established patterns  
✅ **Easier Maintenance**: Single configuration system to understand  
✅ **Enhanced Flexibility**: Support for multiple application contexts  
✅ **Improved Quality**: Consistent patterns across all domains  

---

## 🔗 Related Documentation

- **Primary Reference**: `ARCHITECTURAL_GUIDE_AI.md` - Core architectural patterns
- **Domain Example**: `MVVM/Domains/Dummy/README_AI_GUIDE_REFERENCE.md` - Perfect implementation reference
- **Project Context**: `.github/copilot-instructions.md` - Project overview and context

This implementation plan provides the complete roadmap for modernizing the workspace architecture while maintaining system stability and enabling future flexibility!