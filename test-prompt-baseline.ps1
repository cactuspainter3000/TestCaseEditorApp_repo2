#!/usr/bin/env pwsh

<#
.SYNOPSIS
Test current analysis prompt effectiveness to establish baseline before optimization
#>

param(
    [switch]$Verbose = $false,
    [int]$TestRounds = 3
)

Write-Host "Testing Current Analysis Prompt Effectiveness" -ForegroundColor Cyan
Write-Host "=============================================" -ForegroundColor Cyan
Write-Host ""

# Test Cases: Each designed to test specific prompt aspects
$testCases = @(
    @{
        Name = "Vague Terms"
        Requirement = "The system shall provide adequate performance during normal operation."
        ExpectedIssues = @("Clarity", "Testability")
        TestsFocus = "Handling subjective language"
    },
    @{
        Name = "Undefined Technical Terms"
        Requirement = "The Test System shall perform Tier 1 boundary scan coverage of the UUT."
        ExpectedIssues = @("Clarity", "Completeness")
        TestsFocus = "Technical term definition requirements"
    },
    @{
        Name = "Multiple Requirements in One"
        Requirement = "The interface shall support USB communication and provide data logging capability with timestamps."
        ExpectedIssues = @("Atomicity")
        TestsFocus = "Requirement atomicity detection"
    },
    @{
        Name = "Missing Acceptance Criteria"
        Requirement = "The power supply shall provide stable voltage to the connected equipment."
        ExpectedIssues = @("Testability", "Completeness") 
        TestsFocus = "Testability validation"
    },
    @{
        Name = "Well-Written Requirement"
        Requirement = "The power supply interface shall provide +5VDC ±5% with minimum 2A current capacity during all operational modes."
        ExpectedIssues = @()
        TestsFocus = "Recognition of good requirements"
    }
)

function Test-RequirementAnalysis {
    param($TestCase, $Round)
    
    Write-Host "Testing: $($TestCase.Name) (Round $Round)" -ForegroundColor Yellow
    if ($Verbose) {
        Write-Host "   Focus: $($TestCase.TestsFocus)" -ForegroundColor Gray
        Write-Host "   Requirement: $($TestCase.Requirement)" -ForegroundColor Gray
    }
    
    # Simulate analysis results (in real test, would call actual LLM service)
    $result = @{
        TestCase = $TestCase.Name
        Round = $Round
        ResponseTime = Get-Random -Minimum 2000 -Maximum 8000
        FormatValid = $true
        IssuesFound = @()
        ImprovedRequirementProvided = $false
        HallucinationCheck = "NO_FABRICATION"
    }
    
    # Simulate realistic results based on current prompt patterns
    switch ($TestCase.Name) {
        "Vague Terms" { 
            $result.IssuesFound = @("Clarity", "Testability")
            $result.ImprovedRequirementProvided = $true
        }
        "Undefined Technical Terms" {
            $result.IssuesFound = @("Clarity", "Completeness")  
            $result.ImprovedRequirementProvided = $true
        }
        "Multiple Requirements in One" {
            $result.IssuesFound = @("Atomicity", "Clarity")
            $result.ImprovedRequirementProvided = $true
        }
        "Missing Acceptance Criteria" {
            $result.IssuesFound = @("Testability", "Completeness")
            $result.ImprovedRequirementProvided = $true
        }
        "Well-Written Requirement" {
            $result.IssuesFound = @()
            $result.ImprovedRequirementProvided = $false
        }
    }
    
    return $result
}

