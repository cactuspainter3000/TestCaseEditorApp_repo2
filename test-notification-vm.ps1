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
Write-Host "Please check the LED in the notification area:" -ForegroundColor White
Write-Host "  - Red = LLM Disconnected" -ForegroundColor Red
Write-Host "  - Green = LLM Connected" -ForegroundColor Green
Write-Host "  - Orange = LLM Connecting" -ForegroundColor Yellow

Write-Host ""
Write-Host "Press Enter to close the app and exit test..." -ForegroundColor Yellow
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