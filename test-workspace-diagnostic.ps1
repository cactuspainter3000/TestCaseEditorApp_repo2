#!/usr/bin/env pwsh
# Test AnythingLLM Workspace Configuration
# Diagnoses why success criteria are still being generated despite optimization

param(
    [string]$WorkspaceSlug = "test-case-analysis"
)

Write-Host "=== AnythingLLM Workspace Configuration Diagnostic ===" -ForegroundColor Cyan

$baseUrl = "http://localhost:3001"

try {
    # Test 1: Check if AnythingLLM is running
    Write-Host "`nTest 1: Checking AnythingLLM availability..." -ForegroundColor Yellow
    $pingResult = Test-NetConnection -ComputerName "localhost" -Port 3001 -WarningAction SilentlyContinue
    if ($pingResult.TcpTestSucceeded) {
        Write-Host "✓ AnythingLLM is running on port 3001" -ForegroundColor Green
    } else {
        Write-Host "✗ AnythingLLM is not running on port 3001" -ForegroundColor Red
        return
    }

    # Test 2: List all workspaces
    Write-Host "`nTest 2: Listing workspaces..." -ForegroundColor Yellow
    try {
        $response = Invoke-RestMethod -Uri "$baseUrl/api/v1/workspaces" -Method GET
        if ($response.workspaces) {
            Write-Host "✓ Found $($response.workspaces.Count) workspace(s):" -ForegroundColor Green
            foreach ($ws in $response.workspaces) {
                Write-Host "  - Name: $($ws.name), Slug: $($ws.slug)" -ForegroundColor Cyan
                if ($ws.slug -eq $WorkspaceSlug) {
                    $targetWorkspace = $ws
                }
            }
        } else {
            Write-Host "✗ No workspaces found" -ForegroundColor Red
        }
    } catch {
        Write-Host "✗ Error listing workspaces: $($_.Exception.Message)" -ForegroundColor Red
        return
    }

    # Test 3: Check specific workspace configuration
    if ($targetWorkspace) {
        Write-Host "`nTest 3: Checking workspace '$WorkspaceSlug' configuration..." -ForegroundColor Yellow
        
        Write-Host "  Workspace Details:" -ForegroundColor Cyan
        Write-Host "  - ID: $($targetWorkspace.id)" -ForegroundColor Gray
        Write-Host "  - Name: $($targetWorkspace.name)" -ForegroundColor Gray
        Write-Host "  - Slug: $($targetWorkspace.slug)" -ForegroundColor Gray
        
        # Check system prompt
        if ($targetWorkspace.openAiPrompt) {
            $systemPrompt = $targetWorkspace.openAiPrompt
            Write-Host "  - System Prompt Length: $($systemPrompt.Length) characters" -ForegroundColor Gray
            
            # Check for key indicators of our optimized prompt
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
                
                # Show the problematic sections
                $lines = $systemPrompt -split "`n"
                $criteriaLines = $lines | Select-String "criteria" -Context 2
                if ($criteriaLines) {
                    Write-Host "`n  Problematic sections:" -ForegroundColor Yellow
                    foreach ($line in $criteriaLines) {
                        Write-Host "    $($line.Line)" -ForegroundColor Gray
                    }
                }
            }
            
            # Check temperature setting
            if ($targetWorkspace.PSObject.Properties['openAiTemp']) {
                Write-Host "  - Temperature: $($targetWorkspace.openAiTemp)" -ForegroundColor Gray
            }
            
        } else {
            Write-Host "  ✗ No system prompt configured!" -ForegroundColor Red
            Write-Host "     This means the optimization is not active - full prompts are being sent." -ForegroundColor Red
        }
    } else {
        Write-Host "`nTest 3: Workspace '$WorkspaceSlug' not found!" -ForegroundColor Red
        Write-Host "Available workspaces:" -ForegroundColor Yellow
        foreach ($ws in $response.workspaces) {
            Write-Host "  - $($ws.slug)" -ForegroundColor Gray
        }
    }

    # Test 4: Test a sample prompt
    if ($targetWorkspace) {
        Write-Host "`nTest 4: Testing sample requirement analysis..." -ForegroundColor Yellow
        
        $testRequirement = @"
The system shall validate user input within 2 seconds.
"@
        
        try {
            $chatResponse = Invoke-RestMethod -Uri "$baseUrl/api/v1/workspace/$($targetWorkspace.slug)/chat" -Method POST -ContentType "application/json" -Body (@{
                message = "Analyze this requirement: $testRequirement"
                mode = "query"
            } | ConvertTo-Json)
            
            if ($chatResponse.textResponse) {
                $response = $chatResponse.textResponse
                Write-Host "  Response Length: $($response.Length) characters" -ForegroundColor Gray
                
                $hasSuccessInResponse = $response -like "*success criteria*"
                Write-Host "  Contains 'success criteria': $hasSuccessInResponse" -ForegroundColor $(if($hasSuccessInResponse) {"Red"} else {"Green"})
                
                if ($hasSuccessInResponse) {
                    Write-Host "`n  ⚠️ CONFIRMED: Response still contains success criteria!" -ForegroundColor Red
                    
                    # Show where success criteria appears
                    $lines = $response -split "`n"
                    $successLines = $lines | Select-String "success" -Context 1
                    if ($successLines) {
                        Write-Host "`n  Success criteria found in response:" -ForegroundColor Yellow
                        foreach ($line in $successLines[0..2]) {  # Show first few matches
                            Write-Host "    $($line.Line)" -ForegroundColor Gray
                        }
                    }
                }
            }
        } catch {
            Write-Host "  ✗ Error testing sample prompt: $($_.Exception.Message)" -ForegroundColor Red
        }
    }

} catch {
    Write-Host "✗ Unexpected error: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host "`n=== Diagnostic Complete ===" -ForegroundColor Cyan
Write-Host "If issues were found, check the workspace system prompt configuration in AnythingLLM." -ForegroundColor Yellow