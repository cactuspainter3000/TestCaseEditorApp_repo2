#!/usr/bin/env pwsh
#
# Test script to verify Unload Project functionality
# 
# Expected workflow:
# 1. Start app
# 2. Open existing project 
# 3. Navigate to Requirements (should show data)
# 4. Click Unload Project
# 5. Verify Requirements data is cleared
#

Write-Host "🧪 Testing Unload Project Fix" -ForegroundColor Cyan
Write-Host ""

# Clear old log files
$logDir = "$env:LOCALAPPDATA\TestCaseEditorApp\logs"
if (Test-Path $logDir) {
    Write-Host "🧹 Clearing old log files..." -ForegroundColor Yellow
    Remove-Item "$logDir\*" -Force -ErrorAction SilentlyContinue
}

# Start the application
Write-Host "🚀 Starting TestCaseEditorApp..." -ForegroundColor Green
$app = Start-Process -FilePath "dotnet" -ArgumentList "run --project TestCaseEditorApp.csproj --configuration Debug" -PassThru -WindowStyle Normal

# Wait for app to start
Write-Host "⏳ Waiting for application to start (10 seconds)..." -ForegroundColor Yellow
Start-Sleep -Seconds 10

Write-Host ""
Write-Host "📋 TEST INSTRUCTIONS:" -ForegroundColor Cyan
Write-Host "1. Open an existing project (Requirements should load)" -ForegroundColor White  
Write-Host "2. Navigate to Requirements section (verify data is visible)" -ForegroundColor White
Write-Host "3. Click 'Unload Project' button" -ForegroundColor White
Write-Host "4. Verify Requirements navigation is cleared/reset" -ForegroundColor White
Write-Host ""
Write-Host "Press ENTER when you've completed the test..." -ForegroundColor Yellow
Read-Host

# Stop the application
Write-Host "⏹️ Stopping application..." -ForegroundColor Yellow
if (!$app.HasExited) {
    $app.Kill()
    $app.WaitForExit(5000)
}

# Analyze logs
Write-Host ""
Write-Host "📊 LOG ANALYSIS:" -ForegroundColor Cyan

if (Test-Path $logDir) {
    $logs = Get-Content "$logDir\*.log" -ErrorAction SilentlyContinue
    
    # Check for key events
    $projectOpened = $logs | Where-Object { $_ -match "ProjectOpened" }
    $broadcastReceived = $logs | Where-Object { $_ -match "🔔🔔🔔 BROADCAST RECEIVED" }
    $workspaceTracking = $logs | Where-Object { $_ -match "NewProjectMediator workspace tracking updated" }
    $unloadClicked = $logs | Where-Object { $_ -match "UnloadProject button clicked" }
    $projectClosed = $logs | Where-Object { $_ -match "Broadcasting ProjectClosed event" }
    $requirementsClear = $logs | Where-Object { $_ -match "Clearing requirements collection on project close" }
    $navigationReset = $logs | Where-Object { $_ -match "Collection is empty, resetting navigation state" }
    
    Write-Host ""
    Write-Host "✅ Key Events Found:" -ForegroundColor Green
    Write-Host "   📡 Project Opened: $($projectOpened.Count)" -ForegroundColor White
    Write-Host "   🔔 Broadcasts Received: $($broadcastReceived.Count)" -ForegroundColor White
    Write-Host "   📍 Workspace Tracking: $($workspaceTracking.Count)" -ForegroundColor White
    Write-Host "   🖱️ Unload Clicked: $($unloadClicked.Count)" -ForegroundColor White
    Write-Host "   📡 Project Closed Broadcast: $($projectClosed.Count)" -ForegroundColor White
    Write-Host "   🗑️ Requirements Cleared: $($requirementsClear.Count)" -ForegroundColor White
    Write-Host "   🧹 Navigation Reset: $($navigationReset.Count)" -ForegroundColor White
    
    Write-Host ""
    if ($projectClosed.Count -gt 0 -and $requirementsClear.Count -gt 0 -and $navigationReset.Count -gt 0) {
        Write-Host "🎉 SUCCESS: Unload Project fix is working properly!" -ForegroundColor Green
        Write-Host "   - Project close event broadcast ✅" -ForegroundColor Green
        Write-Host "   - Requirements data cleared ✅" -ForegroundColor Green
        Write-Host "   - Navigation state reset ✅" -ForegroundColor Green
    } else {
        Write-Host "⚠️ ISSUE DETECTED:" -ForegroundColor Red
        if ($projectClosed.Count -eq 0) {
            Write-Host "   - ProjectClosed event not broadcast ❌" -ForegroundColor Red
        }
        if ($requirementsClear.Count -eq 0) {
            Write-Host "   - Requirements not cleared ❌" -ForegroundColor Red
        }
        if ($navigationReset.Count -eq 0) {
            Write-Host "   - Navigation not reset ❌" -ForegroundColor Red
        }
    }
    
    # Show recent logs for debugging
    Write-Host ""
    Write-Host "📝 Recent log entries:" -ForegroundColor Cyan
    $logs | Select-Object -Last 20 | ForEach-Object { Write-Host "   $_" -ForegroundColor DarkGray }
    
} else {
    Write-Host "⚠️ No log files found. Check if logging is working." -ForegroundColor Red
}

Write-Host ""
Write-Host "🏁 Test completed!" -ForegroundColor Cyan