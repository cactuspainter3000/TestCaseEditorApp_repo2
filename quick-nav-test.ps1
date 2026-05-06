# Clear any existing log
if (Test-Path "c:\temp\navigation-debug.log") {
    Remove-Item "c:\temp\navigation-debug.log" -Force
}

Write-Host "Starting TestCaseEditor navigation debug test..." -ForegroundColor Green
Write-Host "Log file cleared. Launching app..." -ForegroundColor Yellow

# Launch app in background
Start-Process -FilePath ".\bin\Debug\net8.0-windows\TestCaseEditorApp.exe" 

Write-Host "App launched. Please click on 'Test Case Generator' in the side menu." -ForegroundColor Cyan
Write-Host "Press any key after you've clicked the menu item to check the log..." -ForegroundColor Yellow
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")

Write-Host "`nChecking debug log..." -ForegroundColor Green
if (Test-Path "c:\temp\navigation-debug.log") {
    Write-Host "--- Navigation Debug Log ---" -ForegroundColor Cyan
    Get-Content "c:\temp\navigation-debug.log"
    Write-Host "--- End Log ---" -ForegroundColor Cyan
} else {
    Write-Host "No debug log found. Navigation might not be working." -ForegroundColor Red
}

Write-Host "Test complete." -ForegroundColor Green