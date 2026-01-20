#!/usr/bin/env powershell
# Test script to verify LLM scoring fixes
# This script will help us see if the original requirement scores are realistic

Write-Host "=== Testing LLM Analysis Score Fix ===" -ForegroundColor Cyan
Write-Host "Purpose: Verify that original requirement scores are realistic (not perfect 100%)" -ForegroundColor Yellow
Write-Host ""

# Test requirement with obvious quality issues
$testRequirement = @"
The system should work good and be fast. Users need to do stuff quickly without problems. 
It must handle data and process things efficiently for all scenarios.
"@

Write-Host "Test Requirement (intentionally vague):" -ForegroundColor Magenta
Write-Host $testRequirement -ForegroundColor Gray
Write-Host ""

# Check if LLM service is available
$envProvider = $env:LLM_PROVIDER
if (-not $envProvider) { $envProvider = "ollama" }

Write-Host "LLM Provider: $envProvider" -ForegroundColor Green

if ($envProvider -eq "ollama") {
    Write-Host "Checking Ollama service..." -ForegroundColor Yellow
    try {
        $response = Invoke-RestMethod -Uri "http://localhost:11434/api/version" -Method GET -TimeoutSec 5
        Write-Host "✅ Ollama is running (version: $($response.version))" -ForegroundColor Green
    }
    catch {
        Write-Host "❌ Ollama service not available. Start with: ollama serve" -ForegroundColor Red
        Write-Host "Error: $($_.Exception.Message)" -ForegroundColor Red
        exit 1
    }

    # Check if model is available
    $model = $env:OLLAMA_MODEL
    if (-not $model) { $model = "phi4-mini:3.8b-q4_K_M" }
    
    Write-Host "Checking model: $model..." -ForegroundColor Yellow
    try {
        $models = Invoke-RestMethod -Uri "http://localhost:11434/api/tags" -Method GET
        $modelExists = $models.models | Where-Object { $_.name -eq $model }
        if ($modelExists) {
            Write-Host "✅ Model $model is available" -ForegroundColor Green
        } else {
            Write-Host "❌ Model $model not found. Available models:" -ForegroundColor Red
            $models.models | ForEach-Object { Write-Host "  - $($_.name)" -ForegroundColor Gray }
            exit 1
        }
    }
    catch {
        Write-Host "❌ Failed to check models: $($_.Exception.Message)" -ForegroundColor Red
        exit 1
    }
}

Write-Host ""
Write-Host "=== Instructions ===" -ForegroundColor Cyan
Write-Host "1. Open the TestCaseEditorApp" -ForegroundColor White
Write-Host "2. Create a new project or open an existing one" -ForegroundColor White
Write-Host "3. Navigate to Requirements" -ForegroundColor White
Write-Host "4. Add the test requirement above (copy from this output)" -ForegroundColor White
Write-Host "5. Run LLM Analysis" -ForegroundColor White
Write-Host "6. Check if the Original Quality Score is realistic (should be around 3-6/10, not 10/10)" -ForegroundColor White
Write-Host ""

Write-Host "Expected behavior:" -ForegroundColor Yellow
Write-Host "- Original Quality Score: 3-6 (reflects vague wording)" -ForegroundColor Gray
Write-Host "- Improved Quality Score: 7-10 (reflects LLM improvements)" -ForegroundColor Gray
Write-Host "- Analysis should provide specific improvement suggestions" -ForegroundColor Gray
Write-Host ""

Write-Host "If you still see 100% original scores, the LLM prompt fix needs more work." -ForegroundColor Red
Write-Host "Check logs for debug output showing score assignment details." -ForegroundColor Yellow