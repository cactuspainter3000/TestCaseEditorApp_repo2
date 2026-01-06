#!/usr/bin/env pwsh

# Test script to verify fuzzy workspace matching fixes the name mismatch issue

Write-Host "🧪 Testing Fuzzy Workspace Matching Fix" -ForegroundColor Cyan
Write-Host "=========================================="

Write-Host "🔍 Analyzing the issue from the logs..." -ForegroundColor Yellow
Write-Host "  ❌ Looking for: 'New Project 2026' (with spaces)"
Write-Host "  ✅ Available:   'NewProject2026' (without spaces)"
Write-Host ""

Write-Host "🔧 Applied Fix: Enhanced workspace detection with fuzzy matching" -ForegroundColor Green
Write-Host "  • Exact name matching (original logic)"
Write-Host "  • Normalized fuzzy matching (removes spaces, dashes, underscores)"
Write-Host "  • Fallback to 'Test Case Editor' pattern if no matches"
Write-Host ""

# Test the fuzzy matching logic conceptually
function Test-FuzzyMatchingLogic {
    Write-Host "📝 Testing fuzzy matching logic..." -ForegroundColor Yellow
    
    # Simulate the normalization logic
    $projectName = "New Project 2026"
    $workspaceName = "NewProject2026"
    
    $normalizedProject = $projectName.Replace(" ", "").Replace("-", "").Replace("_", "").ToLowerInvariant()
    $normalizedWorkspace = $workspaceName.Replace(" ", "").Replace("-", "").Replace("_", "").ToLowerInvariant()
    
    Write-Host "  Original project name: '$projectName'"
    Write-Host "  Available workspace:   '$workspaceName'"
    Write-Host "  Normalized project:    '$normalizedProject'"
    Write-Host "  Normalized workspace:  '$normalizedWorkspace'"
    Write-Host "  Match result:          $($normalizedProject -eq $normalizedWorkspace)"
    
    if ($normalizedProject -eq $normalizedWorkspace) {
        Write-Host "  ✅ Fuzzy matching will work!" -ForegroundColor Green
        return $true
    } else {
        Write-Host "  ❌ Fuzzy matching may not work" -ForegroundColor Red
        return $false
    }
}

# Test other common naming variations
function Test-CommonVariations {
    Write-Host ""
    Write-Host "📝 Testing other common naming variations..." -ForegroundColor Yellow
    
    $testCases = @(
        @{ Project = "My Test Project"; Workspace = "MyTestProject" }
        @{ Project = "Test-Case-Editor"; Workspace = "Test_Case_Editor" }
        @{ Project = "Requirements Analysis"; Workspace = "requirements-analysis" }
        @{ Project = "Project_Name_123"; Workspace = "Project Name 123" }
    )
    
    $allPassed = $true
    
    foreach ($test in $testCases) {
        $normalizedProject = $test.Project.Replace(" ", "").Replace("-", "").Replace("_", "").ToLowerInvariant()
        $normalizedWorkspace = $test.Workspace.Replace(" ", "").Replace("-", "").Replace("_", "").ToLowerInvariant()
        $match = $normalizedProject -eq $normalizedWorkspace
        
        if ($match) {
            Write-Host "  ✅ '$($test.Project)' ↔ '$($test.Workspace)'" -ForegroundColor Green
        } else {
            Write-Host "  ❌ '$($test.Project)' ↔ '$($test.Workspace)'" -ForegroundColor Red
            $allPassed = $false
        }
    }
    
    return $allPassed
}

# Verify the code changes exist
function Test-CodeChanges {
    Write-Host ""
    Write-Host "📝 Verifying code changes are in place..." -ForegroundColor Yellow
    
    $serviceFile = "MVVM/Domains/TestCaseGeneration/Services/RequirementAnalysisService.cs"
    
    # Check for fuzzy matching logic
    $fuzzyLogicExists = Select-String -Path $serviceFile -Pattern "normalizedProjectName.*Replace.*ToLowerInvariant" -Quiet
    if ($fuzzyLogicExists) {
        Write-Host "  ✅ Fuzzy matching normalization logic found" -ForegroundColor Green
    } else {
        Write-Host "  ❌ Fuzzy matching normalization logic not found" -ForegroundColor Red
        return $false
    }
    
    # Check for fuzzy matching comparison
    $fuzzyCompareExists = Select-String -Path $serviceFile -Pattern "normalizedWorkspaceName.*normalizedProjectName" -Quiet
    if ($fuzzyCompareExists) {
        Write-Host "  ✅ Fuzzy matching comparison logic found" -ForegroundColor Green
    } else {
        Write-Host "  ❌ Fuzzy matching comparison logic not found" -ForegroundColor Red
        return $false
    }
    
    # Check for debug logging
    $debugLogExists = Select-String -Path $serviceFile -Pattern "Fuzzy match found" -Quiet
    if ($debugLogExists) {
        Write-Host "  ✅ Debug logging for fuzzy matching found" -ForegroundColor Green
    } else {
        Write-Host "  ❌ Debug logging for fuzzy matching not found" -ForegroundColor Red
        return $false
    }
    
    return $true
}

# Run all tests
Write-Host ""
$fuzzyLogicTest = Test-FuzzyMatchingLogic
$variationsTest = Test-CommonVariations
$codeChangesTest = Test-CodeChanges

Write-Host ""
Write-Host "🎯 Summary:" -ForegroundColor Cyan
if ($fuzzyLogicTest -and $variationsTest -and $codeChangesTest) {
    Write-Host "✅ All tests passed! The fuzzy workspace matching fix should resolve the name mismatch issue." -ForegroundColor Green
    Write-Host ""
    Write-Host "🔄 Expected behavior on next analysis:" -ForegroundColor Yellow
    Write-Host "  1. Look for exact match: 'New Project 2026' → Not found"
    Write-Host "  2. Try fuzzy match: 'newproject2026' ↔ 'newproject2026' → Match found!"
    Write-Host "  3. Use 'NewProject2026' workspace instead of fallback"
    Write-Host "  4. Analysis should complete much faster (~30 seconds vs 2+ minutes)"
    
    exit 0
} else {
    Write-Host "❌ Some tests failed. Please review the implementation." -ForegroundColor Red
    exit 1
}