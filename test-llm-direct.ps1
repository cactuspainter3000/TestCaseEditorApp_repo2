#!/usr/bin/env powershell
# Direct test of LLM analysis to verify prompt and parsing fixes

Write-Host "=== Direct LLM Analysis Test ===" -ForegroundColor Cyan
Write-Host "Testing the actual LLM prompt and response parsing" -ForegroundColor Yellow
Write-Host ""

# Test requirement with obvious quality issues that should get low score
$testPrompt = @"
🚨 CORRECTIVE ANALYSIS - AVOID FABRICATION 🚨

Your previous analysis contained fabricated technical details not present in the original requirement.
Please analyze this requirement again, but this time:

**STRICT RULES:**
- Use ONLY information explicitly stated in the requirement
- Use ONLY definitions from uploaded supplemental materials (if any)
- Do NOT mention IEEE standards, ISO standards, or specific technical protocols unless they appear in the requirement text
- Do NOT invent definitions for technical terms like 'Tier 1/2/3' unless provided in supplemental materials
- When you don't have enough information, suggest asking for clarification instead of inventing details

**Requirement ID:** TEST-001
**Name:** System Performance
**Description:** The system should work good and be fast. Users need to do stuff quickly without problems. It must handle data and process things efficiently for all scenarios.

Format your response exactly as follows:

**ORIGINAL REQUIREMENT QUALITY SCORE:** [1-10]
(Rate the user's original requirement text, not any improved version you suggest)

**ISSUES FOUND:**
- [List specific problems with this requirement]
- [Focus on clarity, testability, completeness issues]

**RECOMMENDATIONS:**
- **Category:** [Issue type] | **Description:** [What to fix] | **Suggested Edit:** [Rewritten requirement text]

**HALLUCINATION CHECK:**
- You MUST respond with 'NO_FABRICATION' for this corrective attempt
- If you still need to invent details, respond with 'FABRICATED_DETAILS' and we'll try a different approach

Remember: It's better to have fewer, accurate recommendations than many fabricated ones!
"@

# Check if we can reach the LLM directly
$envProvider = $env:LLM_PROVIDER
if (-not $envProvider) { $envProvider = "ollama" }

if ($envProvider -eq "ollama") {
    Write-Host "Testing direct Ollama call..." -ForegroundColor Yellow
    
    $model = $env:OLLAMA_MODEL
    if (-not $model) { $model = "phi4-mini:3.8b-q4_K_M" }
    
    try {
        $requestBody = @{
            model = $model
            prompt = $testPrompt
            stream = $false
        } | ConvertTo-Json
        
        Write-Host "Sending request to Ollama..." -ForegroundColor Green
        $response = Invoke-RestMethod -Uri "http://localhost:11434/api/generate" -Method POST -Body $requestBody -ContentType "application/json" -TimeoutSec 60
        
        Write-Host ""
        Write-Host "=== LLM Response ===" -ForegroundColor Cyan
        Write-Host $response.response -ForegroundColor White
        Write-Host ""
        
        # Test our parser patterns on this response
        Write-Host "=== Testing Parser Patterns ===" -ForegroundColor Cyan
        
        $responseText = $response.response
        
        # Test the regex patterns we use in the parser
        $originalScorePattern = "\*\*ORIGINAL\s+REQUIREMENT\s+QUALITY\s+SCORE:\*\*\s*(\d+)"
        $qualityScorePattern = "\*\*QUALITY\s+SCORE:\*\*\s*(\d+)"
        
        if ($responseText -match $originalScorePattern) {
            Write-Host "✅ Found ORIGINAL REQUIREMENT QUALITY SCORE: $($Matches[1])" -ForegroundColor Green
        } elseif ($responseText -match $qualityScorePattern) {
            Write-Host "⚠️ Found generic QUALITY SCORE: $($Matches[1])" -ForegroundColor Yellow
        } else {
            Write-Host "❌ No quality score pattern found" -ForegroundColor Red
        }
        
        # Check what the score actually is
        if ($responseText -match "\*\*(ORIGINAL\s+REQUIREMENT\s+)?QUALITY\s+SCORE:\*\*\s*(\d+)") {
            $score = [int]$Matches[2]
            Write-Host ""
            if ($score -le 6) {
                Write-Host "✅ Score is realistic for poor requirement: $score/10" -ForegroundColor Green
            } else {
                Write-Host "❌ Score is too high for poor requirement: $score/10" -ForegroundColor Red
                Write-Host "This suggests the LLM is still rating its own improvements, not the original" -ForegroundColor Yellow
            }
        }
        
    }
    catch {
        Write-Host "❌ Error calling Ollama: $($_.Exception.Message)" -ForegroundColor Red
    }
} else {
    Write-Host "This test only supports Ollama provider. Set LLM_PROVIDER=ollama to test." -ForegroundColor Red
}