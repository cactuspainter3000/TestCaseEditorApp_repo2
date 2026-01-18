# DEFINITIVE SIDE MENU MAPPING
> **Complete Technical Reference for All Menu Items and Their Correct Configurations**
> **Current Date**: January 18, 2026

---

## 📋 **COMPREHENSIVE MENU STRUCTURE**

### **Main Application Menu Section**
**Section ID**: `application-menu`  
**Section Text**: `"Application Menu"`  
**IsExpanded**: `true` (always visible)

### **Top-Level Parent Menu**
**Menu ID**: `test-case-generator`  
**Display Text**: `"Test Case Generator"`  
**Icon**: `🧪`  
**Type**: `MenuAction` with dropdown  
**IsExpanded**: `false` (starts collapsed)  
**Command**: `TestCaseGeneratorNavigationCommand`  
**Navigation Call**: `NavigateToSection("TestCaseGenerator")`

---

## 🗂️ **COMPLETE CHILD MENU HIERARCHY**

### **1. PROJECT MANAGEMENT SECTION**
**Parent ID**: `project`  
**Display Text**: `"Project"`  
**Icon**: `📁`  
**Type**: `MenuAction` with dropdown  
**Command**: `ProjectNavigationCommand`  
**Navigation Call**: `NavigateToSection("Project")`

#### **Project Sub-Items**:
- **New Project**
  - **ID**: `project.new`
  - **Text**: `"New Project"`  
  - **Icon**: `🗂️`
  - **Command**: `NewProjectCommand`
  - **Navigation Call**: `NavigateToSection("NewProject")`
  - **Configuration Method**: `CreateNewProjectConfiguration()`

- **Dummy Domain**  
  - **ID**: `project.dummy`
  - **Text**: `"Dummy Domain"`
  - **Icon**: `🔧`
  - **Command**: `DummyNavigationCommand`  
  - **Navigation Call**: `NavigateToSection("Dummy")`
  - **Configuration Method**: `CreateDummyConfiguration()`

- **Open Project**
  - **ID**: `project.open`
  - **Text**: `"Open Project"`
  - **Icon**: `📂`  
  - **Command**: `OpenProjectCommand`
  - **Navigation Call**: `NavigateToSection("OpenProject")`
  - **Configuration Method**: `CreateOpenProjectConfiguration()`

- **Save Project**
  - **ID**: `project.save`
  - **Text**: `"Save Project"`
  - **Icon**: `💾`
  - **Command**: `SaveProjectCommand`
  - **Navigation Call**: N/A (action only, no workspace switch)

- **Unload Project**
  - **ID**: `project.unload` 
  - **Text**: `"Unload Project"`
  - **Icon**: `📤`
  - **Command**: `UnloadProjectCommand`
  - **Navigation Call**: N/A (action only, no workspace switch)

---

### **2. REQUIREMENTS SECTION**
**Parent ID**: `requirements`  
**Display Text**: `"Requirements"`  
**Icon**: `📋`  
**Type**: `MenuAction` with dropdown  
**Command**: `RequirementsNavigationCommand`  
**Navigation Call**: `NavigateToSection("requirements")` *(Note: lowercase)*  
**Configuration Method**: `CreateRequirementsConfiguration()`

#### **Requirements Sub-Items**:
- **Import Additional Requirements**
  - **ID**: `requirements.import`
  - **Text**: `"Import Additional Requirements"`
  - **Icon**: `📥`
  - **Command**: `ImportAdditionalCommand`
  - **Navigation Call**: N/A (action only)
  - **Can Execute**: Only when `HasRequirements == true`

---

### **3. LLM LEARNING SECTION**
**Parent ID**: `llm-learning`  
**Display Text**: `"LLM Learning"`  
**Icon**: `🤖`  
**Type**: `MenuAction` with dropdown  
**Command**: `DummyNavigationCommand` *(Currently routes to Dummy)*  
**Navigation Call**: `NavigateToSection("llm learning")`  
**Configuration Method**: `CreateLLMLearningConfiguration()`

#### **LLM Learning Sub-Items**:
- **Generate Analysis Command**
  - **ID**: `llm.generate`
  - **Text**: `"Generate Analysis Command"`
  - **Icon**: `⚙️`
  - **Command**: `GenerateAnalysisCommandCommand`
  - **Navigation Call**: N/A (action only)

