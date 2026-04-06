#!/usr/bin/env pwsh
# test-startup-flow.ps1 - Debug the app startup flow to see what configuration is applied

Write-Host "=== Testing App Startup Flow ===" -ForegroundColor Cyan

# Build the application first
Write-Host "`n1. Building application..." -ForegroundColor Yellow
$buildResult = dotnet build --configuration Debug --verbosity quiet
if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed!" -ForegroundColor Red
    exit 1
}

Write-Host "Build successful" -ForegroundColor Green

# Run the app in debug mode with extra logging
Write-Host "`n2. Starting application with debug output..." -ForegroundColor Yellow

# Set environment for enhanced logging
$env:ASPNETCORE_ENVIRONMENT = "Development"

# Start the application
$process = Start-Process -FilePath ".\bin\Debug\net8.0-windows\TestCaseEditorApp.exe" -PassThru -WindowStyle Normal

Write-Host "Application started with PID: $($process.Id)" -ForegroundColor Green
Write-Host "`nApplication is running. Check the debug output in Visual Studio Output window or Debug Console." -ForegroundColor Cyan
Write-Host "Look for debug messages like:" -ForegroundColor White
Write-Host "  *** NavigationService: Publishing SectionChangeRequested ***" -ForegroundColor Gray
Write-Host "  [ViewAreaCoordinator] Section change requested: 'startup'" -ForegroundColor Gray
Write-Host "  [ViewConfigurationService] Configuration created for: startup" -ForegroundColor Gray

Write-Host "`nPress any key to terminate the application..." -ForegroundColor Yellow
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")

# Kill the process
if (!$process.HasExited) {
    $process.Kill()
    Write-Host "Application terminated." -ForegroundColor Green
} else {
    Write-Host "Application already exited." -ForegroundColor Green
}