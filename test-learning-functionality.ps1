#!/usr/bin/env pwsh

# Test script to verify LLM learning functionality after DI fix

Write-Host "=== Testing Learning Functionality After DI Fix ===" -ForegroundColor Cyan

# 1. Check that learning services are properly registered
Write-Host "`n1. Checking service registrations..." -ForegroundColor Yellow

$appXamlPath = "App.xaml.cs"
if (Test-Path $appXamlPath) {
    Write-Host "Looking for IEditDetectionService and ILLMLearningService registrations:" -ForegroundColor Green
    Select-String -Path $appXamlPath -Pattern "(IEditDetectionService|ILLMLearningService)" -Context 2
    Write-Host ""
} else {
    Write-Host "App.xaml.cs not found!" -ForegroundColor Red
}

# 2. Check that RequirementAnalysisViewModel has the proper constructor
Write-Host "2. Checking RequirementAnalysisViewModel constructor..." -ForegroundColor Yellow

$vmPath = "MVVM\Domains\Requirements\ViewModels\RequirementAnalysisViewModel.cs"
if (Test-Path $vmPath) {
    Write-Host "Constructor signature:" -ForegroundColor Green
    Select-String -Path $vmPath -Pattern "public RequirementAnalysisViewModel" -Context 5
    Write-Host ""
} else {
    Write-Host "RequirementAnalysisViewModel.cs not found!" -ForegroundColor Red
}

# 3. Check that Requirements_MainViewModel properly instantiates with DI
Write-Host "3. Checking Requirements_MainViewModel instantiation..." -ForegroundColor Yellow

$mainVmPath = "MVVM\Domains\Requirements\ViewModels\Requirements_MainViewModel.cs"
if (Test-Path $mainVmPath) {
    Write-Host "RequirementAnalysisViewModel instantiation:" -ForegroundColor Green
    Select-String -Path $mainVmPath -Pattern "new RequirementAnalysisViewModel" -Context 5
    Write-Host ""
} else {
    Write-Host "Requirements_MainViewModel.cs not found!" -ForegroundColor Red
}

# 4. Check logs for learning functionality messages
Write-Host "4. Checking recent logs for learning-related messages..." -ForegroundColor Yellow

$logPatterns = @(
    "LLM learning service",
    "EditDetectionService", 
    "learning feedback",
    "learning workflow",
    "User consent",
    "External analysis detected"
)

foreach ($pattern in $logPatterns) {
    Write-Host "Checking for: $pattern" -ForegroundColor Cyan
    Get-Content -Path "debug_*.txt", "targeted_*.txt" -ErrorAction SilentlyContinue | 
        Select-String $pattern | Select-Object -Last 3
}

Write-Host "`n=== Test Instructions ===" -ForegroundColor Magenta
Write-Host "To verify learning functionality works:" -ForegroundColor White
Write-Host "1. Open a requirement in the Requirements tab" -ForegroundColor Gray
Write-Host "2. Click 'LLM Analysis Request → Clipboard'" -ForegroundColor Gray  
Write-Host "3. Paste external LLM response using Ctrl+V" -ForegroundColor Gray
Write-Host "4. Edit the requirement and save" -ForegroundColor Gray
Write-Host "5. You should see prompts asking about updating LLM learning" -ForegroundColor Gray

Write-Host "`n=== Expected Log Messages ===" -ForegroundColor Magenta
Write-Host "✅ EditDetectionService properly injected and initialized" -ForegroundColor Green
Write-Host "✅ LLM learning service available for feedback collection" -ForegroundColor Green
Write-Host "✅ External analysis detected - starting learning workflow" -ForegroundColor Green
Write-Host "✅ User consent obtained - transmitting feedback to LLM" -ForegroundColor Green

Write-Host "`nDI Fix Applied: Learning services should now be properly injected!" -ForegroundColor Green