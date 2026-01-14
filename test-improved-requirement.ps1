#!/usr/bin/env pwsh

<#
.SYNOPSIS
Test the enhanced LLM analysis with IMPROVED REQUIREMENT output

.DESCRIPTION
Tests that the LLM properly provides a rewritten requirement in the new IMPROVED REQUIREMENT section.
This is the key deliverable from the analysis - not just identifying problems but providing an improved version.
#>

Write-Host "=== Testing Enhanced Requirement Analysis with Improved Requirement Output ===" -ForegroundColor Green
Write-Host "This test verifies that the LLM provides a complete rewritten requirement addressing identified issues." -ForegroundColor Gray
Write-Host ""

# Test requirement with issues that need fixing
$testRequirement = "The Test System shall be capable of performing Tier 1 Boundary Scan coverage of the UUT."

Write-Host "Test Requirement:" -ForegroundColor Cyan
Write-Host "  $testRequirement"
Write-Host ""

Write-Host "Expected LLM Behavior:" -ForegroundColor Yellow
Write-Host "1. Identify issues (e.g., undefined 'Tier 1', unclear 'UUT')" -ForegroundColor Gray
Write-Host "2. Provide IMPROVED REQUIREMENT section with complete rewrite" -ForegroundColor Gray  
Write-Host "3. Explain rationale in RECOMMENDATIONS section" -ForegroundColor Gray
Write-Host ""

# Start the application for manual testing
Write-Host "Starting Test Case Editor App for manual verification..." -ForegroundColor Green
Write-Host "1. Load a requirement with the test text above" -ForegroundColor Gray
Write-Host "2. Run LLM analysis" -ForegroundColor Gray
Write-Host "3. Check that 'IMPROVED REQUIREMENT' appears in the response" -ForegroundColor Gray
Write-Host "4. Verify logging shows 'ImprovedReq=True'" -ForegroundColor Gray

# Check if AnythingLLM is running
$process = Get-Process -Name "AnythingLLM" -ErrorAction SilentlyContinue
if ($process) {
    Write-Host "✅ AnythingLLM is running (PID: $($process.Id))" -ForegroundColor Green
} else {
    Write-Host "⚠️ AnythingLLM not detected - may need to start it" -ForegroundColor Yellow
}

# Build and start the app
Write-Host ""
Write-Host "Building application..." -ForegroundColor Gray
dotnet build --configuration Debug --verbosity quiet

if ($LASTEXITCODE -eq 0) {
    Write-Host "✅ Build successful" -ForegroundColor Green
    Write-Host ""
    Write-Host "Starting application..." -ForegroundColor Gray
    Start-Process ".\bin\Debug\net8.0-windows\TestCaseEditorApp.exe"
    Write-Host "Application started. Test the requirement analysis manually." -ForegroundColor Green
} else {
    Write-Host "❌ Build failed" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "=== Manual Test Instructions ===" -ForegroundColor Cyan
Write-Host "1. Create a new project or open an existing one"
Write-Host "2. Add a requirement: '$testRequirement'"
Write-Host "3. Run analysis and check for these sections in the response:"
Write-Host "   - IMPROVED REQUIREMENT: [Complete rewritten requirement]"
Write-Host "   - RECOMMENDATIONS: [Rationale for changes]"
Write-Host "   - HALLUCINATION CHECK: NO_FABRICATION"
Write-Host "4. Check logs for: 'ImprovedReq=True' in parsing output"
Write-Host ""
Write-Host "The analysis should provide a clear, testable, complete requirement rewrite!" -ForegroundColor Green