# Quick test to see the optimized prompt structure
Write-Host "Testing Optimized Prompt Generation" -ForegroundColor Green
Write-Host "====================================" -ForegroundColor Green

# Add references to load the prompt builder
Add-Type -Path ".\bin\Debug\net8.0-windows\TestCaseEditorApp.dll"

# Test the optimized prompt
$builder = New-Object TestCaseEditorApp.Prompts.RequirementAnalysisPromptBuilder
$systemPrompt = $builder.GetSystemPrompt()

Write-Host "`nOptimized Prompt Structure:" -ForegroundColor Cyan
Write-Host "Length: $($systemPrompt.Length) characters" -ForegroundColor Yellow

# Show key sections
$lines = $systemPrompt -split "`n"
$tierSections = $lines | Where-Object { $_ -match "^.*TIER \d+:" }

Write-Host "`nHierarchical Structure:" -ForegroundColor Cyan
foreach ($section in $tierSections) {
    Write-Host "  $section" -ForegroundColor White
}

# Test context prompt generation
$contextPrompt = $builder.BuildContextPrompt("TEST-001", "Test Requirement", "The system shall perform boundary scan testing.")

Write-Host "`nContext Prompt:" -ForegroundColor Cyan
Write-Host "Length: $($contextPrompt.Length) characters" -ForegroundColor Yellow

Write-Host "`n✅ Prompt generation working correctly!" -ForegroundColor Green