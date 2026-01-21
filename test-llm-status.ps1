# Test script to check LLM status communication
Write-Host "Testing LLM status updates..." -ForegroundColor Cyan

# Check if AnythingLLM is responding
Write-Host "Checking AnythingLLM on localhost:3001..." -ForegroundColor Yellow
try {
    $response = Invoke-WebRequest -Uri "http://localhost:3001" -Method GET -TimeoutSec 5
    Write-Host "✅ AnythingLLM is responding: $($response.StatusCode)" -ForegroundColor Green
    Write-Host "Content preview: $($response.Content.Substring(0, [Math]::Min(100, $response.Content.Length)))" -ForegroundColor White
} catch {
    Write-Host "❌ AnythingLLM not responding: $($_.Exception.Message)" -ForegroundColor Red
}

# Check if the application is running
Write-Host "`nChecking if TestCaseEditorApp is running..." -ForegroundColor Yellow
$appProcess = Get-Process | Where-Object { $_.ProcessName -like "*TestCaseEditor*" }
if ($appProcess) {
    Write-Host "✅ TestCaseEditorApp is running (PID: $($appProcess.Id))" -ForegroundColor Green
} else {
    Write-Host "❌ TestCaseEditorApp not found in running processes" -ForegroundColor Red
}

Write-Host "`nDone." -ForegroundColor Cyan