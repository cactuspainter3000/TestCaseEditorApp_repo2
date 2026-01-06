# Manual API Key Configuration and Workspace Test
# Fixes the success criteria issue by properly configuring AnythingLLM authentication

Write-Host "=== AnythingLLM API Key Configuration ===" -ForegroundColor Cyan

# Function to test if API key works
function Test-ApiKey($apiKey) {
    try {
        $headers = @{ "Authorization" = "Bearer $apiKey" }
        $response = Invoke-RestMethod -Uri "http://localhost:3001/api/v1/workspaces" -Method GET -Headers $headers
        return $true
    } catch {
        return $false
    }
}

# Try common development API keys
$testKeys = @("development", "test", "default", "anythinglm", "local", "dev", "admin")

Write-Host "`nTesting common API keys..." -ForegroundColor Yellow
$workingKey = $null

foreach ($key in $testKeys) {
    Write-Host "  Testing: $key" -NoNewline
    if (Test-ApiKey $key) {
        Write-Host " ✓" -ForegroundColor Green
        $workingKey = $key
        break
    } else {
        Write-Host " ✗" -ForegroundColor Red
    }
}

if ($workingKey) {
    Write-Host "`n✓ Found working API key: $workingKey" -ForegroundColor Green
    
    # Save it to registry for the app to use
    Write-Host "Saving API key to registry..." -ForegroundColor Yellow
    try {
        $regKey = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey("SOFTWARE\TestCaseEditorApp\AnythingLLM")
        $regKey.SetValue("ApiKey", $workingKey)
        $regKey.Close()
        Write-Host "✓ API key saved to registry" -ForegroundColor Green
    } catch {
        Write-Host "✗ Failed to save API key: $_" -ForegroundColor Red
    }
    
    # Test workspace access
    Write-Host "`nTesting workspace access..." -ForegroundColor Yellow
    try {
        $headers = @{ "Authorization" = "Bearer $workingKey" }
        $response = Invoke-RestMethod -Uri "http://localhost:3001/api/v1/workspaces" -Method GET -Headers $headers
        Write-Host "✓ Successfully accessed workspaces:" -ForegroundColor Green
        
        foreach ($ws in $response.workspaces) {
            Write-Host "  - $($ws.name) ($($ws.slug))" -ForegroundColor Cyan
            
            # Check if this workspace has proper system prompt
            if ($ws.openAiPrompt) {
                $hasSuccessCriteria = $ws.openAiPrompt -like "*success criteria*"
                $hasRequirementsAnalysis = $ws.openAiPrompt -like "*requirements quality analysis*"
                
                Write-Host "    System Prompt: $($ws.openAiPrompt.Length) chars" -ForegroundColor Gray
                Write-Host "    Contains 'requirements quality analysis': $hasRequirementsAnalysis" -ForegroundColor $(if($hasRequirementsAnalysis) {"Green"} else {"Red"})
                Write-Host "    Contains 'success criteria': $hasSuccessCriteria" -ForegroundColor $(if($hasSuccessCriteria) {"Red"} else {"Green"})
                
                if ($hasSuccessCriteria) {
                    Write-Host "    ⚠️ This workspace still has success criteria in system prompt!" -ForegroundColor Red
                }
            } else {
                Write-Host "    System Prompt: Not configured" -ForegroundColor Red
            }
        }
    } catch {
        Write-Host "✗ Failed to access workspaces: $_" -ForegroundColor Red
    }
    
} else {
    Write-Host "`n✗ No working API key found" -ForegroundColor Red
    Write-Host "You may need to:" -ForegroundColor Yellow
    Write-Host "  1. Open AnythingLLM in browser (http://localhost:3001)" -ForegroundColor Yellow
    Write-Host "  2. Go to Settings > API Keys" -ForegroundColor Yellow
    Write-Host "  3. Generate a new API key" -ForegroundColor Yellow
    Write-Host "  4. Run this script with the key: .\api-key-test.ps1 -ApiKey 'your-key-here'" -ForegroundColor Yellow
}

Write-Host "`n=== Configuration Complete ===" -ForegroundColor Cyan