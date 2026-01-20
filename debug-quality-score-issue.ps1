#!/usr/bin/env pwsh

# Debug script to analyze the quality score issue

Write-Host "=== Quality Score Debug Investigation ===" -ForegroundColor Yellow

Write-Host "`nPROBLEM:" -ForegroundColor Red
Write-Host "Quality score showing 10/10 (100%) instead of the original requirement score" -ForegroundColor Red

Write-Host "`nLIKELY CAUSES:" -ForegroundColor Cyan
Write-Host "1. LLM provides improved requirement with its own score" -ForegroundColor Gray
Write-Host "2. Parser picks up improved score instead of original score" -ForegroundColor Gray
Write-Host "3. Multiple scores in response, parser gets the wrong one" -ForegroundColor Gray
Write-Host "4. Original score gets overwritten later in the process" -ForegroundColor Gray

Write-Host "`nDEBUGGING CHANGES ADDED:" -ForegroundColor Green
Write-Host "✅ Enhanced logging in RequirementAnalysisViewModel.UpdateUIFromAnalysis" -ForegroundColor White
Write-Host "✅ Warning-level logging in NaturalLanguageResponseParser for score parsing" -ForegroundColor White
Write-Host "✅ Debug output shows Original vs Improved vs Legacy scores" -ForegroundColor White

Write-Host "`nTEST STEPS:" -ForegroundColor Magenta
Write-Host "1. Run LLM analysis on a requirement" -ForegroundColor White
Write-Host "2. Check console output for score parsing warnings" -ForegroundColor White
Write-Host "3. Look for multiple quality scores in LLM response" -ForegroundColor White
Write-Host "4. Check if score is being assigned to wrong field" -ForegroundColor White

Write-Host "`nEXPECTED DEBUG OUTPUT:" -ForegroundColor Yellow
Write-Host "[NaturalLanguageParser] DEBUG SCORE PARSING - Setting OriginalQualityScore to {X}" -ForegroundColor Green
Write-Host "[RequirementAnalysisVM] DEBUG SCORE INVESTIGATION - OriginalQualityScore: {X}, ImprovedQualityScore: {Y}" -ForegroundColor Green

Write-Host "`nIF ORIGINAL SCORE IS 10:" -ForegroundColor Red
Write-Host "❌ LLM is rating the original requirement at 10/10 (which is unlikely)" -ForegroundColor Yellow
Write-Host "❌ Parser is picking up the improved score as original" -ForegroundColor Yellow  
Write-Host "❌ Analysis process has logical error in score assignment" -ForegroundColor Yellow

Write-Host "`nPOSSIBLE FIXES:" -ForegroundColor Cyan
Write-Host "1. Update LLM prompt to clearly separate original vs improved scores" -ForegroundColor White
Write-Host "2. Fix parser to only pick up first/original score" -ForegroundColor White
Write-Host "3. Add score validation logic to ensure realistic original scores" -ForegroundColor White
Write-Host "4. Implement separate parsing for improved requirement scores" -ForegroundColor White

Write-Host "`nRun analysis and check debug output to identify the issue!" -ForegroundColor Green