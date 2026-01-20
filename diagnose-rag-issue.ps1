Write-Host "=== RAG Upload Diagnostic ===" -ForegroundColor Cyan
Write-Host ""

Write-Host "Checking if this is likely an existing vs new project issue..." -ForegroundColor Yellow
Write-Host ""

Write-Host "1. EXISTING PROJECT (most likely):" -ForegroundColor Red
Write-Host "   - Screenshot shows 10/10 at 10:44 AM" -ForegroundColor White
Write-Host "   - Our fix was completed around that time" -ForegroundColor White  
Write-Host "   - Existing workspaces still have old RAG documents" -ForegroundColor White
Write-Host ""

Write-Host "2. SOLUTION - Test with NEW project:" -ForegroundColor Green
Write-Host "   - Close current project" -ForegroundColor White
Write-Host "   - Create brand new project (different name)" -ForegroundColor White
Write-Host "   - This will trigger automatic RAG upload with our fix" -ForegroundColor White
Write-Host "   - Test same poor requirement" -ForegroundColor White
Write-Host ""

Write-Host "3. If NEW project also shows 10/10:" -ForegroundColor Yellow
Write-Host "   - Check workspace creation logs" -ForegroundColor White
Write-Host "   - Verify RAG files exist in Config folder" -ForegroundColor White
Write-Host "   - Test manual AnythingLLM workspace creation" -ForegroundColor White
Write-Host ""

Write-Host "Quick verification - checking if RAG files have our fixes:" -ForegroundColor Cyan
$ragPath = "Config\RAG-JSON-Schema-Training.md"
if (Test-Path $ragPath) {
    $ragContent = Get-Content $ragPath -Raw
    if ($ragContent -match "ORIGINAL.*REQUIREMENT.*QUALITY") {
        Write-Host "✅ RAG training document has scoring fixes" -ForegroundColor Green
    } else {
        Write-Host "❌ RAG training document missing scoring fixes" -ForegroundColor Red
    }
    
    if ($ragContent -match "Rate the user's original requirement") {
        Write-Host "✅ RAG document has explicit original rating instructions" -ForegroundColor Green
    } else {
        Write-Host "❌ RAG document missing explicit instructions" -ForegroundColor Red
    }
} else {
    Write-Host "❌ RAG training document not found at $ragPath" -ForegroundColor Red
}

Write-Host ""
Write-Host "RECOMMENDATION: Try creating a NEW project to test the fix." -ForegroundColor Yellow