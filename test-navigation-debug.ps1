# Test Script for Navigation Debugging
# This script captures debug console output from the TestCaseGenerator navigation

Write-Host "*** TestCaseGenerator Navigation Debug Test ***" -ForegroundColor Green
Write-Host "1. Kill any existing app processes" -ForegroundColor Yellow

# Kill any existing TestCaseEditorApp processes
Get-Process "TestCaseEditorApp" -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 2

Write-Host "2. Build the solution" -ForegroundColor Yellow
$buildResult = & dotnet build TestCaseEditorApp.sln 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed!" -ForegroundColor Red
    Write-Host $buildResult
    exit 1
}
Write-Host "Build successful!" -ForegroundColor Green

Write-Host "3. Instructions for testing navigation:" -ForegroundColor Yellow
Write-Host "   a) We'll launch the app"
Write-Host "   b) Click on 'Test Case Generator' in the side menu"
Write-Host "   c) Check the console output in this terminal"
Write-Host "   d) Press any key when you've clicked the menu item"
Write-Host ""

Write-Host "4. Launching application..." -ForegroundColor Yellow

# Start the app and capture output
$appProcess = Start-Process -FilePath ".\bin\Debug\net8.0-windows\TestCaseEditorApp.exe" -PassThru -WorkingDirectory "."

Write-Host "App launched (PID: $($appProcess.Id)). Now click 'Test Case Generator' in the side menu..." -ForegroundColor Cyan
Write-Host "Press any key after clicking to see the debug output..." -ForegroundColor Cyan
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")

Write-Host "`n*** Debug Output Analysis ***" -ForegroundColor Green
Write-Host "Expected output should show:" -ForegroundColor Yellow
Write-Host "  - NavigationMediator: NavigateToSection('TestCaseGenerator') called"
Write-Host "  - ViewAreaCoordinator: OnSectionChangeRequested called with 'TestCaseGenerator'"
Write-Host "  - ViewConfigurationService: GetConfigurationForSection called with 'TestCaseGenerator'"
Write-Host "  - ViewConfigurationService: sectionName.ToLowerInvariant() = 'testcasegenerator'"
Write-Host "  - TestCaseGenerator called with context: <context>"
Write-Host "  - TestCaseGenerator config created."
Write-Host ""

Write-Host "If you don't see this output, the navigation event is not being triggered properly." -ForegroundColor Red

Write-Host "`nPress any key to close the app and finish the test..." -ForegroundColor Yellow
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")

# Clean up
if (!$appProcess.HasExited) {
    $appProcess.Kill()
    $appProcess.WaitForExit()
}

Write-Host "Test completed." -ForegroundColor Green