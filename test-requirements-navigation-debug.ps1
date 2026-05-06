#!/usr/bin/env powershell

Write-Host "Testing Requirements navigation with detailed logging..." -ForegroundColor Cyan

# Set environment for debugging
$env:LLM_PROVIDER = "noop"

# Start application and wait for it to load
Write-Host "Starting application..." -ForegroundColor Yellow
$app = Start-Process -FilePath ".\bin\Debug\net8.0-windows\TestCaseEditorApp.exe" -PassThru -RedirectStandardOutput "requirements-test-output.log" -RedirectStandardError "requirements-test-errors.log"

# Wait for app to start
Start-Sleep -Seconds 5

if ($app.HasExited) {
    Write-Host "❌ Application failed to start" -ForegroundColor Red
    Write-Host "Stdout:" -ForegroundColor Yellow
    Get-Content "requirements-test-output.log" -ErrorAction SilentlyContinue
    Write-Host "Stderr:" -ForegroundColor Yellow
    Get-Content "requirements-test-errors.log" -ErrorAction SilentlyContinue
    exit 1
}

Write-Host "✅ Application started (PID: $($app.Id))" -ForegroundColor Green

# Simulate opening a test project
Write-Host "Note: For full testing, manually open the test project and navigate to Requirements" -ForegroundColor Yellow
Write-Host "Expected behavior:" -ForegroundColor Cyan
Write-Host "1. Open project loads requirements successfully" -ForegroundColor Cyan
Write-Host "2. Navigation to Requirements shows proper ViewModel bindings" -ForegroundColor Cyan
Write-Host "3. No threading exceptions during navigation" -ForegroundColor Cyan

Write-Host "Press Enter to stop test and check logs..."
Read-Host

# Stop application
Write-Host "Stopping application..." -ForegroundColor Yellow
$app.Kill()
$app.WaitForExit(5000)

Write-Host "Checking logs for threading issues and binding errors..." -ForegroundColor Cyan

# Check for threading errors
$threadingErrors = Select-String -Path "requirements-test-errors.log" -Pattern "InvalidOperationException.*thread.*access" -ErrorAction SilentlyContinue
if ($threadingErrors) {
    Write-Host "❌ Threading errors found:" -ForegroundColor Red
    $threadingErrors | ForEach-Object { Write-Host $_.Line -ForegroundColor Red }
}

# Check for binding errors
$bindingErrors = Select-String -Path "requirements-test-errors.log" -Pattern "BindingExpression.*TestCaseGenerator_NavigationVM" -ErrorAction SilentlyContinue
if ($bindingErrors) {
    Write-Host "❌ Wrong ViewModel binding errors found:" -ForegroundColor Red
    $bindingErrors | ForEach-Object { Write-Host $_.Line -ForegroundColor Red }
}

# Check for our debug logs
$debugLogs = Select-String -Path "requirements-test-output.log" -Pattern "EnsureRequirementsNavigationView|DataContext set to|Requirements_NavigationViewModel" -ErrorAction SilentlyContinue
if ($debugLogs) {
    Write-Host "✅ Debug logs found:" -ForegroundColor Green
    $debugLogs | ForEach-Object { Write-Host $_.Line -ForegroundColor Green }
}

Write-Host "Test completed. Check requirements-test-output.log and requirements-test-errors.log for details" -ForegroundColor Cyan