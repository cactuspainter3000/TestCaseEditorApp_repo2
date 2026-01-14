#!/usr/bin/env pwsh

<#
.SYNOPSIS
AI Guide-Based DI Resolution Diagnostic Tool

.DESCRIPTION
Tests DI resolution step-by-step following "Questions First, Code Second" methodology.
Creates detailed diagnostic output to identify specific DI failure points.
#>

param()

Write-Host "🔍 AI Guide DI Resolution Diagnostic" -ForegroundColor Cyan
Write-Host "===================================" -ForegroundColor Cyan
Write-Host "Following 'Questions First, Code Second' methodology..." -ForegroundColor Green

# Question 1: Does the application build?
Write-Host "`n📦 Step 1: Verifying build..." -ForegroundColor Yellow
dotnet build --configuration Debug --verbosity minimal
if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ Build failed - cannot proceed" -ForegroundColor Red
    exit 1
}
Write-Host "✅ Build successful" -ForegroundColor Green

# Question 2: Can we inspect the DI container at runtime?
Write-Host "`n🔍 Step 2: Creating runtime diagnostic..." -ForegroundColor Yellow

# Let me test a simpler approach - just launch the app and immediately check output
try {
    Write-Host "Starting application for immediate diagnostic..." -ForegroundColor Green
    $process = Start-Process -FilePath "bin\Debug\net8.0-windows\TestCaseEditorApp.exe" -PassThru -WindowStyle Hidden
    
    # Give app time to initialize
    Write-Host "Waiting for application startup and DI initialization..." -ForegroundColor Yellow
    Start-Sleep -Seconds 3
    
    if ($process.HasExited) {
        Write-Host "ERROR: Application exited during startup!" -ForegroundColor Red
        return
    }
    
    # The app should have started and initialized the DI container
    # The debug log should be created if ViewConfigurationService.CreateNewProjectConfiguration is called
    Write-Host "Application is running. Checking for debug output..." -ForegroundColor Green
    
    # Wait a bit more and check for debug log
    Start-Sleep -Seconds 2
    
    # Clean shutdown
    Write-Host "Stopping application..." -ForegroundColor Yellow
    if (!$process.HasExited) {
        $process.CloseMainWindow()
        Start-Sleep -Seconds 2
        if (!$process.HasExited) {
            $process.Kill()
        }
    }
    
    # Check debug log
    Write-Host "`n=== Debug Log Analysis ===" -ForegroundColor Cyan
    if (Test-Path $debugLog) {
        Write-Host "Debug log created! Contents:" -ForegroundColor Green
        Get-Content $debugLog | ForEach-Object { Write-Host "  $_" -ForegroundColor White }
    } else {
        Write-Host "No debug log found. NewProject configuration may not have been triggered." -ForegroundColor Yellow
        Write-Host "This suggests the issue may be earlier in the application lifecycle." -ForegroundColor Yellow
    }
}
catch {
    Write-Host "ERROR during test: $_" -ForegroundColor Red
}
finally {
    # Cleanup any remaining processes
    Get-Process -Name "TestCaseEditorApp" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
}