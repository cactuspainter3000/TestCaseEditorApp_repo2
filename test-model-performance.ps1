#!/usr/bin/env powershell

Write-Host "=== Model Performance Comparison Test ===" -ForegroundColor Cyan
Write-Host ""

# Simple prompt for consistent testing
$testPrompt = "Analyze this requirement: User wants filterable test list. Provide 3 key implementation points."

$models = @(
    @{ name = "phi3.5:3.8b-mini-instruct-q4_K_M"; display = "phi3.5 (mini)" },
    @{ name = "phi4-mini:3.8b-q4_K_M"; display = "phi4-mini" }
)

foreach ($model in $models) {
    Write-Host "Testing $($model.display) model..." -ForegroundColor Yellow
    
    $body = @{ 
        model = $model.name
        prompt = $testPrompt
        stream = $false 
    } | ConvertTo-Json
    
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    
    try {
        $response = Invoke-RestMethod -Uri "http://localhost:11434/api/generate" -Method Post -Body $body -ContentType "application/json"
        $sw.Stop()
        
        Write-Host "✅ $($model.display): $($sw.Elapsed.TotalSeconds) seconds" -ForegroundColor Green
        Write-Host "   Response length: $($response.response.Length) characters" -ForegroundColor Cyan
        Write-Host "   First 100 chars: $($response.response.Substring(0, [Math]::Min(100, $response.response.Length)))" -ForegroundColor White
        Write-Host ""
        
    } catch {
        $sw.Stop()
        Write-Host "❌ $($model.display) failed: $($_.Exception.Message)" -ForegroundColor Red
        Write-Host ""
    }
}

Write-Host "=== Performance Summary ===" -ForegroundColor Cyan