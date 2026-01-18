# Side Menu Navigation Mapping

## Overview

This document maps the side menu items to their corresponding section names, configuration methods, ViewModels, and Views. This is essential for debugging navigation issues and understanding the architecture.

## Side Menu Items → ViewModels/Views Mapping

| **Side Menu Item** | **Section Name Called** | **Configuration Method** | **Approach** | **Status** |
|-------------------|------------------------|--------------------------|-------------|------------|
| **Test Case Generator** | "TestCaseGenerator" | `CreateTestCaseGeneratorConfiguration` | **UserControls + DataContext** | ❌ **BROKEN** |
| ↳ Project | "Project" | `CreateProjectConfiguration` | ViewModels + DataTemplates | ✅ Working |
| ↳ Project → New Project | "NewProject" | `CreateNewProjectConfiguration` | ViewModels + DataTemplates | ✅ Working |  
| ↳ Project → Dummy Domain | "Dummy" | `CreateDummyConfiguration` | ViewModels + DataTemplates | ✅ Working |
| ↳ Project → Open Project | "OpenProject" | `CreateOpenProjectConfiguration` | ViewModels + DataTemplates | ✅ Working |
| ↳ Requirements | "requirements" | `CreateRequirementsConfiguration` | ViewModels + DataTemplates | ✅ Working |
| ↳ LLM Learning | "Dummy" | `CreateDummyConfiguration` | ViewModels + DataTemplates | ✅ Working |
| ↳ Test Case Creation | "TestCaseCreation" | `CreateTestCaseCreationConfiguration` | ViewModels + DataTemplates | ✅ Working |

## ViewModels/Views Used by Section

| **Section** | **Title ViewModel** | **Header ViewModel** | **Main ViewModel** | **Navigation ViewModel** | **Notification ViewModel** |
|------------|-------------------|---------------------|-------------------|------------------------|---------------------------|
| **TestCaseGenerator** | `TestCaseGenerator_TitleVM` | `TestCaseGenerator_HeaderVM` | `TestCaseGeneratorMainView` **(UserControl)** | `TestCaseGenerator_NavigationControl` **(UserControl)** | `TestCaseGeneratorNotificationViewModel` |
| **Project** | PlaceholderViewModel | PlaceholderViewModel | `Project_MainViewModel` | `Requirements_NavigationViewModel` | `TestCaseGeneratorNotificationViewModel` |
| **NewProject** | PlaceholderViewModel | `NewProjectHeaderViewModel` | `NewProjectWorkflowViewModel` | null | `TestCaseGeneratorNotificationViewModel` |
| **Dummy** | `Dummy_TitleViewModel` | `Dummy_HeaderViewModel` | `Dummy_MainViewModel` | `Dummy_NavigationViewModel` | `Dummy_NotificationViewModel` |
| **Requirements** | `TestCaseGenerator_TitleVM` | `Requirements_HeaderViewModel` | `Requirements_MainViewModel` | `RequirementsNavigationView` **(UserControl)** | `TestCaseGeneratorNotificationViewModel` |
| **Startup** | `StartUp_TitleViewModel` | `StartUp_HeaderViewModel` | `StartUp_MainViewModel` | `StartUp_NavigationViewModel` | `StartUp_NotificationViewModel` |

## Navigation Commands Location

All navigation commands are located in:
- **File**: `MVVM/ViewModels/SideMenuViewModel.cs`
- **Methods**: Various `*NavigationCommand` properties

### Key Command Mappings

```csharp
// From SideMenuViewModel.cs
_navigationMediator.NavigateToSection("TestCaseGenerator");  // Test Case Generator main
_navigationMediator.NavigateToSection("Project");           // Project
_navigationMediator.NavigateToSection("NewProject");        // New Project  
_navigationMediator.NavigateToSection("Dummy");             // Dummy Domain
_navigationMediator.NavigateToSection("requirements");      // Requirements (lowercase)
_navigationMediator.NavigateToSection("TestCaseCreation");  // Test Case Creation
_navigationMediator.NavigateToSection("OpenProject");       // Open Project
```

## Configuration Switch Logic

**File**: `Services/ViewConfigurationService.cs`
**Method**: `GetConfigurationForSection(string sectionName, object? context = null)`

