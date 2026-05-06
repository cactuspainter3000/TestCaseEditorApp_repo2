# Clear the log file
Write-Host "Clearing previous debug log..." -ForegroundColor Yellow
if (Test-Path "c:\temp\navigation-debug.log") {
    Remove-Item "c:\temp\navigation-debug.log" -Force
}

Write-Host "Launching TestCase Editor App..." -ForegroundColor Green
Start-Process -FilePath ".\bin\Debug\net8.0-windows\TestCaseEditorApp.exe" 

Write-Host "App launched. Please click on 'Test Case Generator' in the side menu." -ForegroundColor Cyan
Write-Host "Then press any key to check if navigation worked..." -ForegroundColor Yellow
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")

Write-Host "`nChecking navigation debug log:" -ForegroundColor Green
if (Test-Path "c:\temp\navigation-debug.log") {
    Write-Host "--- Debug Log Output ---" -ForegroundColor White -BackgroundColor DarkBlue
    Get-Content "c:\temp\navigation-debug.log" | ForEach-Object {
        Write-Host $_ -ForegroundColor Cyan
    }
    Write-Host "--- End Log ---" -ForegroundColor White -BackgroundColor DarkBlue
    Write-Host "`n✅ Navigation debugging is working! The command is being triggered." -ForegroundColor Green
} else {
    Write-Host "❌ No debug log found. The menu click is still not triggering the command." -ForegroundColor Red
}

Write-Host "`nTest complete." -ForegroundColor Yellow