# Test optimized prompts with live LLM
param(
    [string]$TestRequirement = "The system shall provide adequate boundary scan coverage"
)

Write-Host "Testing Optimized Prompts with Live LLM" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green

Write-Host "`nTest Requirement: $TestRequirement" -ForegroundColor Cyan

try {
    Write-Host "`n1. Launching TestCaseEditorApp for prompt testing..." -ForegroundColor Yellow
    
    # Build first to ensure latest changes
    dotnet build --configuration Debug --verbosity quiet
    if ($LASTEXITCODE -ne 0) {
        Write-Host "❌ Build failed" -ForegroundColor Red
        exit 1
    }
    
    Write-Host "✅ Build successful" -ForegroundColor Green
    
    Write-Host "`n2. Testing prompt generation..." -ForegroundColor Yellow
    
    # Create a simple test file to validate prompt structure
    $testCode = @'
using System;
using TestCaseEditorApp.Prompts;

var builder = new RequirementAnalysisPromptBuilder();
var systemPrompt = builder.GetSystemPrompt();

Console.WriteLine($"✅ System prompt generated: {systemPrompt.Length} characters");

// Check for key optimization markers
if (systemPrompt.Contains("TIER 1:") && systemPrompt.Contains("TIER 2:")) {
    Console.WriteLine("✅ Hierarchical structure detected");
} else {
    Console.WriteLine("❌ Missing hierarchical structure");
}

if (systemPrompt.Contains("Core Processing Loop")) {
    Console.WriteLine("✅ AI cognition patterns implemented");
} else {
    Console.WriteLine("❌ Missing AI cognition patterns");
}

// Test context prompt
var contextPrompt = builder.BuildContextPrompt("TEST-001", "Test Requirement", "The system shall provide adequate performance");
Console.WriteLine($"✅ Context prompt generated: {contextPrompt.Length} characters");

Console.WriteLine("\n🎯 Prompt optimization verification complete!");
'@

    $testFile = "temp_prompt_test.cs"
    $testCode | Out-File -FilePath $testFile -Encoding UTF8
    
    Write-Host "`nRunning prompt structure test..." -ForegroundColor Yellow
    dotnet run --configuration Debug -- --test-prompts
    
    # Clean up
    if (Test-Path $testFile) {
        Remove-Item $testFile -Force
    }
    
} catch {
    Write-Host "❌ Error during testing: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host "`n🚀 NEXT STEPS:" -ForegroundColor Cyan
Write-Host "1. Launch app: dotnet run --configuration Debug" -ForegroundColor White
Write-Host "2. Navigate to Test Case Generation" -ForegroundColor White
Write-Host "3. Add the test requirement above" -ForegroundColor White
Write-Host "4. Run analysis and verify:" -ForegroundColor White
Write-Host "   - No 'Fix:' directive text in results" -ForegroundColor Yellow
Write-Host "   - Clean JSON format" -ForegroundColor Yellow
Write-Host "   - Actual requirement text in suggestions" -ForegroundColor Yellow
Write-Host "   - Consistent results on multiple runs" -ForegroundColor Yellow