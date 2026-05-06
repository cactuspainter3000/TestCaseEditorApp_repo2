# Navigation Mismatch Fixes Required

## Problem Summary
The side menu calls `NavigateToSection()` with different strings than what the `ViewConfigurationService` switch statement expects.

## Current Mismatches

| **Menu Item** | **SideMenu Calls** | **Switch Expects** | **Fix Needed** |
|---------------|-------------------|-------------------|----------------|
| Test Case Creation | `"TestCaseCreation"` | `"testcasecreation"` | ❌ Case mismatch |
| Test Case Generator | `"TestCaseGenerator"` | `"testcasegenerator"` | ❌ Case + Friday was `"testcase"` |  
| New Project | `"NewProject"` | `"newproject"` | ❌ Case mismatch |
| Dummy Domain | `"Dummy"` | `"dummy"` | ❌ Case mismatch |
| Open Project | `"OpenProject"` | `"openproject"` | ❌ Case mismatch |
| Project | `"Project"` | `"project"` | ✅ Works (case converted) |
| Requirements | `"requirements"` | `"requirements"` | ✅ Works |
| Startup | `"startup"` | `"startup"` | ✅ Works |

## Solution Options

### Option 1: Fix ViewConfigurationService Switch (RECOMMENDED)
Update the switch to match what SideMenu calls:

```csharp
return sectionName?.ToLowerInvariant() switch
{
    "startup" => CreateStartupConfiguration(context),
    "project" => CreateProjectConfiguration(context), 
    "requirements" => CreateRequirementsConfiguration(context),
    "testcasegenerator" => CreateTestCaseGeneratorConfiguration(context),  // Add this
    "testcasecreation" => CreateTestCaseCreationConfiguration(context),    // Add this  
    "newproject" => CreateNewProjectConfiguration(context),               // Add this
    "openproject" => CreateOpenProjectConfiguration(context),             // Add this
    "dummy" => CreateDummyConfiguration(context),                         // Add this
    
    // Keep old working patterns for compatibility:
    "testcase" or "test case creator" => CreateTestCaseGeneratorConfiguration(context), // Keep Friday working version
    "test case creation" => CreateTestCaseCreationConfiguration(context),
    "new project" => CreateNewProjectConfiguration(context), 
    "open project" => CreateOpenProjectConfiguration(context),
    
    _ => CreateDefaultConfiguration(context)
};
```

### Option 2: Fix SideMenuViewModel Calls
Change all the NavigateToSection calls to lowercase:
- Change `"TestCaseCreation"` → `"testcasecreation"`
- Change `"TestCaseGenerator"` → `"testcasegenerator"` 
- Change `"NewProject"` → `"newproject"`
- Change `"Dummy"` → `"dummy"`  
- Change `"OpenProject"` → `"openproject"`

## Root Cause
The switch uses `sectionName?.ToLowerInvariant()` which converts everything to lowercase, but the original switch patterns were defined assuming different casing/naming conventions than what the menu actually calls.

## Friday Working Version Reference
- **TestCaseGenerator** was working when switch expected `"testcase"`
- This suggests the menu was calling something different, OR
- The switch pattern was changed from `"testcase"` to `"testcasegenerator"`

## Immediate Fix
Add the missing switch patterns to handle both old and new naming:

```csharp
"testcasegenerator" => CreateTestCaseGeneratorConfiguration(context),
"testcasecreation" => CreateTestCaseCreationConfiguration(context), 
"newproject" => CreateNewProjectConfiguration(context),
"openproject" => CreateOpenProjectConfiguration(context),
"dummy" => CreateDummyConfiguration(context),
```

This will make ALL menu items work immediately.