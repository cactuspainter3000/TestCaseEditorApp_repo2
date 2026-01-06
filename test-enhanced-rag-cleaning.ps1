#!/usr/bin/env pwsh
# Test the enhanced RAG JSON cleaning performance

Write-Host "🚀 Testing Enhanced RAG JSON Cleaning..." -ForegroundColor Green

# Build first
Write-Host "Building project..." -ForegroundColor Cyan
dotnet build TestCaseEditorApp.csproj --verbosity quiet

if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ Build failed!" -ForegroundColor Red
    exit 1
}

Write-Host "✅ Build successful!" -ForegroundColor Green

# Check services status
Write-Host "`n📊 Checking service status..." -ForegroundColor Cyan

# Check Ollama
try {
    $response = Invoke-RestMethod -Uri "http://localhost:11434/api/tags" -TimeoutSec 5
    Write-Host "✅ Ollama running with models: $($response.models.name -join ', ')" -ForegroundColor Green
} catch {
    Write-Host "⚠️ Ollama not available" -ForegroundColor Yellow
}

# Check AnythingLLM
try {
    $anythingResponse = Invoke-RestMethod -Uri "http://localhost:3001/api/v1/workspaces" -TimeoutSec 5
    $workspaceCount = $anythingResponse.workspaces.Count
    Write-Host "✅ AnythingLLM running with $workspaceCount workspaces" -ForegroundColor Green
} catch {
    Write-Host "⚠️ AnythingLLM not available" -ForegroundColor Yellow
}

Write-Host "`n🔧 Enhancements Applied:" -ForegroundColor Cyan
Write-Host "  ✅ Enhanced JSON cleaning with regex patterns" -ForegroundColor Green
Write-Host "  ✅ Smart quote replacement and character encoding fixes" -ForegroundColor Green  
Write-Host "  ✅ Trailing comma removal and duplicate comma cleanup" -ForegroundColor Green
Write-Host "  ✅ Added detailed RAG response debugging logs" -ForegroundColor Green

Write-Host "`n🎯 Expected Improvements:" -ForegroundColor Cyan
Write-Host "  📈 RAG JSON parsing should succeed more often" -ForegroundColor Yellow
Write-Host "  ⚡ Less fallback to slow 3+ minute LLM analysis" -ForegroundColor Yellow
Write-Host "  🔍 Better debugging info in console logs" -ForegroundColor Yellow

Write-Host "`n🚀 Starting TestCaseEditorApp with enhanced RAG..." -ForegroundColor Green
Write-Host "📋 Test Instructions:" -ForegroundColor White
Write-Host "  1. Import some requirements" -ForegroundColor Gray
Write-Host "  2. Click analyze on any requirement" -ForegroundColor Gray  
Write-Host "  3. Watch console for enhanced RAG debugging:" -ForegroundColor Gray
Write-Host "     • Raw RAG response" -ForegroundColor DarkGray
Write-Host "     • Cleaned RAG JSON" -ForegroundColor DarkGray
Write-Host "     • JSON validation results" -ForegroundColor DarkGray
Write-Host "  4. Analysis should complete faster with fewer fallbacks!" -ForegroundColor Gray

Write-Host "`n⭐ Look for these improved logs:" -ForegroundColor Yellow
Write-Host "  • [RequirementAnalysisService] Raw RAG response..." -ForegroundColor DarkYellow
Write-Host "  • [RequirementAnalysisService] Cleaned RAG JSON..." -ForegroundColor DarkYellow
Write-Host "  • [RequirementAnalysisService] RAG analysis successful..." -ForegroundColor DarkYellow
Write-Host ""

# Run the app  
Start-Process -FilePath ".\bin\Debug\net8.0-windows\TestCaseEditorApp.exe" -Wait

Write-Host "Test completed." -ForegroundColor Green