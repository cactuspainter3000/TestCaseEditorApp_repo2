#!/usr/bin/env pwsh

Write-Host "=== Testing AnythingLLM Chat Clear Scripts ===" -ForegroundColor Green
Write-Host ""

# Check if API key is set
if (-not $env:ANYTHINGLM_API_KEY) {
    Write-Host "❌ ANYTHINGLM_API_KEY environment variable not set" -ForegroundColor Red
    Write-Host "Set it with: `$env:ANYTHINGLM_API_KEY = 'your-api-key'" -ForegroundColor Yellow
    Write-Host ""
}
else {
    Write-Host "✅ API key found" -ForegroundColor Green
}

# Test AnythingLLM connection
Write-Host "🔗 Testing AnythingLLM connection..." -ForegroundColor Blue
$portTest = Test-NetConnection localhost -Port 3001 -InformationLevel Quiet -WarningAction SilentlyContinue
if ($portTest) {
    Write-Host "✅ AnythingLLM is running on port 3001" -ForegroundColor Green
} else {
    Write-Host "❌ AnythingLLM is not running on port 3001" -ForegroundColor Red
}

Write-Host ""
Write-Host "📋 Available Scripts:" -ForegroundColor Yellow
Write-Host "1. .\Clear-AnythingLLM-Chat.ps1 -Mode list" -ForegroundColor White
Write-Host "   - Lists all available workspaces" -ForegroundColor Gray
Write-Host ""
Write-Host "2. .\clear-chat-quick.ps1" -ForegroundColor White
Write-Host "   - Quick clear for Test Case Editor workspace" -ForegroundColor Gray
Write-Host ""
Write-Host "3. .\Clear-AnythingLLM-Chat.ps1 -WorkspaceName 'Your Workspace' -Mode chat" -ForegroundColor White
Write-Host "   - Clear specific workspace chat" -ForegroundColor Gray
Write-Host ""
Write-Host "4. .\Clear-AnythingLLM-Chat.ps1 -Mode reset" -ForegroundColor White
Write-Host "   - Reset workspace completely (interactive)" -ForegroundColor Gray

Write-Host ""
Write-Host "💡 Example usage:" -ForegroundColor Cyan
Write-Host "# List workspaces first" -ForegroundColor Gray
Write-Host ".\Clear-AnythingLLM-Chat.ps1 -Mode list" -ForegroundColor White
Write-Host ""
Write-Host "# Clear specific workspace" -ForegroundColor Gray
Write-Host ".\clear-chat-quick.ps1" -ForegroundColor White