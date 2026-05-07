# Quick hanging diagnostic test
# This will run for exactly 15 seconds then kill the process

$env:JAMA_BASE_URL = "https://jama02.rockwellcollins.com/contour"
$env:JAMA_CLIENT_ID = "hycv5tyzpvyvhmi" 
$env:JAMA_CLIENT_SECRET = "Wy+qLYTczFkxwZIhJJ/I4Q=="

Write-Host "=== QUICK HANGING DIAGNOSTIC ===" -ForegroundColor Yellow
Write-Host "Running for 15 seconds then forcing termination..." -ForegroundColor Cyan

# Start the process in the background
$process = Start-Process -FilePath "dotnet" -ArgumentList "run --project . --no-build" -NoNewWindow -PassThru -RedirectStandardOutput ".\test_output.txt" -RedirectStandardError ".\test_error.txt"

Write-Host "Process started with PID $($process.Id)" -ForegroundColor Green
Write-Host "Waiting 15 seconds for hanging to occur..." -ForegroundColor Yellow

# Wait exactly 15 seconds
Start-Sleep -Seconds 15

Write-Host "15 seconds elapsed - killing process..." -ForegroundColor Red
$process.Kill()

Write-Host "Process killed. Checking logs..." -ForegroundColor Cyan

# Check if log files exist and show relevant content
if (Test-Path ".\test_output.txt") {
    $output = Get-Content ".\test_output.txt" -ErrorAction SilentlyContinue
    if ($output) {
        Write-Host "STDOUT Output:" -ForegroundColor Green
        $output | ForEach-Object { Write-Host "  $_" }
    } else {
        Write-Host "No STDOUT output captured" -ForegroundColor Yellow
    }
} else {
    Write-Host "No STDOUT file created" -ForegroundColor Yellow
}

if (Test-Path ".\test_error.txt") {
    $errors = Get-Content ".\test_error.txt" -ErrorAction SilentlyContinue
    if ($errors) {
        Write-Host "STDERR Output:" -ForegroundColor Red
        $errors | ForEach-Object { Write-Host "  $_" }
    } else {
        Write-Host "No STDERR output captured" -ForegroundColor Yellow
    }
} else {
    Write-Host "No STDERR file created" -ForegroundColor Yellow
}

# Check debug logs if they exist
if (Test-Path "debug_output.txt") {
    Write-Host "Checking debug_output.txt for Jama logs..." -ForegroundColor Cyan
    $debugLines = Get-Content "debug_output.txt" -Tail 20 | Where-Object { $_ -like "*JamaConnect*" }
    if ($debugLines) {
        Write-Host "Recent Jama debug logs:" -ForegroundColor Green
        $debugLines | ForEach-Object { Write-Host "  $_" }
    } else {
        Write-Host "No recent JamaConnect logs in debug_output.txt" -ForegroundColor Yellow
    }
} else {
    Write-Host "No debug_output.txt file found" -ForegroundColor Yellow
}

# Clean up
Remove-Item ".\test_output.txt" -Force -ErrorAction SilentlyContinue
Remove-Item ".\test_error.txt" -Force -ErrorAction SilentlyContinue

Write-Host "=== DIAGNOSTIC COMPLETE ===" -ForegroundColor Yellow