- **Export for ChatGPT**
  - **ID**: `llm.export`
  - **Text**: `"Export for ChatGPT"`
  - **Icon**: `💬`
  - **Command**: `ExportForChatGptCommand`  
  - **Navigation Call**: N/A (action only)

- **Toggle Auto Export**
  - **ID**: `llm.toggle`
  - **Text**: `"Toggle Auto Export"`
  - **Icon**: `🔄`
  - **Command**: `ToggleAutoExportCommand`
  - **Navigation Call**: N/A (action only)

---

### **4. TEST CASE CREATION SECTION**
**Parent ID**: `test-case-creation`  
**Display Text**: `"Test Case Creation"`  
**Icon**: `📝`  
**Type**: `MenuAction` with dropdown  
**Command**: `GenerateTestCaseCommandCommand`  
**Navigation Call**: `NavigateToSection("TestCaseCreation")`  
**Configuration Method**: `CreateTestCaseCreationConfiguration()`

#### **Test Case Creation Sub-Items**:
- **Test Case Creation**
  - **ID**: `testcase.create`
  - **Text**: `"Test Case Creation"`
  - **Icon**: `📄`
  - **Command**: `GenerateTestCaseCommandCommand`
  - **Navigation Call**: `NavigateToSection("TestCaseCreation")`

- **Generate Test Case Command**  
  - **ID**: `testcase.generate`
  - **Text**: `"Generate Test Case Command"`
  - **Icon**: `⚡`
  - **Command**: `GenerateTestCaseCommandCommand`
  - **Navigation Call**: N/A (action only)

- **Export to Jama...**
  - **ID**: `testcase.export`
  - **Text**: `"Export to Jama..."`
  - **Icon**: `🚀`
  - **Command**: `ExportAllToJamaCommand`
  - **Navigation Call**: N/A (action only)

---

## 🔧 **VIEWCONFIGURATION SERVICE SWITCH PATTERNS**

### **Current Switch Statement**:
```csharp
return sectionName?.ToLowerInvariant() switch
{
    "startup" => CreateStartupConfiguration(context),
    "project" => CreateProjectConfiguration(context),
    "requirements" => CreateRequirementsConfiguration(context),
    "testcasegenerator" or "test case generator" => CreateTestCaseGeneratorConfiguration(context),
    "testcasecreation" or "test case creation" => CreateTestCaseCreationConfiguration(context),
    "testflow" => CreateTestFlowConfiguration(context),
    "llm learning" => CreateLLMLearningConfiguration(context),
    "import" => CreateImportConfiguration(context),
    "newproject" or "new project" => CreateNewProjectConfiguration(context),
    "openproject" or "open project" => CreateOpenProjectConfiguration(context),
    "dummy" => CreateDummyConfiguration(context),
    _ => CreateDefaultConfiguration(context)
};
```

### **Navigation Call → Switch Pattern Mapping**:
| Menu Navigation Call | Switch Pattern Expected | Status |
|---------------------|--------------------------|--------|
| `NavigateToSection("startup")` | `"startup"` | ✅ **MATCH** |
| `NavigateToSection("Project")` | `"project"` (lowercase) | ❌ **CASE MISMATCH** |
| `NavigateToSection("requirements")` | `"requirements"` | ✅ **MATCH** |
| `NavigateToSection("TestCaseGenerator")` | `"testcasegenerator"` (lowercase) | ❌ **CASE MISMATCH** |
| `NavigateToSection("TestCaseCreation")` | `"testcasecreation"` (lowercase) | ❌ **CASE MISMATCH** |
| `NavigateToSection("llm learning")` | `"llm learning"` | ✅ **MATCH** |
| `NavigateToSection("NewProject")` | `"newproject"` (lowercase) | ❌ **CASE MISMATCH** |
| `NavigateToSection("OpenProject")` | `"openproject"` (lowercase) | ❌ **CASE MISMATCH** |
| `NavigateToSection("Dummy")` | `"dummy"` (lowercase) | ❌ **CASE MISMATCH** |

---

## 📊 **DETAILED CONFIGURATION METHOD ANALYSIS**

### **✅ ARCHITECTURALLY COMPLIANT METHODS**

