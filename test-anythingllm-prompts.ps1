# Direct LLM testing with optimized prompts
Write-Host "Testing Optimized Prompts with AnythingLLM API" -ForegroundColor Green
Write-Host "===============================================" -ForegroundColor Green

# Test requirement that should trigger improvements
$testRequirement = "The system shall provide adequate boundary scan coverage"
Write-Host "`nTest Requirement: $testRequirement" -ForegroundColor Cyan

try {
    # Check if AnythingLLM is running
    $response = Invoke-RestMethod -Uri "http://localhost:3001/api/v1/workspaces" -Method Get -ErrorAction SilentlyContinue
    if ($response) {
        Write-Host "✅ AnythingLLM API is accessible" -ForegroundColor Green
    }
} catch {
    Write-Host "❌ AnythingLLM API not accessible: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "   Ensure AnythingLLM is running and API key is configured" -ForegroundColor Yellow
    exit 1
}

Write-Host "`n🧪 Testing Prompt Optimization Results:" -ForegroundColor Yellow

# What we expect from the optimized prompts
Write-Host "`nExpected Improvements:" -ForegroundColor Cyan
Write-Host "✅ No 'Fix:' directive text in responses" -ForegroundColor White
Write-Host "✅ Clean JSON format with all required fields" -ForegroundColor White
Write-Host "✅ SuggestedEdit contains actual requirement text" -ForegroundColor White
Write-Host "✅ Consistent analysis on multiple runs" -ForegroundColor White
Write-Host "✅ Better quality score accuracy" -ForegroundColor White

Write-Host "`n🔧 To Test Live:" -ForegroundColor Yellow
Write-Host "1. Set environment: " -NoNewline -ForegroundColor White
Write-Host '$env:LLM_PROVIDER="anythingllm"' -ForegroundColor Green
Write-Host "2. Launch app: dotnet run" -ForegroundColor White
Write-Host "3. Create/load a test project" -ForegroundColor White
Write-Host "4. Navigate to Test Case Generation" -ForegroundColor White
Write-Host "5. Add requirement: '$testRequirement'" -ForegroundColor White
Write-Host "6. Run analysis and verify improvements listed above" -ForegroundColor White

Write-Host "`n📊 Comparison Test:" -ForegroundColor Cyan
Write-Host "Run the same requirement 3 times and check:" -ForegroundColor White
Write-Host "- Same issues detected each time (consistency)" -ForegroundColor Yellow
Write-Host "- Quality score is appropriate (7-8 for this vague requirement)" -ForegroundColor Yellow
Write-Host "- Suggestions include '[specify X]' placeholders, not fabricated details" -ForegroundColor Yellow

Write-Host "`n🎯 Key Success Indicators:" -ForegroundColor Green
Write-Host "Before optimization: May see 'Fix: Define boundary scan coverage'" -ForegroundColor Red
Write-Host "After optimization:  Should see 'The system shall perform [specify type] boundary scan coverage achieving [specify percentage]% of [define scope]'" -ForegroundColor Green

Write-Host "`nOptimized prompts are ready for testing!" -ForegroundColor Green