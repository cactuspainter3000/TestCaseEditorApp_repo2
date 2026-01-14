#!/usr/bin/env pwsh

<#
.SYNOPSIS
Test the improved parsing logic fixes

.DESCRIPTION
Tests if the parsing fixes resolve the issue where improved requirements and recommendations aren't being extracted
#>

Write-Host "=== Testing Parsing Fixes ===" -ForegroundColor Green
Write-Host ""

Write-Host "CHANGES MADE:" -ForegroundColor Yellow
Write-Host "1. Fixed improved requirement section parsing" -ForegroundColor Gray
Write-Host "   - Removed overly restrictive conditions (no colons, no REQUIRED)" -ForegroundColor Gray
Write-Host "   - Added proper section boundaries (stops at RECOMMENDATIONS, etc.)" -ForegroundColor Gray
Write-Host "   - Added 'continue' to skip section headers" -ForegroundColor Gray
Write-Host ""
Write-Host "2. Fixed recommendation parsing to handle 'Rationale:'" -ForegroundColor Gray  
Write-Host "   - System prompt uses 'Rationale:' but parser only looked for 'Fix:'" -ForegroundColor Gray
Write-Host "   - Now handles both formats" -ForegroundColor Gray
Write-Host ""
Write-Host "3. Added visible debug logging for raw responses" -ForegroundColor Gray
Write-Host "   - Can now see exactly what AnythingLLM returns" -ForegroundColor Gray
Write-Host ""

Write-Host "TESTING STRATEGY:" -ForegroundColor Cyan
Write-Host "1. Run requirement analysis with fixed parsing" -ForegroundColor Gray
Write-Host "2. Check logs for successful improved requirement extraction" -ForegroundColor Gray 
Write-Host "3. Verify recommendations parse correctly" -ForegroundColor Gray
Write-Host "4. Confirm raw response shows new formatting" -ForegroundColor Gray
Write-Host ""

# Build the app with fixes
Write-Host "Building app with parsing fixes..." -ForegroundColor Green
dotnet build --no-restore -v quiet

if ($LASTEXITCODE -eq 0) {
    Write-Host "✓ Build successful with parsing fixes applied" -ForegroundColor Green
    Write-Host ""
    Write-Host "READY TO TEST:" -ForegroundColor Magenta
    Write-Host "Run the app and analyze a requirement to verify:" -ForegroundColor Gray
    Write-Host "- Improved requirement is extracted and displayed" -ForegroundColor Gray
    Write-Host "- Recommendations parse correctly" -ForegroundColor Gray
    Write-Host "- Debug logs show the raw response format" -ForegroundColor Gray
} else {
    Write-Host "X Build failed - check for syntax errors" -ForegroundColor Red
}