# MIGRATION PLAN
> **Step-by-Step Plan to Update All Configurations to Standard Pattern**
> **Follow**: CORRECT_ARCHITECTURE_STANDARD.md
> **Reference**: CURRENT_IMPLEMENTATIONS_AUDIT.md

---

## 🎯 **MIGRATION STRATEGY**

### **Phase 1: Emergency Navigation Fixes** ⚡
> **Goal**: Get all menus working immediately with minimal changes
> **Timeline**: Immediate (1-2 hours)

### **Phase 2: Architectural Standardization** 🏗️  
> **Goal**: Convert all domains to single standard pattern
> **Timeline**: 1-2 days

### **Phase 3: Comprehensive Testing** ✅
> **Goal**: Validate all domains work identically  
> **Timeline**: Half day

---

## ⚡ **PHASE 1: EMERGENCY NAVIGATION FIXES**

### **Step 1.1: Fix Switch Pattern Mismatches**
> **Target**: Make all menus navigate immediately

**File**: `ViewConfigurationService.cs`

**Current Problem**:
```csharp
return sectionName?.ToLowerInvariant() switch
{
    "startup" => CreateStartupConfiguration(context),
    "dummy" => CreateDummyConfiguration(context),           // Menu calls "Dummy" 
    "testcasegenerator" => CreateTestCaseGeneratorConfiguration(context), // Menu calls "TestCaseGenerator"
    "newproject" => CreateNewProjectConfiguration(context),  // Menu calls "NewProject"
    "openproject" => CreateOpenProjectConfiguration(context), // Menu calls "OpenProject"
    // ...
};
```

**Fix**:
```csharp
return sectionName?.ToLowerInvariant() switch
{
    "startup" => CreateStartupConfiguration(context),
    
    // Fix case-sensitive mismatches  
    "dummy" => CreateDummyConfiguration(context),
    "testcasegenerator" => CreateTestCaseGeneratorConfiguration(context),
    "newproject" => CreateNewProjectConfiguration(context),
    "openproject" => CreateOpenProjectConfiguration(context),
    
    // Keep existing working patterns
    "requirements" => CreateRequirementsConfiguration(context),
    
    _ => throw new ArgumentException($"Unknown section: {sectionName}")
};
```

### **Step 1.2: Test Navigation**
```powershell
# Test each menu item
# 1. Launch app
# 2. Click "Test Case Generator" - should expand menu and show views
# 3. Click "Dummy" - should navigate  
# 4. Click "New Project" - should navigate
# 5. Click "Open Project" - should navigate
```

**Expected Result**: All menus expand and show their views (even if some still use UserControls)

---

## 🏗️ **PHASE 2: ARCHITECTURAL STANDARDIZATION**

### **Step 2.1: Fix TestCaseGenerator Domain**
> **Priority**: Highest - user's original request

#### **2.1.a: Verify Required Components Exist**

Check if these ViewModels exist:
- `TestCaseGenerator_TitleViewModel`
- `TestCaseGenerator_HeaderViewModel`  
- `TestCaseGeneratorMainViewModel` (already exists)
- `TestCaseGenerator_NavigationViewModel`
- `TestCaseGeneratorNotificationViewModel`

**If missing ViewModels exist**: Skip to 2.1.b  
**If missing ViewModels don't exist**: Create them first:

```csharp
// Example: TestCaseGenerator_TitleViewModel.cs
public class TestCaseGenerator_TitleViewModel : BaseDomainViewModel
{
    public TestCaseGenerator_TitleViewModel(ITestCaseGeneratorMediator mediator, ILogger<TestCaseGenerator_TitleViewModel> logger)
        : base(mediator, logger)
    {
        Title = "Test Case Generator";
    }
    
    public string Title { get; }
}
```

#### **2.1.b: Verify DataTemplates Exist**

Check `MainWindow.xaml` for:
```xml
<DataTemplate DataType="{x:Type tcg:TestCaseGenerator_TitleViewModel}">
    <tcgviews:TestCaseGenerator_TitleView />
</DataTemplate>
<!-- ... similar for Header, Main, Navigation, Notification -->
```

**If missing**: Add them based on existing View files

#### **2.1.c: Update DI Registration**

**File**: `App.xaml.cs`

Add missing registrations:
```csharp
services.AddTransient<TestCaseGenerator_TitleViewModel>();
services.AddTransient<TestCaseGenerator_HeaderViewModel>();
services.AddTransient<TestCaseGeneratorMainViewModel>();  // Verify exists
services.AddTransient<TestCaseGenerator_NavigationViewModel>();
services.AddTransient<TestCaseGeneratorNotificationViewModel>();
```

