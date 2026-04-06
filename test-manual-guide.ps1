# Test optimized prompts with the actual TestCaseEditorApp
Write-Host "Testing Optimized Prompts with TestCaseEditorApp" -ForegroundColor Green
Write-Host "=================================================" -ForegroundColor Green

Write-Host "`n📋 MANUAL TESTING STEPS:" -ForegroundColor Cyan
Write-Host "1. Launch TestCaseEditorApp: dotnet run" -ForegroundColor White
Write-Host "2. Create/Load a project" -ForegroundColor White
Write-Host "3. Navigate to Test Case Generation" -ForegroundColor White
Write-Host "4. Add a test requirement (examples below):" -ForegroundColor White

Write-Host "`n🧪 TEST REQUIREMENTS:" -ForegroundColor Yellow
Write-Host "Simple vague requirement:" -ForegroundColor Gray
Write-Host '  "The system shall provide adequate performance"' -ForegroundColor White

Write-Host "`nTechnical requirement:" -ForegroundColor Gray
Write-Host '  "The system shall perform boundary scan testing with appropriate coverage"' -ForegroundColor White

Write-Host "`nMulti-requirement:" -ForegroundColor Gray
Write-Host '  "The system shall validate inputs and log results and send notifications"' -ForegroundColor White

Write-Host "`n✨ WHAT TO LOOK FOR:" -ForegroundColor Cyan
Write-Host "- Analysis runs without errors" -ForegroundColor White
Write-Host "- JSON format is clean (no directive text like 'Fix:')" -ForegroundColor White
Write-Host "- Suggestions include actual requirement text, not instructions" -ForegroundColor White
Write-Host "- Analysis is consistent when run multiple times" -ForegroundColor White
Write-Host "- Response time is reasonable" -ForegroundColor White

Write-Host "`n🔧 FOR ANYTHINGLLM TESTING:" -ForegroundColor Yellow
Write-Host "Set environment variable: " -NoNewline -ForegroundColor White
Write-Host '$env:LLM_PROVIDER="ollama"' -ForegroundColor Green
Write-Host "Or configure AnythingLLM API key in the app settings" -ForegroundColor White

Write-Host "`n🚀 LAUNCH APP:" -ForegroundColor Green
Write-Host "dotnet run --configuration Debug" -ForegroundColor White