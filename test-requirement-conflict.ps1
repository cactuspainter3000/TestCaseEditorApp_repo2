# Test requirement import conflict detection
Write-Host "Testing requirement import conflict detection..." -ForegroundColor Green

# Build the application first
cd "c:\Users\e10653214\Test Case Editor App Version 2_repo\TestCaseEditorApp"
Write-Host "Building application..." -ForegroundColor Yellow
dotnet build --configuration Debug --verbosity minimal

if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed. Cannot test." -ForegroundColor Red
    exit 1
}

Write-Host "Build successful. Starting application..." -ForegroundColor Green

# Start the application in background
$app = Start-Process -FilePath ".\bin\Debug\net8.0-windows\TestCaseEditorApp.exe" -PassThru -WindowStyle Normal

Write-Host "Application started with PID: $($app.Id)" -ForegroundColor Cyan

# Wait a few seconds for startup
Start-Sleep -Seconds 3

Write-Host "`nTEST SCENARIO:" -ForegroundColor Yellow
Write-Host "1. Import a requirement document to establish existing requirements" -ForegroundColor White
Write-Host "2. Try importing the same document again (should detect conflicts)" -ForegroundColor White
Write-Host "3. Verify user is prompted for conflict resolution" -ForegroundColor White

Write-Host "`nPlease perform the following test manually:" -ForegroundColor Magenta
Write-Host "- Navigate to Requirements section" -ForegroundColor White
Write-Host "- Import a Word document with requirements" -ForegroundColor White
Write-Host "- Import the same document again" -ForegroundColor White
Write-Host "- Check if you get notified about duplicates" -ForegroundColor White

Read-Host "`nPress Enter when you're done testing to close the application"

# Kill the application
try {
    Stop-Process -Id $app.Id -Force
    Write-Host "Application stopped." -ForegroundColor Green
} catch {
    Write-Host "Could not stop application. You may need to close it manually." -ForegroundColor Yellow
}