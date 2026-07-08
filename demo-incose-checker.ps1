#!/usr/bin/env pwsh
# Quick demo of IncoseConsistentContentChecker

Add-Type -Path ".\bin\Debug\net8.0-windows\TestCaseEditorApp.dll"

$checker = New-Object TestCaseEditorApp.MVVM.Domains.Requirements.Services.IncoseConsistentContentChecker

Write-Host "=== INCOSE Consistent Content Checker Demo ===" -ForegroundColor Cyan
Write-Host ""

# Test cases to demonstrate
$testCases = @(
    @{
        Name = "Good: General pattern"
        Text = "The test system shall generate a diagnostic report."
    },
    @{
        Name = "Good: Event-driven pattern"
        Text = "The operator interface shall display an alert when a fault condition is detected."
    },
    @{
        Name = "Good: State-driven with timing"
        Text = "The system shall log all transactions while the audit mode is active within 5 seconds."
    },
    @{
        Name = "FAIL: Missing actor"
        Text = "Shall generate a report."
    },
    @{
        Name = "FAIL: Missing obligation"
        Text = "The system generates a report."
    },
    @{
        Name = "FAIL: Mixed conditionals"
        Text = "The system shall log when triggered while processing within 1 second."
    },
    @{
        Name = "WARN: Uses 'must' instead of 'shall'"
        Text = "The system must complete the test within 30 seconds."
    },
    @{
        Name = "Real ATP: Good"
        Text = "The DHM test system shall perform boundary scan coverage of all JTAG-accessible nodes when the test sequence is initiated."
    }
)

foreach ($test in $testCases) {
    Write-Host "Test: $($test.Name)" -ForegroundColor Green
    Write-Host "  Input: `"$($test.Text)`"" -ForegroundColor Gray
    
    $result = $checker.Check($test.Text)
    
    Write-Host "  Result:" -ForegroundColor Yellow
    Write-Host "    Type: $($result.RequirementType)" -ForegroundColor White
    Write-Host "    Passed: $($result.Passed)" -ForegroundColor White
    Write-Host "    Actor: $($result.DetectedActor)" -ForegroundColor White
    Write-Host "    Action: $($result.DetectedAction)" -ForegroundColor White
    
    if ($result.Issues.Count -gt 0) {
        Write-Host "    Issues:" -ForegroundColor Red
        foreach ($issue in $result.Issues) {
            Write-Host "      [$($issue.Code)] $($issue.Severity): $($issue.Description)" -ForegroundColor Red
        }
    } else {
        Write-Host "    No structural issues found X" -ForegroundColor Green
    }
    
    Write-Host "    Canonical: $($result.CanonicalFormSuggestion)" -ForegroundColor Cyan
    Write-Host ""
}

Write-Host "Demo complete!" -ForegroundColor Cyan