```csharp
return sectionName?.ToLowerInvariant() switch
{
    // Uses: StartUp_TitleViewModel, StartUp_HeaderViewModel, StartUp_MainViewModel, StartUp_NavigationViewModel, StartUp_NotificationViewModel
    "startup" => CreateStartupConfiguration(context),
    
    // Uses: PlaceholderViewModel (title), PlaceholderViewModel (header), Project_MainViewModel, Requirements_NavigationViewModel, TestCaseGeneratorNotificationViewModel
    "project" => CreateProjectConfiguration(context),
    
    // Uses: TestCaseGenerator_TitleVM (shared), Requirements_HeaderViewModel, Requirements_MainViewModel, RequirementsNavigationView (UserControl), TestCaseGeneratorNotificationViewModel
    "requirements" => CreateRequirementsConfiguration(context),
    
    // Uses: TestCaseGenerator_TitleVM, TestCaseGenerator_HeaderVM, TestCaseGeneratorMainView (UserControl), TestCaseGenerator_NavigationControl (UserControl), TestCaseGeneratorNotificationViewModel
    "testcasegenerator" or "test case generator" => CreateTestCaseGeneratorConfiguration(context),
    
    // Uses: WorkspaceHeaderViewModel, PlaceholderViewModel (content), TestCaseGeneratorNotificationViewModel
    "testcasecreation" or "test case creation" => CreateTestCaseCreationConfiguration(context),
    
    // Uses: PlaceholderViewModel (all workspaces)
    "testflow" => CreateTestFlowConfiguration(context),
    
    // Uses: Dummy_TitleViewModel, Dummy_HeaderViewModel, Dummy_MainViewModel, Dummy_NavigationViewModel, Dummy_NotificationViewModel
    "llm learning" => CreateLLMLearningConfiguration(context),
    
    // Uses: WorkspaceHeaderViewModel, PlaceholderViewModel (content), TestCaseGeneratorNotificationViewModel
    "import" => CreateImportConfiguration(context),
    
    // Uses: TestCaseGenerator_TitleVM (shared), NewProjectHeaderViewModel, NewProjectWorkflowViewModel, null (navigation), TestCaseGeneratorNotificationViewModel  
    "newproject" or "new project" => CreateNewProjectConfiguration(context),
    
    // Uses: TestCaseGenerator_TitleVM (shared), null (header), OpenProjectWorkflowViewModel, TestCaseGenerator_NavigationVM, TestCaseGeneratorNotificationViewModel
    "openproject" or "open project" => CreateOpenProjectConfiguration(context),
    
    // Uses: Dummy_TitleViewModel, Dummy_HeaderViewModel, Dummy_MainViewModel, Dummy_NavigationViewModel, Dummy_NotificationViewModel
    "dummy" => CreateDummyConfiguration(context),
    
    // Uses: PlaceholderViewModel (all workspaces)
    _ => CreateDefaultConfiguration(context)
};
```

## Architecture Patterns

### Pattern 1: ViewModels + DataTemplates (WORKING)
**Used by**: Startup, Project, NewProject, Dummy, Requirements, TestCaseCreation, OpenProject

**How it works**:
1. Configuration method resolves ViewModels from DI container
2. Returns ViewModels directly in ViewConfiguration
3. WPF uses DataTemplates in MainWindow.xaml to render ViewModels

**Example** (Dummy):
```csharp
return new ViewConfiguration(
    sectionName: "Dummy Domain",
    titleViewModel: dummyTitleVM,         // ViewModel
    headerViewModel: dummyHeaderVM,       // ViewModel  
    contentViewModel: dummyMainVM,        // ViewModel
    navigationViewModel: dummyNavigationVM, // ViewModel
    notificationViewModel: dummyNotificationVM, // ViewModel
    context: context
);
```

### Pattern 2: UserControls + DataContext (BROKEN)
**Used by**: TestCaseGenerator

**How it works**:
1. Configuration method resolves ViewModels from DI container  
2. Creates UserControl instances manually
3. Sets UserControl.DataContext to ViewModel
4. Returns UserControl instances in ViewConfiguration

**Example** (TestCaseGenerator):
```csharp
// Create UserControl and bind ViewModel
var mainControl = new TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Views.TestCaseGeneratorMainView();
mainControl.DataContext = mainVM;
mainContent = mainControl;

return new ViewConfiguration(
    sectionName: "TestCase",
    contentViewModel: mainContent, // UserControl, not ViewModel
    // ...
);
```

## Problem Diagnosis

### The Issue
**TestCaseGenerator** is the **ONLY** section using **UserControls + DataContext** approach, while all other working sections use **ViewModels + DataTemplates**.

### Why TestCaseGenerator Fails
1. Creates UserControls manually but they may not be properly integrated with the WPF visual tree
2. DataContext binding may fail due to timing issues
3. UserControls created outside normal WPF lifecycle

### Why Other Sections Work
1. Use standard DataTemplate mapping in MainWindow.xaml
2. WPF handles the ViewModel → View binding automatically
3. Proper integration with ContentPresenter/ContentControl

## Solution Options

### Option 1: Convert TestCaseGenerator to ViewModels + DataTemplates (RECOMMENDED)
- Change `CreateTestCaseGeneratorConfiguration` to return ViewModels directly
- Ensure proper DataTemplates exist in MainWindow.xaml
- Follow the proven working pattern used by other sections

### Option 2: Fix UserControl Approach
- Debug why UserControls aren't displaying
- Check visual tree integration
- Verify DataContext binding

### Option 3: Revert to Last Working Version
- Use git to find when TestCaseGenerator was working
- Restore that implementation
- Document what was different

## DataTemplates Reference

**File**: `MVVM/Views/MainWindow.xaml`

### Existing TestCaseGenerator DataTemplates
```xml
<DataTemplate DataType="{x:Type tcgvm:TestCaseGenerator_TitleVM}">
    <tcgviews:TestCaseGenerator_TitleView />
</DataTemplate>

<DataTemplate DataType="{x:Type tcgvm:TestCaseGenerator_HeaderVM}">
    <tcgviews:TestCaseGenerator_HeaderView />
</DataTemplate>

<DataTemplate DataType="{x:Type tcgvm:TestCaseGenerator_NavigationVM}">
    <tcgviews:TestCaseGenerator_NavigationControl />
</DataTemplate>
```

### Missing DataTemplates
- `TestCaseGeneratorMainVM` → Uses ResourceDictionary in Resources/MainWindowResources.xaml
- `TestCaseGeneratorNotificationViewModel` → Exists in MainWindow.xaml

## Next Steps

1. **Immediate**: Convert TestCaseGenerator to ViewModels + DataTemplates pattern
2. **Test**: Verify TestCaseGenerator navigation works
3. **Document**: Update this file with resolution
4. **Cleanup**: Remove unused UserControl creation code

## Last Updated
January 18, 2026 - Initial documentation during navigation debugging session