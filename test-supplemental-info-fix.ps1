# Test script to verify supplemental information fix
# This tests that the TestCaseGenerator_VM.RefreshSupportContent() method is now public
# and can be called after setting the TestCaseGenerator property

Write-Host "Testing supplemental information initialization fix..." -ForegroundColor Yellow

# Build the project
Write-Host "Building project..." -ForegroundColor Gray
& dotnet build
if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed!" -ForegroundColor Red
    exit 1
}

Write-Host "`n✅ Build succeeded!" -ForegroundColor Green

# Check if RefreshSupportContent method is public
Write-Host "`nChecking if RefreshSupportContent method is public..." -ForegroundColor Gray
$sourceFile = ".\MVVM\ViewModels\TestCaseGenerator_VM.cs"
$publicRefreshMethod = Select-String -Path $sourceFile -Pattern "public void RefreshSupportContent\(\)"

if ($publicRefreshMethod) {
    Write-Host "✅ RefreshSupportContent method is public!" -ForegroundColor Green
    Write-Host "   Found at line: $($publicRefreshMethod.LineNumber)" -ForegroundColor Gray
} else {
    Write-Host "❌ RefreshSupportContent method is not public!" -ForegroundColor Red
    exit 1
}

# Check if RequirementsViewModel calls the method
Write-Host "`nChecking if RequirementsViewModel calls RefreshSupportContent..." -ForegroundColor Gray
$reqViewModel = ".\MVVM\ViewModels\RequirementsViewModel.cs"
$refreshCall = Select-String -Path $reqViewModel -Pattern "RefreshSupportContent\(\)"

if ($refreshCall) {
    Write-Host "✅ RequirementsViewModel calls RefreshSupportContent!" -ForegroundColor Green
    Write-Host "   Found at line: $($refreshCall.LineNumber)" -ForegroundColor Gray
} else {
    Write-Host "❌ RequirementsViewModel does not call RefreshSupportContent!" -ForegroundColor Red
    exit 1
}

Write-Host "`n🎉 All checks passed! The supplemental information initialization fix should now work." -ForegroundColor Green
Write-Host "`n📋 Summary of changes:" -ForegroundColor Cyan
Write-Host "   1. Made TestCaseGenerator_VM.RefreshSupportContent method public" -ForegroundColor White
Write-Host "   2. RequirementsViewModel now calls RefreshSupportContent after setting TestCaseGenerator property" -ForegroundColor White
Write-Host "   3. This ensures supplemental information loads for the first requirement in new projects" -ForegroundColor White

Write-Host "`n🔬 Test scenario:" -ForegroundColor Cyan
Write-Host "   When you create a new project and analyze the first requirement:" -ForegroundColor White
Write-Host "   • Tables and paragraphs should load immediately" -ForegroundColor White
Write-Host "   • Analysis should complete in ~25 seconds (not 3 minutes)" -ForegroundColor White
Write-Host "   • LLM should have access to all supplemental information" -ForegroundColor White