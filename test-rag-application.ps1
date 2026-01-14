# Test script to run application and trigger RAG analysis with enhanced logging
param(
    [string]$TestFile = "test_additional_requirements.txt"
)

Write-Host "🚀 Starting TestCaseEditorApp with enhanced RAG logging..." -ForegroundColor Cyan
Write-Host "=" * 60 -ForegroundColor Cyan

# Check if test file exists
if (Test-Path $TestFile) {
    Write-Host "✓ Test file found: $TestFile" -ForegroundColor Green
} else {
    Write-Host "❌ Test file not found: $TestFile" -ForegroundColor Red
    Write-Host "Available files:" -ForegroundColor Yellow
    Get-ChildItem -Filter "*.txt" | ForEach-Object { Write-Host "  - $($_.Name)" -ForegroundColor Yellow }
}

Write-Host "`n📋 Instructions:" -ForegroundColor Cyan
Write-Host "1. The application will start with enhanced AnythingLLMService logging" -ForegroundColor White
Write-Host "2. Import requirements from '$TestFile'" -ForegroundColor White
Write-Host "3. Start requirement analysis to trigger RAG" -ForegroundColor White
Write-Host "4. Check console output for detailed AnythingLLM API calls and responses" -ForegroundColor White
Write-Host "5. Look for:" -ForegroundColor White
Write-Host "   - Workspace slug being used" -ForegroundColor Yellow
Write-Host "   - API authentication status" -ForegroundColor Yellow
Write-Host "   - RAG request details and response length" -ForegroundColor Yellow
Write-Host "   - Any error details from AnythingLLM API" -ForegroundColor Yellow

Write-Host "`n🔍 Enhanced logging will show:" -ForegroundColor Cyan
Write-Host "  - [AnythingLLM] API endpoint URLs" -ForegroundColor White
Write-Host "  - [AnythingLLM] API key configuration status" -ForegroundColor White  
Write-Host "  - [AnythingLLM] Response status codes and content" -ForegroundColor White
Write-Host "  - [RAG] Workspace verification and setup" -ForegroundColor White
Write-Host "  - [RAG] Request timing and response analysis" -ForegroundColor White

Write-Host "`n▶️  Press any key to start the application..." -ForegroundColor Green
Read-Host

# Start the application
Start-Process -FilePath ".\bin\Debug\net8.0-windows\TestCaseEditorApp.exe" -Wait

Write-Host "`n✅ Application session completed. Check console output above for RAG debugging details." -ForegroundColor Green