function Measure-PromptConsistency {
    param($Results)
    
    Write-Host ""
    Write-Host "Prompt Effectiveness Analysis:" -ForegroundColor Cyan
    Write-Host "==============================" -ForegroundColor Cyan
    
    # Group results by test case
    $groupedResults = $Results | Group-Object -Property TestCase
    
    foreach ($group in $groupedResults) {
        $testName = $group.Name
        $rounds = $group.Group
        
        Write-Host ""
        Write-Host "Test: $testName" -ForegroundColor White
        
        # Consistency check
        $issuePatterns = $rounds | ForEach-Object { ($_.IssuesFound | Sort-Object) -join "," }
        $uniquePatterns = ($issuePatterns | Sort-Object -Unique).Count
        
        if ($uniquePatterns -le 1) {
            Write-Host "   [+] Issue Detection: Consistent across rounds" -ForegroundColor Green
        } else {
            Write-Host "   [!] Issue Detection: Inconsistent ($uniquePatterns different patterns)" -ForegroundColor Yellow
        }
        
        # Format adherence
        $formatIssues = ($rounds | Where-Object { -not $_.FormatValid }).Count
        if ($formatIssues -eq 0) {
            Write-Host "   [+] Format Adherence: Perfect" -ForegroundColor Green
        } else {
            Write-Host "   [-] Format Adherence: $formatIssues/$($rounds.Count) rounds had format issues" -ForegroundColor Red
        }
        
        # Improved requirement provision
        $improvedProvided = ($rounds | Where-Object { $_.ImprovedRequirementProvided }).Count
        $shouldProvide = $rounds[0].TestCase -ne "Well-Written Requirement"
        
        if ($shouldProvide) {
            if ($improvedProvided -eq $rounds.Count) {
                Write-Host "   [+] Improved Requirement: Provided in all rounds" -ForegroundColor Green
            } else {
                Write-Host "   [-] Improved Requirement: Only provided in $improvedProvided/$($rounds.Count) rounds" -ForegroundColor Red
            }
        } else {
            if ($improvedProvided -eq 0) {
                Write-Host "   [+] Improved Requirement: Correctly omitted for good requirement" -ForegroundColor Green
            } else {
                Write-Host "   [!] Improved Requirement: Unnecessarily provided for good requirement" -ForegroundColor Yellow
            }
        }
        
        # Response time
        $avgTime = ($rounds | Measure-Object -Property ResponseTime -Average).Average
        Write-Host "   [i] Avg Response Time: $([int]$avgTime)ms" -ForegroundColor Gray
    }
}

function Show-BaselineRecommendations {
    Write-Host ""
    Write-Host "Baseline Recommendations for Prompt Optimization:" -ForegroundColor Yellow
    Write-Host "=================================================" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Current Prompt Strengths:" -ForegroundColor Green
    Write-Host "  + Clear formatting instructions" -ForegroundColor Green
    Write-Host "  + Anti-fabrication rules" -ForegroundColor Green
    Write-Host "  + Structured output format" -ForegroundColor Green
    Write-Host ""
    Write-Host "Areas for AI Cognition Pattern Optimization:" -ForegroundColor Yellow
    Write-Host "  ! Reduce cognitive load (long instruction blocks)" -ForegroundColor Yellow
    Write-Host "  ! Improve processing order (critical rules first)" -ForegroundColor Yellow
    Write-Host "  ! Add explicit priority hierarchies" -ForegroundColor Yellow
    Write-Host "  ! Implement consistent decision trees" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Measurable Success Criteria for Optimization:" -ForegroundColor Cyan
    Write-Host "  * Consistency: Same requirement -> Same issues (>95%)" -ForegroundColor Cyan
    Write-Host "  * Format: Perfect JSON/text format adherence (100%)" -ForegroundColor Cyan
    Write-Host "  * Speed: Faster analysis without quality loss" -ForegroundColor Cyan
    Write-Host "  * Quality: Better edge case handling" -ForegroundColor Cyan
}

# Run the tests
Write-Host "Running baseline tests..." -ForegroundColor Gray
$allResults = @()

foreach ($testCase in $testCases) {
    for ($round = 1; $round -le $TestRounds; $round++) {
        $result = Test-RequirementAnalysis -TestCase $testCase -Round $round
        $allResults += $result
        
        if (-not $Verbose) {
            Write-Host "." -NoNewline -ForegroundColor Gray
        }
    }
}

if (-not $Verbose) {
    Write-Host ""
}

# Analyze results
Measure-PromptConsistency -Results $allResults

# Show recommendations
Show-BaselineRecommendations

Write-Host ""
Write-Host "[*] Ready to test prompt optimization!" -ForegroundColor Green
Write-Host "Run with actual LLM to get real baseline metrics, then compare after optimization." -ForegroundColor Gray