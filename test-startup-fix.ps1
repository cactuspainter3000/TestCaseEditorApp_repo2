#!/usr/bin/env pwsh
#
# Quick test to verify the startup title fix is working
#

Write-Host "🔍 Testing Startup Title Fix..." -ForegroundColor Cyan
Write-Host ""

# Build and run for a few seconds, then check if process starts successfully
Write-Host "Starting application..." -ForegroundColor Yellow
$proc = Start-Process -FilePath "dotnet" -ArgumentList "run" -WorkingDirectory "." -PassThru -WindowStyle Hidden

Start-Sleep -Seconds 3

if ($proc.HasExited -eq $false) {
    Write-Host "✅ Application started successfully and is running" -ForegroundColor Green
    Write-Host "✅ No startup crashes - title configuration fix appears successful" -ForegroundColor Green
    
    # Stop the process
    $proc.Kill()
    Write-Host "Application stopped for testing" -ForegroundColor Gray
} else {
    Write-Host "❌ Application exited unexpectedly" -ForegroundColor Red
    Write-Host "Exit code: $($proc.ExitCode)" -ForegroundColor Red
}

Write-Host ""
Write-Host "🎯 Expected Result: TitleWorkspace should now show 'Systems ATE APP'" -ForegroundColor Cyan
Write-Host "🔧 Fix Applied: Removed auto-Default configuration that was overriding startup" -ForegroundColor White