#### **2.1.d: Convert Configuration Method**

**File**: `ViewConfigurationService.cs`

**Replace**:
```csharp
private ViewConfiguration CreateTestCaseGeneratorConfiguration(object? context)
{
    // ❌ OLD: Manual UserControl creation
    var titleControl = new TestCaseGenerator_TitleView();
    // ...
    
    return new ViewConfiguration(/*UserControls as ViewModels*/);
}
```

**With**:
```csharp
private ViewConfiguration CreateTestCaseGeneratorConfiguration(object? context)
{
    // ✅ NEW: DI Resolution only
    var titleVM = App.ServiceProvider?.GetService<TestCaseGenerator_TitleViewModel>();
    var headerVM = App.ServiceProvider?.GetService<TestCaseGenerator_HeaderViewModel>();
    var mainVM = App.ServiceProvider?.GetService<TestCaseGeneratorMainViewModel>();
    var navigationVM = App.ServiceProvider?.GetService<TestCaseGenerator_NavigationViewModel>();
    var notificationVM = App.ServiceProvider?.GetService<TestCaseGeneratorNotificationViewModel>();
    
    // Validation
    if (titleVM == null) throw new InvalidOperationException("TestCaseGenerator_TitleViewModel not registered");
    if (headerVM == null) throw new InvalidOperationException("TestCaseGenerator_HeaderViewModel not registered");
    if (mainVM == null) throw new InvalidOperationException("TestCaseGeneratorMainViewModel not registered");
    if (navigationVM == null) throw new InvalidOperationException("TestCaseGenerator_NavigationViewModel not registered");
    if (notificationVM == null) throw new InvalidOperationException("TestCaseGeneratorNotificationViewModel not registered");
    
    return new ViewConfiguration(
        sectionName: "TestCaseGenerator",
        titleViewModel: titleVM,         // ✅ ViewModel - DataTemplate renders View
        headerViewModel: headerVM,       // ✅ ViewModel - DataTemplate renders View
        contentViewModel: mainVM,        // ✅ ViewModel - DataTemplate renders View
        navigationViewModel: navigationVM, // ✅ ViewModel - DataTemplate renders View
        notificationViewModel: notificationVM,
        context: context
    );
}
```

#### **2.1.e: Test TestCaseGenerator**
```powershell
# Build and test
dotnet build
# Launch app → Test Case Generator menu → Verify views show correctly
```

### **Step 2.2: Fix Requirements Domain**
> **Same process as TestCaseGenerator**

Follow identical steps 2.1.a through 2.1.e, but for Requirements domain:
- Check/create `Requirements*ViewModel` classes
- Check/create DataTemplates  
- Update DI registration
- Convert configuration method from UserControls to ViewModels

### **Step 2.3: Fix NewProject Domain** 
> **Replace PlaceholderViewModels with real ViewModels**

#### **2.3.a: Create Missing ViewModels**
```csharp
// NewProject_TitleViewModel.cs
public class NewProject_TitleViewModel : BaseDomainViewModel
{
    public NewProject_TitleViewModel(INewProjectMediator mediator, ILogger<NewProject_TitleViewModel> logger)
        : base(mediator, logger)
    {
        Title = "New Project";
    }
    
    public string Title { get; }
}

// NewProject_HeaderViewModel.cs 
public class NewProject_HeaderViewModel : BaseDomainViewModel
{
    public NewProject_HeaderViewModel(INewProjectMediator mediator, ILogger<NewProject_HeaderViewModel> logger)
        : base(mediator, logger)
    {
        HeaderText = "Project Setup";
    }
    
    public string HeaderText { get; }
}

// NewProject_NavigationViewModel.cs
public class NewProject_NavigationViewModel : BaseDomainViewModel
{
    // Navigation properties and commands
}
```

#### **2.3.b: Create Views**
Copy from Startup domain and customize:
- `NewProject_TitleView.xaml`
- `NewProject_HeaderView.xaml`  
- `NewProject_NavigationView.xaml`

#### **2.3.c: Add DataTemplates**
#### **2.3.d: Update DI Registration**  
#### **2.3.e: Update Configuration Method**

Replace PlaceholderViewModels with real ViewModels from DI

### **Step 2.4: Fix OpenProject Domain**
> **Same process as NewProject**

### **Step 2.5: Verify Dummy Domain Switch Pattern**
> **Already architecturally correct, just needs navigation fix**

Dummy domain already uses correct ViewModel + DataTemplate pattern. Only needs the switch pattern fix from Phase 1.

---

## ✅ **PHASE 3: COMPREHENSIVE TESTING**

