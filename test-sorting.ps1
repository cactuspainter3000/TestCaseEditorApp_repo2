#!/usr/bin/env pwsh

# Quick test script for requirement sorting using exact NavigationViewModel logic

Write-Host "Testing requirement sorting with NavigationViewModel logic..." -ForegroundColor Green

# Using the exact same regex pattern as NavigationViewModel and RequirementsIndexViewModel
$pattern = "^(.*?)(\d+)$"

# Test requirements from user's screenshot
$testRequirements = @(
    "DECAGON-REQ_RC-11",
    "DECAGON-REQ_RC-16", 
    "DECAGON-REQ_RC-19",
    "DECAGON-REQ_RC-20",
    "DECAGON-REQ_RC-24",
    "DECAGON-REQ_RC-25",
    "DECAGON-REQ_RC-26",
    "DECAGON-REQ_RC-27",
    "DECAGON-REQ_RC-12",  # This should move up to after 11
    "DECAGON-REQ_RC-28",
    "DECAGON-REQ_RC-5"    # This should move to the top
)

Write-Host "Original order from screenshot:" -ForegroundColor Yellow
for ($i = 0; $i -lt $testRequirements.Length; $i++) {
    Write-Host "  $($i + 1). $($testRequirements[$i])"
}

Write-Host "`nTesting regex pattern: $pattern" -ForegroundColor Yellow
foreach ($req in $testRequirements) {
    if ($req -match $pattern) {
        $prefix = $matches[1]
        $number = [int]$matches[2]
        Write-Host "  $req -> prefix: '$prefix', number: $number" -ForegroundColor Green
    } else {
        Write-Host "  $req -> NO MATCH" -ForegroundColor Red
    }
}

Write-Host "`nExpected sorted order:" -ForegroundColor Yellow
$sorted = $testRequirements | Sort-Object { 
    if ($_ -match $pattern) {
        $prefix = $matches[1]
        $number = [int]$matches[2]
        # Create a sort key that combines prefix with zero-padded number
        return "$prefix{0:D10}" -f $number
    } else {
        return $_  # Non-matching items sorted alphabetically
    }
}

for ($i = 0; $i -lt $sorted.Length; $i++) {
    Write-Host "  $($i + 1). $($sorted[$i])" -ForegroundColor Green
}

Write-Host "`nExpected final order should be:" -ForegroundColor Cyan
Write-Host "  1. DECAGON-REQ_RC-5" -ForegroundColor Cyan
Write-Host "  2. DECAGON-REQ_RC-11" -ForegroundColor Cyan
Write-Host "  3. DECAGON-REQ_RC-12" -ForegroundColor Cyan
Write-Host "  4. DECAGON-REQ_RC-16" -ForegroundColor Cyan
Write-Host "  5. DECAGON-REQ_RC-19" -ForegroundColor Cyan
Write-Host "  6. DECAGON-REQ_RC-20" -ForegroundColor Cyan
Write-Host "  7. DECAGON-REQ_RC-24" -ForegroundColor Cyan
Write-Host "  8. DECAGON-REQ_RC-25" -ForegroundColor Cyan
Write-Host "  9. DECAGON-REQ_RC-26" -ForegroundColor Cyan
Write-Host " 10. DECAGON-REQ_RC-27" -ForegroundColor Cyan
Write-Host " 11. DECAGON-REQ_RC-28" -ForegroundColor Cyan

Write-Host "`nDone!" -ForegroundColor Green