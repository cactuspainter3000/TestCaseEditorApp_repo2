#!/usr/bin/env powershell

Write-Host "=== RAG Performance Test ===" -ForegroundColor Cyan
Write-Host "Testing optimized Nicole workspace with phi3.5 model" -ForegroundColor Green
Write-Host ""

# Test the AnythingLLM API directly
$apiKey = "JVJGN9Q-HG4M4Q4-GW3CVWS-AC8XG1G"
$testPrompt = @"
Analyze the following software requirement and provide a comprehensive technical analysis:

REQUIREMENT: As a user, I want to be able to view all available tests in a filterable list so that I can quickly find specific tests.

Please provide:
1. Functional clarity assessment
2. Testability considerations
3. Potential edge cases
4. Implementation considerations
5. Acceptance criteria suggestions

Focus on technical depth and actionable insights for development teams.
"@

Write-Host "1. Testing AnythingLLM Nicole workspace performance..." -ForegroundColor Yellow
Write-Host "   Prompt length: $($testPrompt.Length) characters" -ForegroundColor Cyan

$headers = @{ 
    "Authorization" = "Bearer $apiKey"
    "Content-Type" = "application/json" 
}

$body = @{ 
    message = $testPrompt
    mode = "chat" 
} | ConvertTo-Json

$sw = [System.Diagnostics.Stopwatch]::StartNew()

try {
    $response = Invoke-RestMethod -Uri "http://localhost:3001/api/v1/workspace/Nicole/chat" -Method Post -Headers $headers -Body $body -TimeoutSec 120
    $sw.Stop()
    
    Write-Host "✅ RAG Analysis completed successfully!" -ForegroundColor Green
    Write-Host "   Response time: $($sw.Elapsed.TotalSeconds) seconds" -ForegroundColor Yellow
    Write-Host "   Response length: $($response.textResponse.Length) characters" -ForegroundColor Cyan
    Write-Host ""
    
    Write-Host "2. Response Preview:" -ForegroundColor Yellow
    $preview = $response.textResponse.Substring(0, [Math]::Min(300, $response.textResponse.Length))
    Write-Host "   $preview..." -ForegroundColor White
    Write-Host ""
    
    # Compare to baseline
    Write-Host "3. Performance Comparison:" -ForegroundColor Yellow
    Write-Host "   Previous baseline (phi4-mini): ~67.8 seconds" -ForegroundColor White
    Write-Host "   Current result (phi3.5): $($sw.Elapsed.TotalSeconds) seconds" -ForegroundColor Green
    
    if ($sw.Elapsed.TotalSeconds -lt 67.8) {
        $improvement = [math]::Round(((67.8 - $sw.Elapsed.TotalSeconds) / 67.8) * 100, 1)
        Write-Host "   ✅ Performance improvement: $improvement%" -ForegroundColor Green
    }
    
} catch {
    $sw.Stop()
    Write-Host "❌ RAG test failed after $($sw.Elapsed.TotalSeconds) seconds" -ForegroundColor Red
    Write-Host "   Error: $($_.Exception.Message)" -ForegroundColor Red
    
    # Additional debugging
    Write-Host ""
    Write-Host "4. Debugging Information:" -ForegroundColor Yellow
    try {
        $systemInfo = Invoke-RestMethod -Uri "http://localhost:3001/api/v1/system" -Headers @{ "Authorization" = "Bearer $apiKey" }
        Write-Host "   Current model: $($systemInfo.settings.OllamaLLMModelPref)" -ForegroundColor Cyan
    } catch {
        Write-Host "   Could not retrieve system info" -ForegroundColor Red
    }
}

Write-Host ""
Write-Host "=== Test Complete ===" -ForegroundColor Cyan