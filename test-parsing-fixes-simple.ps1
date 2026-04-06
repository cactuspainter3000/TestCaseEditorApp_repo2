#!/usr/bin/env pwsh

Write-Host "=== Testing Parsing Fixes ===" -ForegroundColor Green
Write-Host ""

Write-Host "CHANGES MADE:" -ForegroundColor Yellow
Write-Host "1. Fixed improved requirement section parsing" -ForegroundColor Gray
Write-Host "2. Fixed recommendation parsing to handle 'Rationale:'" -ForegroundColor Gray  
Write-Host "3. Added visible debug logging for raw responses" -ForegroundColor Gray
Write-Host ""

# Build the app with fixes
Write-Host "Building app with parsing fixes..." -ForegroundColor Green
dotnet build --no-restore -v quiet

if ($LASTEXITCODE -eq 0) {
    Write-Host "Build successful with parsing fixes applied" -ForegroundColor Green
    Write-Host ""
    Write-Host "READY TO TEST:" -ForegroundColor Magenta
    Write-Host "Run the app and analyze a requirement to verify the fixes work" -ForegroundColor Gray
} else {
    Write-Host "Build failed - check for syntax errors" -ForegroundColor Red
}