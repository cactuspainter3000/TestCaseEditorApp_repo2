#!/usr/bin/env pwsh

# Diagnostic script for LLM analysis navigation issue

Write-Host "=== Diagnosing LLM Analysis Navigation Issue ===" -ForegroundColor Cyan

Write-Host "`nISSUE: LLM analysis not updating during navigation" -ForegroundColor Red
Write-Host "WORKING: Details, supplemental info, tables update correctly" -ForegroundColor Green
Write-Host "NOT WORKING: LLM analysis stays static" -ForegroundColor Red

Write-Host "`nDEBUG CHANGES ADDED:" -ForegroundColor Yellow
Write-Host "1. Added console output to RefreshAnalysisDisplay method" -ForegroundColor Gray
Write-Host "2. Added console output to UpdateUIFromAnalysis method" -ForegroundColor Gray
Write-Host "3. Added logging to track requirement ID and analysis state" -ForegroundColor Gray

Write-Host "`nTEST INSTRUCTIONS:" -ForegroundColor Magenta
Write-Host "1. Launch the application" -ForegroundColor White
Write-Host "2. Open Requirements tab with multiple requirements" -ForegroundColor White
Write-Host "3. Run LLM analysis on one or more requirements" -ForegroundColor White
Write-Host "4. Navigate to LLM Analysis view" -ForegroundColor White
Write-Host "5. Use Previous/Next buttons to navigate between requirements" -ForegroundColor White
Write-Host "6. Watch console output for debug messages" -ForegroundColor White

Write-Host "`nEXPECTED DEBUG OUTPUT:" -ForegroundColor Cyan
Write-Host "*** [RequirementAnalysisVM] RefreshAnalysisDisplay: <REQ_ID>, HasAnalysis: <true/false> ***" -ForegroundColor Green
Write-Host "*** [RequirementAnalysisVM] UpdateUIFromAnalysis: Score=<N>, Issues=<N>, Improved=<true/false> ***" -ForegroundColor Green

Write-Host "`nIF NO DEBUG OUTPUT APPEARS:" -ForegroundColor Red
Write-Host "- RequirementAnalysisViewModel.CurrentRequirement is not being set" -ForegroundColor Yellow
Write-Host "- Navigation event chain is broken" -ForegroundColor Yellow
Write-Host "- Check Requirements_MainViewModel.OnRequirementSelected" -ForegroundColor Yellow

Write-Host "`nIF DEBUG OUTPUT APPEARS BUT UI DOESN'T UPDATE:" -ForegroundColor Red
Write-Host "- Property change notifications may not be working" -ForegroundColor Yellow
Write-Host "- UI binding issues in RequirementAnalysisView" -ForegroundColor Yellow
Write-Host "- ObservableProperty attributes missing or incorrect" -ForegroundColor Yellow

Write-Host "`nReady to Test Navigation!" -ForegroundColor Green