#!/usr/bin/env pwsh

<#
.SYNOPSIS
Test implementation of clean analysis sessions for requirements

.DESCRIPTION
Demonstrates how to implement isolated requirement analysis sessions
#>

Write-Host "=== Clean Session Requirement Analysis Approach ===" -ForegroundColor Green
Write-Host ""

Write-Host "PROPOSED IMPLEMENTATION:" -ForegroundColor Yellow
Write-Host "1. Before each requirement analysis:" -ForegroundColor White
Write-Host "   - Clear workspace chat history (openAiHistory = 0)" -ForegroundColor Gray
Write-Host "   - Set fresh system prompt with requirement context" -ForegroundColor Gray
Write-Host "   - Send single analysis request" -ForegroundColor Gray
Write-Host ""
Write-Host "2. After each requirement analysis:" -ForegroundColor White
Write-Host "   - Extract results" -ForegroundColor Gray
Write-Host "   - Optional: Clear again for next requirement" -ForegroundColor Gray
Write-Host ""

Write-Host "BENEFITS:" -ForegroundColor Cyan
Write-Host "✅ Each requirement analyzed independently" -ForegroundColor Green
Write-Host "✅ No cross-contamination between analyses" -ForegroundColor Green
Write-Host "✅ Reproducible results" -ForegroundColor Green
Write-Host "✅ Better debugging and traceability" -ForegroundColor Green
Write-Host "✅ Consistent system prompt effectiveness" -ForegroundColor Green
Write-Host ""

Write-Host "IMPLEMENTATION APPROACH:" -ForegroundColor Magenta
Write-Host "Method 1: Clear before each analysis (current approach)" -ForegroundColor White
Write-Host "Method 2: Use separate workspace per requirement" -ForegroundColor White
Write-Host "Method 3: Enhanced payload with 'new session' indicator" -ForegroundColor White
Write-Host ""

Write-Host "RECOMMENDATION:" -ForegroundColor Yellow
Write-Host "Implement Method 1 with automatic clearing in RequirementAnalysisService" -ForegroundColor White
Write-Host "Add a 'ClearChatBeforeAnalysis' option for clean sessions" -ForegroundColor Gray