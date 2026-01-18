# Navigation Debug Test Plan

## Current Status
After extensive architectural fixes, all domains now use the AI Guide standard (ViewModels + DataTemplates). 
However, you've reported that "clicking on test case generator still doesn't show the correct views".

## Debug Logging Added
We've added comprehensive debug logging to:
1. NavigationMediator.NavigateToSection() - logs the section name received  
2. ViewAreaCoordinator.OnSectionChangeRequested() - logs when the event is handled
3. ViewConfigurationService.GetConfigurationForSection() - logs what section name is processed
4. DebugAndCallTestCaseGenerator() - logs TestCaseGenerator configuration creation

## Debug Log Location
All debug output is written to: `c:\temp\navigation-debug.log`

## Testing Steps

### Step 1: Clear Log and Launch App
```powershell
# Clear any existing log
if (Test-Path "c:\temp\navigation-debug.log") { Remove-Item "c:\temp\navigation-debug.log" -Force }

# Launch app
Start-Process -FilePath ".\bin\Debug\net8.0-windows\TestCaseEditorApp.exe"
```

### Step 2: Test Navigation
1. Wait for app to fully load
2. Click on "Test Case Generator" in the side menu
3. Observe what happens in the UI (are views changing?)

### Step 3: Check Debug Output
```powershell
# Check if debug log was created
if (Test-Path "c:\temp\navigation-debug.log") {
    Get-Content "c:\temp\navigation-debug.log"
} else {
    Write-Host "No debug log found - navigation event might not be triggering"
}
```

## Expected Debug Output
When you click "Test Case Generator", you should see:
```
[HH:mm:ss] NavigationMediator: NavigateToSection('TestCaseGenerator') called
[HH:mm:ss] NavigationMediator: Publishing SectionChangeRequested for 'TestCaseGenerator'
[HH:mm:ss] ViewAreaCoordinator: OnSectionChangeRequested('TestCaseGenerator')
[HH:mm:ss] ViewConfigurationService: GetConfigurationForSection('TestCaseGenerator') - lowercase: 'testcasegenerator'
[HH:mm:ss] DebugAndCallTestCaseGenerator: Creating TestCaseGenerator configuration with context: [context]
[HH:mm:ss] DebugAndCallTestCaseGenerator: TestCaseGenerator config created - type: ViewConfiguration
```

## What This Tells Us

### If No Log File Is Created:
- The navigation event is not being triggered at all
- Check if the side menu is properly wired to call NavigateToSection()

### If Log Shows NavigationMediator But Nothing Else:
- The event is being published but not received by ViewAreaCoordinator
- Check the mediator subscription in ViewAreaCoordinator

### If Log Shows Up to ViewConfigurationService But No TestCaseGenerator Call:
- The switch pattern isn't matching "TestCaseGenerator" -> "testcasegenerator"
- Check the switch statement in GetConfigurationForSection()

### If Full Log Appears But Views Don't Update:
- Navigation is working but there's an issue with the ViewModels or DataTemplates
- Check MainWindow.xaml DataTemplate for TestCaseGeneratorMainVM
- Check if ViewModels are being resolved from DI properly

## Current Navigation Flow
1. SideMenuViewModel.NavigateToTestCaseGenerator() calls `NavigateToSection("TestCaseGenerator")`
2. NavigationMediator publishes SectionChangeRequested event  
3. ViewAreaCoordinator.OnSectionChangeRequested() handles the event
4. ViewConfigurationService.GetConfigurationForSection("TestCaseGenerator") is called
5. Switch pattern matches "testcasegenerator" and calls DebugAndCallTestCaseGenerator()
6. DebugAndCallTestCaseGenerator() calls CreateTestCaseGeneratorConfiguration()
7. CreateTestCaseGeneratorConfiguration() resolves ViewModels from DI
8. ViewConfiguration is returned with the resolved ViewModels
9. ViewAreaCoordinator applies the configuration to the workspace areas
10. DataTemplates in MainWindow.xaml should automatically render the Views

## Next Steps
1. Run the test and share the debug output
2. Based on the results, we can identify exactly where the navigation is failing
3. Fix the specific issue preventing the views from updating

## Files Modified for Debug Logging
- NavigationMediator.cs - Added log output for navigation calls
- ViewAreaCoordinator.cs - Added log output for section change events  
- ViewConfigurationService.cs - Added log output for configuration requests