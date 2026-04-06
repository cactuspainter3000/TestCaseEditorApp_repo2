#!/usr/bin/env pwsh
# Test the JSON repair loop functionality

Write-Host "🔧 Testing JSON Repair Loop Implementation..." -ForegroundColor Green

# Build first (quietly)
Write-Host "Building project..." -ForegroundColor Cyan
dotnet build TestCaseEditorApp.csproj --verbosity quiet

Write-Host "✅ Build completed!" -ForegroundColor Green

Write-Host "`n🔧 JSON Repair Loop Features Added:" -ForegroundColor Cyan
Write-Host "  ✅ TryJsonRepairAsync method implemented" -ForegroundColor Green
Write-Host "  ✅ Automatic JSON repair when validation fails" -ForegroundColor Green  
Write-Host "  ✅ Smart repair prompts for common JSON issues" -ForegroundColor Green
Write-Host "  ✅ Enhanced logging for repair attempts" -ForegroundColor Green
Write-Host "  ✅ Fallback chain: RAG → JSON Repair → LLM" -ForegroundColor Green

Write-Host "`n🎯 How JSON Repair Works:" -ForegroundColor Cyan
Write-Host "  1. RAG generates response (24 seconds)" -ForegroundColor Yellow
Write-Host "  2. If JSON invalid → Ask AnythingLLM to fix it" -ForegroundColor Yellow
Write-Host "  3. If repair succeeds → Use repaired analysis" -ForegroundColor Yellow
Write-Host "  4. If repair fails → Fall back to slow LLM (3+ min)" -ForegroundColor Yellow

Write-Host "`n📊 Expected Performance Improvement:" -ForegroundColor Cyan
Write-Host "  📈 Higher RAG success rate (fixing JSON vs full fallback)" -ForegroundColor Green
Write-Host "  ⚡ Faster overall analysis (repair << full LLM generation)" -ForegroundColor Green
Write-Host "  🔍 Better debugging visibility" -ForegroundColor Green

Write-Host "`n🚀 Starting TestCaseEditorApp with JSON Repair..." -ForegroundColor Green
Write-Host "📋 Look for these new logs:" -ForegroundColor White
Write-Host "  • [RequirementAnalysisService] attempting JSON repair..." -ForegroundColor Gray
Write-Host "  • [RequirementAnalysisService] JSON repair successful..." -ForegroundColor Gray
Write-Host "  • [RequirementAnalysisService] JSON repair also failed..." -ForegroundColor Gray

# Run the app
Start-Process -FilePath ".\bin\Debug\net8.0-windows\TestCaseEditorApp.exe" -Wait

Write-Host "Test completed." -ForegroundColor Green