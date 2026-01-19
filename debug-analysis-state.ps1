#!/usr/bin/env pwsh
# Quick fix to clear stale analysis error messages

Write-Host "=== Analysis Error State Reset ===" -ForegroundColor Cyan
Write-Host "This script provides steps to reset any stale analysis error states" -ForegroundColor Yellow
Write-Host

Write-Host "Quick Solution Steps:" -ForegroundColor Green
Write-Host "1. Navigate to a different requirement (click on another req in navigation)" -ForegroundColor White
Write-Host "2. Navigate back to the failing requirement" -ForegroundColor White
Write-Host "3. Check if the 'Analyze Now' button appears" -ForegroundColor White
Write-Host

Write-Host "Alternative approach:" -ForegroundColor Green
Write-Host "1. Try switching to the 'Requirements' workspace from the side menu" -ForegroundColor White
Write-Host "   (look for 'Requirements' vs 'Test Case Generator')" -ForegroundColor White
Write-Host "2. The fix I applied was specifically for Requirements workspace" -ForegroundColor White
Write-Host

Write-Host "Root cause analysis:" -ForegroundColor Yellow
Write-Host "- You're viewing Test Case Generator workspace (legacy)" -ForegroundColor White
Write-Host "- This uses TestCaseGenerator_AnalysisVM which has stale error state" -ForegroundColor White
Write-Host "- The 'Analysis Failed' message is blocking the 'Analyze Now' button" -ForegroundColor White
Write-Host "- Need to clear the AnalysisStatusMessage property" -ForegroundColor White
Write-Host

Write-Host "What I can see from the screenshot:" -ForegroundColor Magenta
Write-Host "- Current workspace: 'Test Case Generator'" -ForegroundColor White
Write-Host "- LLM Analysis tab shows: 'Analysis Failed'" -ForegroundColor White
Write-Host "- Expected: 'No analysis available' + 'Analyze Now' button" -ForegroundColor White