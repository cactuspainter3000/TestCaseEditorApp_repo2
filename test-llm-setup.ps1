# Setup AnythingLLM for testing optimized prompts
Write-Host "AnythingLLM Setup for Prompt Testing" -ForegroundColor Green
Write-Host "====================================" -ForegroundColor Green

Write-Host "`n🔧 SETUP OPTIONS:" -ForegroundColor Cyan

Write-Host "`nOption A: Use Ollama (Local LLM)" -ForegroundColor Yellow
Write-Host "1. Install Ollama: https://ollama.ai" -ForegroundColor White
Write-Host "2. Pull a model: ollama pull phi4-mini" -ForegroundColor White
Write-Host '3. Set environment: $env:LLM_PROVIDER="ollama"' -ForegroundColor White
Write-Host '4. Set model: $env:OLLAMA_MODEL="phi4-mini"' -ForegroundColor White

Write-Host "`nOption B: Configure AnythingLLM API Key" -ForegroundColor Yellow
Write-Host "1. Launch AnythingLLM application" -ForegroundColor White
Write-Host "2. Go to Settings -> API Keys" -ForegroundColor White
Write-Host "3. Generate an API key" -ForegroundColor White
Write-Host "4. The app will auto-detect and use it" -ForegroundColor White

Write-Host "`n🧪 TEST SEQUENCE:" -ForegroundColor Cyan
Write-Host "1. Run: " -NoNewline -ForegroundColor White
Write-Host ".\test-enhanced-integration.ps1" -ForegroundColor Green
Write-Host "2. If successful, test with app:" -ForegroundColor White
Write-Host "   - Launch TestCaseEditorApp" -ForegroundColor White
Write-Host "   - Try requirement analysis" -ForegroundColor White
Write-Host "   - Compare results with baseline" -ForegroundColor White

Write-Host "`n📊 COMPARISON TESTING:" -ForegroundColor Yellow
Write-Host "Run the same requirement multiple times and check:" -ForegroundColor White
Write-Host "- Consistency: Same issues detected each time?" -ForegroundColor White
Write-Host "- Quality: Better suggestions than before?" -ForegroundColor White
Write-Host "- Speed: Faster response time?" -ForegroundColor White
Write-Host "- Format: Clean JSON without directive text?" -ForegroundColor White

Write-Host "`n💡 TIP: Use the baseline test to get measurable metrics!" -ForegroundColor Cyan
Write-Host ".\test-prompt-baseline.ps1" -ForegroundColor Green