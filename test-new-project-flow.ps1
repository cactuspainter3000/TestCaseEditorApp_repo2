# Quick test to check application startup and new project creation
Write-Host "Testing application startup and new project flow..." -ForegroundColor Green

cd "c:\Users\e10653214\Test Case Editor App Version 2_repo\TestCaseEditorApp"

# Kill any existing instances
try { taskkill /F /IM TestCaseEditorApp.exe 2>$null } catch { }

Write-Host "Starting application..." -ForegroundColor Yellow
Start-Process -FilePath ".\bin\Debug\net8.0-windows\TestCaseEditorApp.exe" -WindowStyle Normal

Write-Host "`nApplication should start in a few seconds." -ForegroundColor Cyan
Write-Host "Please test:" -ForegroundColor White
Write-Host "1. Click on 'New Project' or 'Project' section" -ForegroundColor Yellow  
Write-Host "2. Try creating a new project" -ForegroundColor Yellow
Write-Host "3. Check if any requirements appear" -ForegroundColor Yellow
Write-Host "4. Note any error messages in the UI" -ForegroundColor Yellow

Read-Host "`nPress Enter when done testing (this will close the application)"

# Kill the application
try { 
    taskkill /F /IM TestCaseEditorApp.exe 
    Write-Host "Application closed." -ForegroundColor Green
} catch { 
    Write-Host "Could not close application automatically." -ForegroundColor Yellow 
}