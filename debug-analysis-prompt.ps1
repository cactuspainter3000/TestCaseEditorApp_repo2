#!/usr/bin/env pwsh
# Debug script to inspect the exact prompt being sent to LLM for analysis

Write-Host "=== LLM Analysis Debug Tool ===" -ForegroundColor Cyan
Write-Host "This script helps debug why LLM analysis takes too long or misses supplemental info" -ForegroundColor Yellow
Write-Host

# Check if app is running
$process = Get-Process -Name "TestCaseEditorApp" -ErrorAction SilentlyContinue
if ($null -eq $process) {
    Write-Host "❌ TestCaseEditorApp is not running" -ForegroundColor Red
    Write-Host "Please start the application and load a requirement for analysis" -ForegroundColor Yellow
    exit 1
}

Write-Host "✅ TestCaseEditorApp is running" -ForegroundColor Green

# Instructions for debugging
Write-Host "`n=== How to Debug Analysis Issues ===" -ForegroundColor Cyan
Write-Host "1. In the app, open the requirement with analysis issues"
Write-Host "2. Go to the Analysis tab (LLM Analysis)"
Write-Host "3. Open the browser developer tools (F12)"
Write-Host "4. In the Console, call: window.inspectAnalysisPrompt()"
Write-Host "5. Check the prompt includes 'SUPPLEMENTAL PARAGRAPHS:' section"
Write-Host "6. For performance, check if system prompt is cached"
Write-Host

Write-Host "=== Performance Optimization Tips ===" -ForegroundColor Green
Write-Host "• Ensure LLM service is local (Ollama on localhost:11434)"
Write-Host "• Check if supplemental info is properly formatted"
Write-Host "• Verify model is loaded (phi4-mini recommended)"
Write-Host "• Enable caching for repeated analysis"
Write-Host

Write-Host "=== Common Issues ===" -ForegroundColor Yellow
Write-Host "❌ Slow performance → Check LLM service response time"
Write-Host "❌ Missing definitions → Verify supplemental content is passed"
Write-Host "❌ Incorrect flagging → Check prompt instruction clarity"
Write-Host

# Check if Ollama service is running
try {
    $ollamaResponse = Invoke-RestMethod -Uri "http://localhost:11434/api/tags" -Method GET -TimeoutSec 3 -ErrorAction Stop
    Write-Host "✅ Ollama service is running" -ForegroundColor Green
    
    $models = $ollamaResponse.models
    if ($models -and $models.Count -gt 0) {
        Write-Host "✅ Available models:" -ForegroundColor Green
        foreach ($model in $models) {
            Write-Host "   - $($model.name)" -ForegroundColor White
        }
    } else {
        Write-Host "⚠️  No models found in Ollama" -ForegroundColor Yellow
    }
}
catch {
    Write-Host "❌ Ollama service not reachable at localhost:11434" -ForegroundColor Red
    Write-Host "This might explain slow performance - check LLM service configuration" -ForegroundColor Yellow
}

Write-Host "`nPress any key to exit..."
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")