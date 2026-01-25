# Test script to verify workspace view routing fix after unload/reload cycle
# This verifies that conditional header and main workspace views update properly when unloading 
# and reloading projects with different ImportSource types (Jama vs Document)

Write-Host "🧪 Testing Workspace View Routing Fix After Unload/Reload" -ForegroundColor Cyan
Write-Host "=" * 50 -ForegroundColor Gray

Write-Host ""
Write-Host "📝 Test Scenario:" -ForegroundColor Yellow
Write-Host "  1. Load a Jama project (should show Jama views)" -ForegroundColor White
Write-Host "  2. Unload the project" -ForegroundColor White
Write-Host "  3. Load a Document project (should show Document views)" -ForegroundColor White
Write-Host "  4. Unload the project" -ForegroundColor White  
Write-Host "  5. Load a Jama project again (should show Jama views again)" -ForegroundColor White

Write-Host ""
Write-Host "🔍 What to test manually:" -ForegroundColor Green
Write-Host ""

Write-Host "1. Load Jama project:" -ForegroundColor White
Write-Host "   - Open the test_jama_import.tcex.json file" -ForegroundColor Gray
Write-Host "   - Navigate to Requirements section" -ForegroundColor Gray
Write-Host "   - VERIFY: Header shows simple requirement name (no description details)" -ForegroundColor Gray
Write-Host "   - VERIFY: Main workspace uses JamaRequirementsMainViewModel" -ForegroundColor Gray

Write-Host ""
Write-Host "2. Unload Jama project:" -ForegroundColor White  
Write-Host "   - Click Unload Project in side menu" -ForegroundColor Gray
Write-Host "   - VERIFY: Debug logs show workspace context and view config clearing" -ForegroundColor Gray

Write-Host ""
Write-Host "3. Load Document project:" -ForegroundColor White
Write-Host "   - Open the test_document_import.tcex.json file" -ForegroundColor Gray
Write-Host "   - Navigate to Requirements section" -ForegroundColor Gray
Write-Host "   - VERIFY: Header shows detailed requirement info (description, supplemental, etc.)" -ForegroundColor Gray
Write-Host "   - VERIFY: Main workspace uses Requirements_MainViewModel" -ForegroundColor Gray

Write-Host ""
Write-Host "4. Unload Document project:" -ForegroundColor White
Write-Host "   - Click Unload Project in side menu" -ForegroundColor Gray
Write-Host "   - VERIFY: Debug logs show workspace context and view config clearing" -ForegroundColor Gray

Write-Host ""
Write-Host "5. Load Jama project again:" -ForegroundColor White
Write-Host "   - Open the test_jama_import.tcex.json file" -ForegroundColor Gray
Write-Host "   - Navigate to Requirements section" -ForegroundColor Gray
Write-Host "   - VERIFY: Header again shows simple requirement name" -ForegroundColor Gray
Write-Host "   - VERIFY: Main workspace again uses JamaRequirementsMainViewModel" -ForegroundColor Gray

Write-Host ""
Write-Host "🔧 Key Debug Logs to Watch For:" -ForegroundColor Magenta
Write-Host "  ✅ '[ViewConfigurationService] IsJamaDataSource() returned: {true|false}'" -ForegroundColor White
Write-Host "  ✅ '[ViewConfigurationService] Using {Jama-optimized|document} Requirements view'" -ForegroundColor White
Write-Host "  ✅ '🧹 Clearing workspace context cache on project unload'" -ForegroundColor White
Write-Host "  ✅ '🧹 Clearing view configuration on project unload'" -ForegroundColor White
Write-Host "  ✅ '[RequirementsMediator] IsJamaDataSource() - CurrentWorkspace: {status}'" -ForegroundColor White

Write-Host ""
Write-Host "📁 Test Files Available:" -ForegroundColor Blue
$jamaFile = "test_jama_import.tcex.json"
$docFile = "test_document_import.tcex.json"

if (Test-Path $jamaFile) {
    Write-Host "  ✅ $jamaFile (ImportSource: Jama)" -ForegroundColor Green
} else {
    Write-Host "  ❌ $jamaFile missing" -ForegroundColor Red
}

if (Test-Path $docFile) {
    Write-Host "  ✅ $docFile (ImportSource: Document)" -ForegroundColor Green
} else {
    Write-Host "  ❌ $docFile missing" -ForegroundColor Red
}

Write-Host ""
Write-Host "🎯 Expected Behavior:" -ForegroundColor Yellow
Write-Host "  - Header workspace should switch content based on ImportSource" -ForegroundColor White
Write-Host "  - Main workspace should switch ViewModels based on ImportSource" -ForegroundColor White  
Write-Host "  - Unload should clear cached workspace context and view configuration" -ForegroundColor White
Write-Host "  - Reload should create fresh view configuration based on new ImportSource" -ForegroundColor White

Write-Host ""
Write-Host "🚀 Ready for Testing!" -ForegroundColor Green
Write-Host "   Run this script, then manually test the scenario above." -ForegroundColor White