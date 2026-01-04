#!/usr/bin/env pwsh
# Demo script showing how to configure Jama Connect integration

Write-Host "=== Jama Connect Integration Setup Demo ===" -ForegroundColor Cyan
Write-Host ""

Write-Host "✅ Successfully created Jama Connect integration!" -ForegroundColor Green
Write-Host ""

Write-Host "What we've implemented:" -ForegroundColor Yellow
Write-Host "  🔧 JamaConnectService - Full REST API client" -ForegroundColor White
Write-Host "  🔧 test-jama-api.ps1 - Connection testing script" -ForegroundColor White
Write-Host "  🔧 Updated ExportAllToJamaCommand - Now functional!" -ForegroundColor White
Write-Host "  🔧 Dependency injection - Properly configured" -ForegroundColor White
Write-Host ""

Write-Host "How to configure for your Jama instance:" -ForegroundColor Yellow
Write-Host ""

Write-Host "Option 1 - API Token (Recommended):" -ForegroundColor Green
Write-Host "  `$env:JAMA_BASE_URL = 'https://yourcompany.jamacloud.com'" -ForegroundColor Gray
Write-Host "  `$env:JAMA_API_TOKEN = 'your-api-token-here'" -ForegroundColor Gray
Write-Host ""

Write-Host "Option 2 - Username/Password:" -ForegroundColor Green
Write-Host "  `$env:JAMA_BASE_URL = 'https://yourcompany.jamacloud.com'" -ForegroundColor Gray
Write-Host "  `$env:JAMA_USERNAME = 'your-username'" -ForegroundColor Gray
Write-Host "  `$env:JAMA_PASSWORD = 'your-password'" -ForegroundColor Gray
Write-Host ""

Write-Host "Testing your configuration:" -ForegroundColor Yellow
Write-Host "  1. Set the environment variables above" -ForegroundColor White
Write-Host "  2. Run: .\test-jama-api.ps1" -ForegroundColor White
Write-Host "  3. Verify connection and data access" -ForegroundColor White
Write-Host ""

Write-Host "Using in the application:" -ForegroundColor Yellow
Write-Host "  1. Configure environment variables as above" -ForegroundColor White
Write-Host "  2. Start TestCaseEditorApp" -ForegroundColor White
Write-Host "  3. Click 'Export to Jama...' button in side menu" -ForegroundColor White
Write-Host "  4. Application will:" -ForegroundColor White
Write-Host "     • Test connection to your Jama instance" -ForegroundColor Gray
Write-Host "     • Show available projects" -ForegroundColor Gray
Write-Host "     • Offer to import requirements directly" -ForegroundColor Gray
Write-Host "     • NO MORE FILE EXPORTS NEEDED!" -ForegroundColor Green
Write-Host ""

Write-Host "API Capabilities now available:" -ForegroundColor Yellow
Write-Host "  ✅ Direct requirement import from Jama" -ForegroundColor Green
Write-Host "  ✅ Real-time project listing" -ForegroundColor Green
Write-Host "  ✅ Connection testing and validation" -ForegroundColor Green
Write-Host "  🔄 Test case push-back (ready to implement)" -ForegroundColor Orange
Write-Host "  🔄 Bidirectional sync (ready to implement)" -ForegroundColor Orange
Write-Host ""

Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "  1. Get your Jama Connect credentials" -ForegroundColor White
Write-Host "  2. Test with .\test-jama-api.ps1" -ForegroundColor White
Write-Host "  3. Try the Export to Jama button in app" -ForegroundColor White
Write-Host "  4. Replace file-based workflow entirely!" -ForegroundColor White
Write-Host ""

Write-Host "This eliminates the error-prone export/import workflow!" -ForegroundColor Green -BackgroundColor Black