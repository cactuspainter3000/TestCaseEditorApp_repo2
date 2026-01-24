# UI Test Trigger Script
# This will programmatically trigger the UI test that shows hanging

$env:JAMA_BASE_URL = "https://jama02.rockwellcollins.com/contour"
$env:JAMA_CLIENT_ID = "hycv5tyzpvyvhmi" 
$env:JAMA_CLIENT_SECRET = "Wy+qLYTczFkxwZIhJJ/I4Q=="

Write-Host "=== UI JAMA TEST TRIGGER ===" -ForegroundColor Yellow

# Start the WPF application
Write-Host "Starting WPF application..." -ForegroundColor Green
$process = Start-Process -FilePath "dotnet" -ArgumentList "run --project . --no-build" -PassThru

Write-Host "Process started with PID $($process.Id)" -ForegroundColor Green
Write-Host "The application is now running." -ForegroundColor Cyan
Write-Host "Use the UI to trigger the Jama test and observe hanging..." -ForegroundColor Yellow
Write-Host "Watch the debug logs and provide them back for analysis." -ForegroundColor Yellow
Write-Host ""
Write-Host "Press any key to terminate the application..." -ForegroundColor Red

# Wait for user input
Read-Host

Write-Host "Terminating application..." -ForegroundColor Red
$process.Kill()

Write-Host "Application terminated." -ForegroundColor Green