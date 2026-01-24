#!/usr/bin/env pwsh
# Test script with timeout and detailed logging for Jama Connect debugging

Write-Host "=== JAMA CONNECT DETAILED DEBUG TEST ===" -ForegroundColor Yellow
Write-Host "Starting test with 30-second timeout..." -ForegroundColor Cyan

# Start the test as a background job with detailed logging
$job = Start-Job -ScriptBlock {
    # Set environment variables
    $env:JAMA_BASE_URL = "https://jama02.rockwellcollins.com/contour"
    $env:JAMA_CLIENT_ID = "hycv5tyzpvyvhmi" 
    $env:JAMA_CLIENT_SECRET = "Wy+qLYTczFkxwZIhJJ/I4Q=="
    
    # Build and run the application in test mode
    Set-Location "C:\Users\e10653214\Test Case Editor App Version 2_repo\TestCaseEditorApp"
    
    Write-Host "[JOB] Starting application..." -ForegroundColor Green
    
    # Run with detailed logging
    dotnet run --project . --no-build --configuration Debug 2>&1 | ForEach-Object {
        Write-Host "[JOB] $_" -ForegroundColor Gray
    }
}

# Wait for job to complete or timeout after 30 seconds
$completed = Wait-Job -Job $job -Timeout 30

if ($completed) {
    Write-Host "Job completed within timeout" -ForegroundColor Green
    Receive-Job -Job $job
} else {
    Write-Host "Job TIMED OUT after 30 seconds - indicates hanging!" -ForegroundColor Red
    Write-Host "Stopping job..." -ForegroundColor Yellow
    Stop-Job -Job $job
    
    # Get any output that was produced before timeout
    $output = Receive-Job -Job $job
    if ($output) {
        Write-Host "Partial output before timeout:" -ForegroundColor Cyan
        $output | ForEach-Object {
            Write-Host "  $_" -ForegroundColor Gray
        }
    }
}

# Clean up
Remove-Job -Job $job -Force

Write-Host "=== DEBUG TEST COMPLETE ===" -ForegroundColor Yellow