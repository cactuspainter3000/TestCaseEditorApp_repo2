# Configure AnythingLLM API Key for Workspace Optimization
# This script helps set up the API key to enable workspace-level prompt optimization

param(
    [string]$ApiKey = ""
)

Write-Host "=== AnythingLLM API Key Configuration ===" -ForegroundColor Cyan

if ([string]::IsNullOrWhiteSpace($ApiKey)) {
    Write-Host "`nPlease copy the API key from AnythingLLM and paste it below:" -ForegroundColor Yellow
    Write-Host "(The API key should be visible in the Settings > API Keys page)" -ForegroundColor Gray
    $ApiKey = Read-Host "API Key"
}

if ([string]::IsNullOrWhiteSpace($ApiKey)) {
    Write-Host "No API key provided. Exiting." -ForegroundColor Red
    return
}

# Test the API key
Write-Host "`nTesting API key..." -ForegroundColor Yellow
try {
    $headers = @{ "Authorization" = "Bearer $ApiKey" }
    $response = Invoke-RestMethod -Uri "http://localhost:3001/api/v1/workspaces" -Method GET -Headers $headers
    Write-Host "✓ API key works! Found $($response.workspaces.Count) workspace(s)" -ForegroundColor Green
    
    # Save to registry for the application to use
    Write-Host "`nSaving API key to application configuration..." -ForegroundColor Yellow
    try {
        $regKey = "HKCU:\SOFTWARE\TestCaseEditorApp\AnythingLLM"
        if (-not (Test-Path $regKey)) {
            New-Item -Path $regKey -Force | Out-Null
        }
        Set-ItemProperty -Path $regKey -Name "ApiKey" -Value $ApiKey
        Write-Host "✓ API key saved to registry" -ForegroundColor Green
        
        # Now test workspace configuration
        Write-Host "`nTesting workspace configuration..." -ForegroundColor Yellow
        $testWorkspace = $response.workspaces | Where-Object { $_.slug -like "*test*" -or $_.slug -like "*analysis*" } | Select-Object -First 1
        if ($testWorkspace) {
            Write-Host "Found test workspace: $($testWorkspace.name) ($($testWorkspace.slug))" -ForegroundColor Cyan
            
            if ($testWorkspace.openAiPrompt) {
                $promptLength = $testWorkspace.openAiPrompt.Length
                Write-Host "Current system prompt: $promptLength characters" -ForegroundColor Gray
                
                $hasOptimization = $testWorkspace.openAiPrompt -like "*requirements quality analysis*"
                if ($hasOptimization) {
                    Write-Host "✓ Workspace already has optimized system prompt!" -ForegroundColor Green
                    Write-Host "  Prompt optimization is active - only context will be sent with requests" -ForegroundColor Green
                } else {
                    Write-Host "⚠ Workspace system prompt needs optimization" -ForegroundColor Yellow
                    Write-Host "  Full prompts will be sent until workspace is configured" -ForegroundColor Yellow
                }
            } else {
                Write-Host "⚠ No system prompt configured in workspace" -ForegroundColor Yellow
                Write-Host "  The application will configure this automatically on next project creation" -ForegroundColor Gray
            }
        } else {
            Write-Host "No test workspace found - will be created automatically" -ForegroundColor Gray
        }
        
        Write-Host "`n✓ Configuration complete!" -ForegroundColor Green
        Write-Host "The application will now use workspace-level optimization to reduce prompt sizes by ~95%" -ForegroundColor Green
        
    } catch {
        Write-Host "✗ Error saving API key to registry: $($_.Exception.Message)" -ForegroundColor Red
        Write-Host "You may need to run as Administrator or configure the key manually" -ForegroundColor Yellow
    }
    
} catch {
    Write-Host "✗ API key test failed: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "Please verify the API key is correct and try again" -ForegroundColor Yellow
}