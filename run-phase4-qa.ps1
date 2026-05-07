#!/usr/bin/env pwsh
# Phase 4 Integration Quality Assurance Test Runner
# Runs comprehensive integration tests for all Phase 4 services

param(
    [switch]$StopOnFailure = $false,
    [switch]$Verbose = $false
)

Write-Host "Phase 4 Integration Quality Assurance" -ForegroundColor Cyan
Write-Host "=====================================" -ForegroundColor Cyan
Write-Host ""

# Set error action preference
$ErrorActionPreference = if ($StopOnFailure) { "Stop" } else { "Continue" }

try {
    # Ensure we're in the correct directory
    $projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
    Set-Location $projectRoot

    Write-Host "Step 1: Building project..." -ForegroundColor Yellow
    $buildResult = dotnet build TestCaseEditorApp.csproj --configuration Release --verbosity minimal
    
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Build failed! Cannot proceed with integration tests." -ForegroundColor Red
        exit 1
    }
    
    Write-Host "Build successful" -ForegroundColor Green
    Write-Host ""

    Write-Host "Step 2: Running Phase 4 Integration Quality Assurance Tests..." -ForegroundColor Yellow
    Write-Host ""
    
    # Run the Phase 4 QA console app
    $runArgs = @(
        "run"
        "Tests/Phase4QAConsoleApp.cs"
        "--project", "TestCaseEditorApp.csproj"
        "--configuration", "Release"
    )

    if ($Verbose) {
        $runArgs += "--verbosity", "detailed"
    } else {
        $runArgs += "--verbosity", "minimal"
    }

    $testOutput = & dotnet @runArgs

    if ($LASTEXITCODE -eq 0) {
        Write-Host ""
        Write-Host "ALL PHASE 4 INTEGRATION TESTS PASSED!" -ForegroundColor Green
        Write-Host "Phase 4 services are properly integrated and ready for production." -ForegroundColor Green
        $success = $true
    } elseif ($LASTEXITCODE -eq 1) {
        Write-Host ""
        Write-Host "SOME INTEGRATION TESTS FAILED" -ForegroundColor Red
        Write-Host "Review the test output above to identify and fix issues." -ForegroundColor Red
        $success = $false
    } else {
        Write-Host ""
        Write-Host "CRITICAL ERROR IN TEST EXECUTION" -ForegroundColor Red
        Write-Host "Exit code: $LASTEXITCODE" -ForegroundColor Red
        $success = $false
    }

    Write-Host ""
    Write-Host "Test Summary:" -ForegroundColor Cyan
    Write-Host "   - Service Dependencies: " -NoNewline -ForegroundColor Cyan
    if ($success) { 
        Write-Host "VALIDATED" -ForegroundColor Green 
    } else { 
        Write-Host "ISSUES DETECTED" -ForegroundColor Red 
    }
    
    Write-Host "   - End-to-End Workflows: " -NoNewline -ForegroundColor Cyan
    if ($success) { 
        Write-Host "WORKING" -ForegroundColor Green 
    } else { 
        Write-Host "ISSUES DETECTED" -ForegroundColor Red 
    }
    
    Write-Host "   - Performance: " -NoNewline -ForegroundColor Cyan
    if ($success) { 
        Write-Host "ACCEPTABLE" -ForegroundColor Green 
    } else { 
        Write-Host "ISSUES DETECTED" -ForegroundColor Red 
    }
    
    Write-Host "   - Error Handling: " -NoNewline -ForegroundColor Cyan
    if ($success) { 
        Write-Host "ROBUST" -ForegroundColor Green 
    } else { 
        Write-Host "ISSUES DETECTED" -ForegroundColor Red 
    }
    
    Write-Host "   - Service Integration: " -NoNewline -ForegroundColor Cyan
    if ($success) { 
        Write-Host "QUALITY ASSURED" -ForegroundColor Green 
    } else { 
        Write-Host "ISSUES DETECTED" -ForegroundColor Red 
    }
    
    Write-Host ""

    if ($success) {
        Write-Host "Phase 4 Integration Quality Assurance: COMPLETE" -ForegroundColor Green
        Write-Host "Ready to proceed to Phase 5: Testing & Deployment" -ForegroundColor Green
        exit 0
    } else {
        Write-Host "Phase 4 Integration Quality Assurance: NEEDS ATTENTION" -ForegroundColor Yellow
        Write-Host "Fix identified issues before proceeding to Phase 5" -ForegroundColor Yellow
        exit 1
    }
}
catch {
    Write-Host ""
    Write-Host "SCRIPT ERROR: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "Stack Trace: $($_.ScriptStackTrace)" -ForegroundColor Red
    exit 2
}
finally {
    Write-Host ""
    Write-Host "Phase 4 Integration Quality Assurance test runner completed." -ForegroundColor Gray
}