#### **1. CreateStartupConfiguration()**
```csharp
private ViewConfiguration CreateStartupConfiguration(object? context)
{
    // ✅ CORRECT: DI Resolution Only
    var titleVM = App.ServiceProvider?.GetService<StartUp_TitleViewModel>();
    var headerVM = App.ServiceProvider?.GetService<StartUp_HeaderViewModel>();
    var mainVM = App.ServiceProvider?.GetService<StartUp_MainViewModel>();
    var navVM = App.ServiceProvider?.GetService<StartUp_NavigationViewModel>();
    var notificationVM = App.ServiceProvider?.GetService<StartUp_NotificationViewModel>();
    
    // ✅ CORRECT: Fail-fast validation
    if (titleVM == null) throw new InvalidOperationException("StartUp_TitleViewModel not resolved");
    // ... validation for all ViewModels
    
    // ✅ CORRECT: Return ViewModels directly (DataTemplates handle rendering)
    return new ViewConfiguration(
        sectionName: "Startup",
        titleViewModel: titleVM,         // ViewModel → DataTemplate → View
        headerViewModel: headerVM,       // ViewModel → DataTemplate → View  
        contentViewModel: mainVM,        // ViewModel → DataTemplate → View
        navigationViewModel: navVM,      // ViewModel → DataTemplate → View
        notificationViewModel: notificationVM,
        context: context
    );
}
```
**Status**: ✅ **PERFECT IMPLEMENTATION**  
**Pattern**: ViewModels + DataTemplates  
**Navigation**: Works correctly via `NavigateToSection("startup")`

#### **2. CreateDummyConfiguration()**
```csharp
private ViewConfiguration CreateDummyConfiguration(object? context)
{
    // ✅ CORRECT: Identical pattern to Startup
    var dummyVM = App.ServiceProvider?.GetService<DummyViewModel>();
    var titleVM = App.ServiceProvider?.GetService<DummyTitleViewModel>();
    var headerVM = App.ServiceProvider?.GetService<DummyHeaderViewModel>();
    var navVM = App.ServiceProvider?.GetService<DummyNavigationViewModel>();
    var notificationVM = App.ServiceProvider?.GetService<DummyNotificationViewModel>();
    
    // ✅ CORRECT: Complete validation chain
    // ✅ CORRECT: ViewModels returned directly
    return new ViewConfiguration(/* ViewModels only */);
}
```
**Status**: ✅ **PERFECT IMPLEMENTATION**  
**Pattern**: ViewModels + DataTemplates  
**Navigation**: Broken due to case mismatch (`"Dummy"` → `"dummy"`)

---

### **❌ ARCHITECTURALLY NON-COMPLIANT METHODS**

#### **3. CreateTestCaseGeneratorConfiguration()**
```csharp
private ViewConfiguration CreateTestCaseGeneratorConfiguration(object? context)
{
    // ❌ WRONG: Manual UserControl creation
    var titleControl = new TestCaseGenerator_TitleView();
    var headerControl = new TestCaseGenerator_HeaderView();  
    var mainControl = new TestCaseGeneratorMainView();
    var navigationControl = new TestCaseGenerator_NavigationControl();
    
    // ❌ WRONG: Manual DataContext assignment
    titleControl.DataContext = titleVM;
    headerControl.DataContext = headerVM;
    mainControl.DataContext = mainVM;
    navigationControl.DataContext = navigationVM;
    
    // ❌ WRONG: Passing UserControls where ViewModels expected
    return new ViewConfiguration(
        sectionName: "TestCaseGenerator",
        titleViewModel: titleControl,        // UserControl, not ViewModel!
        headerViewModel: headerControl,      // UserControl, not ViewModel!
        contentViewModel: mainControl,       // UserControl, not ViewModel!
        navigationViewModel: navigationControl,
        notificationViewModel: notificationControl,
        context: context
    );
}
```
**Status**: ❌ **ANTI-PATTERN**  
**Pattern**: UserControls + Manual DataContext  
**Navigation**: Broken due to case mismatch (`"TestCaseGenerator"` → `"testcasegenerator"`)  
**Major Issues**:
- Manual UserControl instantiation
- Manual DataContext assignment  
- Type confusion (UserControls passed as ViewModels)
- No DI validation
- Inconsistent with other domains

#### **4. CreateRequirementsConfiguration()**
**Status**: ❌ **ANTI-PATTERN** (Identical issues to TestCaseGenerator)  
**Pattern**: UserControls + Manual DataContext  
**Navigation**: Works correctly via `NavigateToSection("requirements")`

