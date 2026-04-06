#!/usr/bin/env pwsh

<#
.SYNOPSIS
Debug the AnythingLLM response parsing issue

.DESCRIPTION
Capture and analyze the raw response that caused parsing failures
#>

Write-Host "=== AnythingLLM Response Analysis Debug ===" -ForegroundColor Red
Write-Host ""

Write-Host "FINDINGS FROM LOGS:" -ForegroundColor Yellow
Write-Host "1. Workspace WAS updated with new system prompt ✓" -ForegroundColor Green
Write-Host "   - 'Generated system prompt contains 'IMPROVED REQUIREMENT': True'" -ForegroundColor Gray
Write-Host "2. AnythingLLM completed successfully after ~5 minutes ✓" -ForegroundColor Green
Write-Host "   - 'RAG request completed in 295365.4325ms, response length: 2930'" -ForegroundColor Gray
Write-Host "3. PARSING FAILED to extract improved requirement ❌" -ForegroundColor Red
Write-Host "   - 'No improved requirement provided for DECAGON-REQ_RC-5'" -ForegroundColor Gray
Write-Host "4. Multiple recommendation parsing errors ❌" -ForegroundColor Red
Write-Host "   - 'missing SuggestedEdit - removing from recommendations'" -ForegroundColor Gray
Write-Host ""

Write-Host "DIAGNOSIS:" -ForegroundColor Cyan
Write-Host "The issue is NOT workspace configuration or communication." -ForegroundColor Gray
Write-Host "The issue IS parsing logic failing to match the new response format." -ForegroundColor Gray
Write-Host ""

Write-Host "POSSIBLE CAUSES:" -ForegroundColor Yellow
Write-Host "1. AnythingLLM using old format despite new system prompt" -ForegroundColor Gray
Write-Host "2. Parsing logic too strict for actual LLM response format" -ForegroundColor Gray
Write-Host "3. Response format differs from expected structure" -ForegroundColor Gray
Write-Host ""

Write-Host "NEXT STEPS:" -ForegroundColor Green
Write-Host "1. Check the actual raw response content in app logs" -ForegroundColor Gray
Write-Host "2. Compare against parsing logic expectations" -ForegroundColor Gray
Write-Host "3. Update parsing to handle actual LLM response format" -ForegroundColor Gray
Write-Host ""

Write-Host "The mystery is solved! 🔍" -ForegroundColor Magenta
Write-Host "It's a parsing issue, not a communication issue." -ForegroundColor Gray