### **Step 3.1: Navigation Testing**
```powershell
# Test script for all menu items
@"
Test Plan:
1. Launch app
2. Click each menu item:
   - Startup → Should show startup views
   - Test Case Generator → Should show test case generator views  
   - Requirements → Should show requirements views
   - Dummy → Should show dummy views
   - New Project → Should show new project views
   - Open Project → Should show open project views

Expected: All navigate successfully, all show domain-specific content
"@
```

### **Step 3.2: Architecture Validation**
```powershell
# Verify all configurations follow standard pattern
# Run: .\test-architecture-compliance.ps1 (if created)
# Or manually check each CreateXConfiguration method follows standard
```

### **Step 3.3: Build and Error Testing**
```powershell
# Clean build test
dotnet clean
dotnet build
# Should have zero errors, zero warnings

# Runtime error test  
# Launch app → Exercise all navigation → Check for exceptions
```

---

## 📋 **IMPLEMENTATION CHECKLIST**

### **Phase 1 Checklist** ⚡
- [ ] **Fix switch patterns**: Update `ViewConfigurationService.cs` switch statement
- [ ] **Test navigation**: All menu items expand and show views
- [ ] **Verify no regressions**: Startup and working domains still work

### **Phase 2 Checklist** 🏗️

#### **TestCaseGenerator Domain**
- [ ] **ViewModels exist**: All 5 ViewModels created/verified
- [ ] **DataTemplates exist**: All 5 DataTemplates added
- [ ] **DI registered**: All ViewModels in App.xaml.cs
- [ ] **Configuration updated**: Uses DI resolution, no UserControls
- [ ] **Navigation works**: Menu shows correct TestCaseGenerator views

#### **Requirements Domain**
- [ ] **ViewModels exist**: All 5 ViewModels created/verified
- [ ] **DataTemplates exist**: All 5 DataTemplates added  
- [ ] **DI registered**: All ViewModels in App.xaml.cs
- [ ] **Configuration updated**: Uses DI resolution, no UserControls
- [ ] **Navigation works**: Menu shows correct Requirements views

#### **NewProject Domain**
- [ ] **Replace PlaceholderViewModels**: Real ViewModels for Title/Header/Navigation
- [ ] **Views created**: Corresponding Views for new ViewModels
- [ ] **DataTemplates added**: DataTemplates for new ViewModels
- [ ] **DI registered**: New ViewModels in App.xaml.cs
- [ ] **Navigation works**: Menu shows correct NewProject views

#### **OpenProject Domain**  
- [ ] **Replace PlaceholderViewModels**: Real ViewModels for Title/Header/Navigation
- [ ] **Views created**: Corresponding Views for new ViewModels
- [ ] **DataTemplates added**: DataTemplates for new ViewModels
- [ ] **DI registered**: New ViewModels in App.xaml.cs  
- [ ] **Navigation works**: Menu shows correct OpenProject views

#### **Dummy Domain**
- [ ] **Navigation fixed**: Switch pattern corrected
- [ ] **Architecture verified**: Already follows standard pattern

### **Phase 3 Checklist** ✅  
- [ ] **All navigation works**: Every menu item navigates successfully
- [ ] **Build succeeds**: Zero errors, zero warnings
- [ ] **No runtime errors**: No exceptions during navigation
- [ ] **Consistent architecture**: All domains use identical pattern
- [ ] **Documentation updated**: All domains marked compliant in audit

---

## 🚨 **ROLLBACK PLAN**

If migration causes issues:

### **Emergency Rollback**
1. **Revert ViewConfigurationService.cs**: Restore original switch patterns
2. **Comment out new DI registrations**: Prevent missing dependency errors  
3. **Revert to UserControl patterns**: Temporarily restore working state
4. **Debug systematically**: Fix one domain at a time

### **Partial Success Handling**
- **If some domains work**: Keep successful conversions, rollback broken ones
- **If navigation breaks**: Revert Phase 1 switch pattern changes first
- **If DI errors occur**: Check registration order and dependency chains

---

## 🎯 **SUCCESS CRITERIA**

Migration is **complete** when:

1. ✅ **All navigation works**: Every menu item shows correct domain views
2. ✅ **Zero compilation errors**: Clean build with no warnings
3. ✅ **Architectural consistency**: All domains use ViewModels + DataTemplates 
4. ✅ **No PlaceholderViewModels**: All workspaces have real ViewModels
5. ✅ **Pattern uniformity**: All configurations identical in structure
6. ✅ **Documentation compliance**: Audit shows 6/6 domains fully compliant

**End State**: User clicks "Test Case Generator" → menu expands → shows correct test case generator views → same reliable pattern for all domains