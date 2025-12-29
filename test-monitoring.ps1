# Test script to verify AnythingLLM monitoring is working
Write-Host "Testing GenericServiceMonitor integration..."

# Start the application in the background
Write-Host "Starting TestCaseEditorApp..."
$app = Start-Process -FilePath "dotnet" -ArgumentList "run --project TestCaseEditorApp.csproj" -PassThru -NoNewWindow

# Wait for app to initialize
Write-Host "Waiting for application to initialize (15 seconds)..."
Start-Sleep -Seconds 15

# Check if port 3001 is available (AnythingLLM should be monitored even if not running)
$port3001 = Test-NetConnection -ComputerName localhost -Port 3001 -InformationLevel Quiet -WarningAction SilentlyContinue
Write-Host "AnythingLLM (port 3001) status: $($port3001)"

# Let monitoring run for a bit to see status checks
Write-Host "Letting monitoring run for 30 seconds to observe status checks..."
Start-Sleep -Seconds 30

# Stop the application
Write-Host "Stopping application..."
try {
    $app.Kill()
    Write-Host "Application stopped successfully"
} catch {
    Write-Host "Failed to stop application: $($_.Exception.Message)"
}

Write-Host "Test completed. Check application logs for monitoring status messages."