---

### **⚠️ PARTIALLY COMPLIANT METHODS**

#### **5. CreateNewProjectConfiguration()**
```csharp
private ViewConfiguration CreateNewProjectConfiguration(object? context)
{
    // ✅ CORRECT: DI resolution for main content
    var newProjectVM = App.ServiceProvider?.GetService<NewProjectViewModel>();
    
    // ❌ WRONG: PlaceholderViewModels instead of real ViewModels
    return new ViewConfiguration(
        sectionName: "NewProject",
        titleViewModel: new PlaceholderViewModel("New Project"),      // Placeholder!
        headerViewModel: new PlaceholderViewModel("Project Setup"),   // Placeholder!
        contentViewModel: newProjectVM,                               // ✅ Real ViewModel
        navigationViewModel: new PlaceholderViewModel("Navigation"),  // Placeholder!
        notificationViewModel: notificationVM,
        context: context
    );
}
```
**Status**: ⚠️ **PARTIAL COMPLIANCE**  
**Pattern**: Mixed (Real ViewModels + PlaceholderViewModels)  
**Navigation**: Broken due to case mismatch (`"NewProject"` → `"newproject"`)  
**Issues**: PlaceholderViewModels for title/header/navigation

#### **6. CreateOpenProjectConfiguration()**
**Status**: ⚠️ **PARTIAL COMPLIANCE** (Identical pattern to NewProject)  
**Navigation**: Broken due to case mismatch (`"OpenProject"` → `"openproject"`)

#### **7. CreateProjectConfiguration()**
**Status**: ⚠️ **PARTIAL COMPLIANCE**  
**Navigation**: Broken due to case mismatch (`"Project"` → `"project"`)

---

## 🎯 **CORRECT IMPLEMENTATION STANDARD**

### **Required ViewModels for Each Domain** (AI Guide Standard):
```csharp
// Example for any Domain
{Domain}_TitleViewModel       // Title workspace
{Domain}_HeaderViewModel      // Header workspace  
{Domain}_MainViewModel        // Main content workspace
{Domain}_NavigationViewModel  // Navigation workspace
{Domain}_NotificationViewModel // Notifications (optional)
```

### **Required DataTemplates** (MainWindow.xaml):
```xml
<!-- Each ViewModel needs corresponding DataTemplate -->
<DataTemplate DataType="{x:Type domain:{Domain}_TitleViewModel}">
    <domainviews:{Domain}_TitleView />
</DataTemplate>

<DataTemplate DataType="{x:Type domain:{Domain}_HeaderViewModel}">
    <domainviews:{Domain}_HeaderView />  
</DataTemplate>

<DataTemplate DataType="{x:Type domain:{Domain}_MainViewModel}">
    <domainviews:{Domain}_MainView />
</DataTemplate>

<DataTemplate DataType="{x:Type domain:{Domain}_NavigationViewModel}">
    <domainviews:{Domain}_NavigationView />
</DataTemplate>

<DataTemplate DataType="{x:Type domain:{Domain}_NotificationViewModel}">
    <domainviews:{Domain}_NotificationView />
</DataTemplate>
```

### **Required DI Registration** (App.xaml.cs):
```csharp
// All ViewModels must be registered
services.AddTransient<{Domain}_TitleViewModel>();
services.AddTransient<{Domain}_HeaderViewModel>();
services.AddTransient<{Domain}_MainViewModel>();
services.AddTransient<{Domain}_NavigationViewModel>();
services.AddTransient<{Domain}_NotificationViewModel>();
```

