# Test if NotificationWorkspaceViewModel LED fix is working
Write-Host "Testing NotificationWorkspaceViewModel LED Fix" -ForegroundColor Cyan

# Build the app
Write-Host "Building application..." -ForegroundColor Yellow
dotnet build --verbosity quiet
if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed" -ForegroundColor Red
    exit 1
}

Write-Host "Build successful" -ForegroundColor Green

# Start the app
Write-Host "Starting app to test LED status..." -ForegroundColor Yellow
$appProcess = Start-Process -FilePath ".\bin\Debug\net8.0-windows\TestCaseEditorApp.exe" -PassThru

# Wait for startup
Start-Sleep -Seconds 3

# Check if process is running
if ($appProcess.HasExited) {
    Write-Host "App crashed during startup" -ForegroundColor Red
    Write-Host "Exit code: $($appProcess.ExitCode)" -ForegroundColor Red
    exit 1
}

Write-Host "App started successfully (PID: $($appProcess.Id))" -ForegroundColor Green

# Wait a bit more for full initialization
Start-Sleep -Seconds 2

Write-Host "App should now be running with LED status indicator" -ForegroundColor Cyan
Write-Host ""
Write-Host "IMPORTANT: You're currently seeing the STARTUP notification (basic status)" -ForegroundColor Magenta
Write-Host "The LLM LED is only available in WORKING domains (Requirements, TestCaseGenerator, etc.)" -ForegroundColor Magenta
Write-Host ""
Write-Host "To test the LLM LED:" -ForegroundColor Yellow
Write-Host "1. Navigate to 'Requirements' or 'TestCaseGenerator' domain" -ForegroundColor White
Write-Host "2. Check the LED in the notification area:" -ForegroundColor White
Write-Host "   - Red = LLM Disconnected" -ForegroundColor Red
Write-Host "   - Green = LLM Connected" -ForegroundColor Green
Write-Host "   - Orange = LLM Connecting" -ForegroundColor Yellow
Write-Host ""
Write-Host "Press Enter after testing navigation to a working domain..." -ForegroundColor Yellow
Read-Host

Write-Host ""
Write-Host "Did the LED appear and show correct status in the working domain?" -ForegroundColor Cyan
Write-Host "Type your response:" -ForegroundColor Yellow
Write-Host "  'yes' or 'y' = LED working correctly" -ForegroundColor Green
Write-Host "  'no' or 'n' = LED not working correctly" -ForegroundColor Red
Write-Host "  'missing' or 'm' = LED didn't appear at all" -ForegroundColor Yellow
$response = Read-Host "Response"

Write-Host ""
if ($response -match "^(no?|n)$") {
    Write-Host "LED not working correctly. What did you observe?" -ForegroundColor Red
    Write-Host "  1. Wrong color (red when should be green, etc.)" -ForegroundColor White
    Write-Host "  2. Doesn't change when navigating between domains" -ForegroundColor White
    Write-Host "  3. Shows wrong text" -ForegroundColor White
    Write-Host "  4. Other issue" -ForegroundColor White
    $issue = Read-Host "Issue type (1-4 or describe)"
    
    if ($issue -eq "1") {
        Write-Host "What color is the LED showing?" -ForegroundColor Yellow
        $currentColor = Read-Host "Current LED color (red/green/orange/other)"
        Write-Host "What color should it be showing?" -ForegroundColor Yellow
        $expectedColor = Read-Host "Expected LED color (red/green/orange)"
        Write-Host "LED Color Issue: Currently $currentColor, Expected $expectedColor" -ForegroundColor Yellow
        
        # Test Project domain specifically
        Write-Host ""
        Write-Host "Please test the Project domain:" -ForegroundColor Cyan
        Write-Host "1. Click on 'Project' in the side menu" -ForegroundColor White
        Write-Host "2. Does the LED appear in the Project domain notification area?" -ForegroundColor White
        $projectTest = Read-Host "Project LED visible? (yes/no)"
        Write-Host "Project domain LED status: $projectTest" -ForegroundColor Yellow
    } else {
        Write-Host "Issue noted: $issue" -ForegroundColor Yellow
    }
} elseif ($response -match "^(missing|m)$") {
    Write-Host "LED not visible. Did you navigate to Requirements or TestCaseGenerator?" -ForegroundColor Red
    $navigated = Read-Host "Did you navigate? (y/n)"
    Write-Host "Navigation status noted: $navigated" -ForegroundColor Yellow
} else {
    Write-Host "LED working correctly - great!" -ForegroundColor Green
}

Write-Host ""
Write-Host "Press Enter to close the app..." -ForegroundColor Yellow
Read-Host

# Clean shutdown
try {
    $appProcess.CloseMainWindow() | Out-Null
    Start-Sleep -Seconds 2
    if (!$appProcess.HasExited) {
        $appProcess.Kill()
    }
    Write-Host "App closed successfully" -ForegroundColor Green
} catch {
    Write-Host "Warning: App may still be running" -ForegroundColor Yellow
}