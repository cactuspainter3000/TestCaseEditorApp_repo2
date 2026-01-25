#!/usr/bin/env powershell
# Test script to verify ImportSource auto-detection in workspace loading

Write-Host "=== Testing ImportSource Auto-Detection ===" -ForegroundColor Green

# Test 1: Load the Jama test workspace and check if ImportSource is detected
Write-Host "`nTest 1: Checking Jama workspace..." -ForegroundColor Yellow
$testWorkspace = ".\test_workspace.tcex.json"

if (Test-Path $testWorkspace) {
    Write-Host "Loading workspace: $testWorkspace"
    
    # Read the current JSON to see if ImportSource is already there
    $currentJson = Get-Content $testWorkspace -Raw | ConvertFrom-Json
    
    Write-Host "Current ImportSource value: '$($currentJson.ImportSource)'" -ForegroundColor Cyan
    Write-Host "Requirements count: $($currentJson.Requirements.Count)"
    
    # Check if requirements have GlobalId (Jama indicator)
    $hasGlobalIds = $currentJson.Requirements | Where-Object { $_.GlobalId -and $_.GlobalId.Trim() -ne "" }
    Write-Host "Requirements with GlobalId: $($hasGlobalIds.Count)"
    
    if ($hasGlobalIds.Count -gt 0) {
        Write-Host "✅ This should be detected as Jama workspace" -ForegroundColor Green
        Write-Host "Sample GlobalId: '$($hasGlobalIds[0].GlobalId)'"
    } else {
        Write-Host "❌ No GlobalIds found - detection might fail" -ForegroundColor Red
    }
} else {
    Write-Host "❌ Test workspace file not found: $testWorkspace" -ForegroundColor Red
}

# Test 2: Check Downloads workspace 
Write-Host "`nTest 2: Checking Downloads workspace..." -ForegroundColor Yellow
$downloadsWorkspace = "C:\Users\e10653214\Downloads\Debug Test.tcex.json"

if (Test-Path $downloadsWorkspace) {
    Write-Host "Loading workspace: $downloadsWorkspace"
    
    $downloadJson = Get-Content $downloadsWorkspace -Raw | ConvertFrom-Json
    
    Write-Host "Current ImportSource value: '$($downloadJson.ImportSource)'" -ForegroundColor Cyan
    Write-Host "SourceDocPath: '$($downloadJson.SourceDocPath)'"
    
    if ($downloadJson.SourceDocPath) {
        Write-Host "✅ This should be detected as Document workspace" -ForegroundColor Green
    } else {
        Write-Host "❌ No SourceDocPath - detection might be unclear" -ForegroundColor Red
    }
} else {
    Write-Host "❌ Downloads workspace file not found: $downloadsWorkspace" -ForegroundColor Red
}

Write-Host "`n=== Instructions ===" -ForegroundColor Yellow
Write-Host "1. Launch the app and load one of these workspaces"
Write-Host "2. Check the console output for auto-detection messages:"
Write-Host "   '[Load] Auto-detected Jama workspace (has GlobalId values), set ImportSource = 'Jama''"
Write-Host "   '[Load] Auto-detected Document workspace (has SourceDocPath), set ImportSource = 'Document''"
Write-Host "3. Save the workspace and check if ImportSource appears in the JSON"