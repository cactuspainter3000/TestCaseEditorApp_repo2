# Test AnythingLLM Workspace Configuration
# Diagnoses why success criteria are still being generated despite optimization

param(
    [string]$WorkspaceSlug = "test-case-analysis"
)

Write-Host "=== AnythingLLM Workspace Configuration Diagnostic ===" -ForegroundColor Cyan

$baseUrl = "http://localhost:3001"

# Test 1: Check if AnythingLLM is running
Write-Host "`nTest 1: Checking AnythingLLM availability..." -ForegroundColor Yellow
$pingResult = Test-NetConnection -ComputerName "localhost" -Port 3001 -WarningAction SilentlyContinue
if (-not $pingResult.TcpTestSucceeded) {
    Write-Host "✗ AnythingLLM is not running on port 3001" -ForegroundColor Red
    return
}
Write-Host "✓ AnythingLLM is running on port 3001" -ForegroundColor Green

# Test 2: List all workspaces
Write-Host "`nTest 2: Listing workspaces..." -ForegroundColor Yellow
try {
    $response = Invoke-RestMethod -Uri "$baseUrl/api/v1/workspaces" -Method GET
    if ($response.workspaces) {
        Write-Host "✓ Found $($response.workspaces.Count) workspace(s):" -ForegroundColor Green
        $targetWorkspace = $null
        foreach ($ws in $response.workspaces) {
            Write-Host "  - Name: $($ws.name), Slug: $($ws.slug)" -ForegroundColor Cyan
            if ($ws.slug -eq $WorkspaceSlug) {
                $targetWorkspace = $ws
            }
        }
    } else {
        Write-Host "✗ No workspaces found" -ForegroundColor Red
        return
    }
} catch {
    Write-Host "✗ Error listing workspaces: $($_.Exception.Message)" -ForegroundColor Red
    return
}

# Test 3: Check specific workspace configuration
if (-not $targetWorkspace) {
    Write-Host "`nTest 3: Workspace '$WorkspaceSlug' not found!" -ForegroundColor Red
    return
}

Write-Host "`nTest 3: Checking workspace '$WorkspaceSlug' configuration..." -ForegroundColor Yellow
Write-Host "  Workspace Details:" -ForegroundColor Cyan
Write-Host "  - ID: $($targetWorkspace.id)" -ForegroundColor Gray
Write-Host "  - Name: $($targetWorkspace.name)" -ForegroundColor Gray
Write-Host "  - Slug: $($targetWorkspace.slug)" -ForegroundColor Gray

# Check system prompt
if ($targetWorkspace.openAiPrompt) {
    $systemPrompt = $targetWorkspace.openAiPrompt
    Write-Host "  - System Prompt Length: $($systemPrompt.Length) characters" -ForegroundColor Gray
    
    # Check for key indicators
    $hasRequirementsAnalysis = $systemPrompt -like "*requirements quality analysis*"
    $hasAntiFabrication = $systemPrompt -like "*ANTI-FABRICATION RULES*"
    $hasSuccessCriteria = $systemPrompt -like "*success criteria*"
    
    Write-Host "  System Prompt Analysis:" -ForegroundColor Cyan
    Write-Host "    ✓ Contains 'requirements quality analysis': $hasRequirementsAnalysis" -ForegroundColor $(if($hasRequirementsAnalysis) {"Green"} else {"Red"})
    Write-Host "    ✓ Contains 'ANTI-FABRICATION RULES': $hasAntiFabrication" -ForegroundColor $(if($hasAntiFabrication) {"Green"} else {"Red"})
    Write-Host "    ✗ Contains 'success criteria': $hasSuccessCriteria" -ForegroundColor $(if($hasSuccessCriteria) {"Red"} else {"Green"})
    
    if ($hasSuccessCriteria) {
        Write-Host "`n  ⚠️ ISSUE FOUND: System prompt still contains 'success criteria' references!" -ForegroundColor Red
        Write-Host "     This explains why success criteria are still being generated." -ForegroundColor Red
        
        # Show problematic sections
        $lines = $systemPrompt -split "`n"
        $criteriaLines = $lines | Select-String "criteria"
        if ($criteriaLines.Count -gt 0) {
            Write-Host "`n  Problematic sections (first 3):" -ForegroundColor Yellow
            for ($i = 0; $i -lt [Math]::Min(3, $criteriaLines.Count); $i++) {
                Write-Host "    Line $($criteriaLines[$i].LineNumber): $($criteriaLines[$i].Line)" -ForegroundColor Gray
            }
        }
    }
} else {
    Write-Host "  ✗ No system prompt configured!" -ForegroundColor Red
    Write-Host "     This means the optimization is not active - full prompts are being sent." -ForegroundColor Red
}

Write-Host "`n=== Diagnostic Complete ===" -ForegroundColor Cyan