### **Standard Configuration Method Pattern**:
```csharp
private ViewConfiguration Create{Domain}Configuration(object? context)
{
    // 1. RESOLVE ALL VIEWMODELS FROM DI
    var titleVM = App.ServiceProvider?.GetService<{Domain}_TitleViewModel>();
    var headerVM = App.ServiceProvider?.GetService<{Domain}_HeaderViewModel>();
    var mainVM = App.ServiceProvider?.GetService<{Domain}_MainViewModel>();
    var navigationVM = App.ServiceProvider?.GetService<{Domain}_NavigationViewModel>();
    var notificationVM = App.ServiceProvider?.GetService<{Domain}_NotificationViewModel>();
    
    // 2. VALIDATE ALL RESOLVED SUCCESSFULLY (FAIL-FAST)
    if (titleVM == null) throw new InvalidOperationException("{Domain}_TitleViewModel not registered");
    if (headerVM == null) throw new InvalidOperationException("{Domain}_HeaderViewModel not registered");
    if (mainVM == null) throw new InvalidOperationException("{Domain}_MainViewModel not registered");
    if (navigationVM == null) throw new InvalidOperationException("{Domain}_NavigationViewModel not registered");
    if (notificationVM == null) throw new InvalidOperationException("{Domain}_NotificationViewModel not registered");
    
    // 3. RETURN VIEWMODELS DIRECTLY (DATATEMPLATES HANDLE VIEWS)
    return new ViewConfiguration(
        sectionName: "{Domain}",
        titleViewModel: titleVM,         // ViewModel → DataTemplate automatically renders View
        headerViewModel: headerVM,       // ViewModel → DataTemplate automatically renders View
        contentViewModel: mainVM,        // ViewModel → DataTemplate automatically renders View
        navigationViewModel: navigationVM, // ViewModel → DataTemplate automatically renders View
        notificationViewModel: notificationVM,
        context: context
    );
}
```

---

## 📈 **COMPLIANCE STATUS SUMMARY**

| Domain | Configuration Method | Architecture Pattern | Navigation Status | Overall Status |
|--------|---------------------|----------------------|-------------------|----------------|
| **Startup** | `CreateStartupConfiguration` | ✅ ViewModels + DataTemplates | ✅ Working | ✅ **COMPLIANT** |
| **Dummy** | `CreateDummyConfiguration` | ✅ ViewModels + DataTemplates | ❌ Case mismatch | ⚠️ **NEEDS NAVIGATION FIX** |
| **TestCaseGenerator** | `CreateTestCaseGeneratorConfiguration` | ❌ UserControls + Manual DataContext | ❌ Case mismatch | ❌ **NON-COMPLIANT** |
| **Requirements** | `CreateRequirementsConfiguration` | ❌ UserControls + Manual DataContext | ✅ Working | ❌ **NEEDS ARCHITECTURE FIX** |
| **NewProject** | `CreateNewProjectConfiguration` | ⚠️ Mixed (PlaceholderViewModels) | ❌ Case mismatch | ⚠️ **PARTIAL COMPLIANCE** |
| **OpenProject** | `CreateOpenProjectConfiguration` | ⚠️ Mixed (PlaceholderViewModels) | ❌ Case mismatch | ⚠️ **PARTIAL COMPLIANCE** |
| **Project** | `CreateProjectConfiguration` | ⚠️ Mixed (PlaceholderViewModels) | ❌ Case mismatch | ⚠️ **PARTIAL COMPLIANCE** |
| **TestCaseCreation** | `CreateTestCaseCreationConfiguration` | ❓ Unknown | ❌ Case mismatch | ❓ **NEEDS ANALYSIS** |
| **TestFlow** | `CreateTestFlowConfiguration` | ❓ Unknown | ❓ No menu call | ❓ **NEEDS ANALYSIS** |
| **LLMLearning** | `CreateLLMLearningConfiguration` | ❓ Unknown | ❓ Routes to Dummy | ❓ **NEEDS ANALYSIS** |
| **Import** | `CreateImportConfiguration` | ❓ Unknown | ❓ No menu call | ❓ **NEEDS ANALYSIS** |

### **Current Success Rate**: 1/11 (9%) fully compliant

### **Target Success Rate**: 11/11 (100%) fully compliant

---

## 🚨 **CRITICAL FIXES NEEDED**

### **Immediate Priority**:
1. **Fix case-sensitivity navigation mismatches** (affects 7+ domains)
2. **Convert TestCaseGenerator from UserControls to ViewModels pattern**  
3. **Convert Requirements from UserControls to ViewModels pattern**
4. **Replace PlaceholderViewModels with real ViewModels** (3 domains)

### **Success Criteria**:
- ✅ All menu items navigate successfully  
- ✅ All domains use ViewModels + DataTemplates pattern
- ✅ No PlaceholderViewModels in production
- ✅ Consistent architecture across all domains
- ✅ Zero navigation failures

**End Goal**: User clicks any menu item → navigation works → shows correct domain views → all using identical architectural pattern