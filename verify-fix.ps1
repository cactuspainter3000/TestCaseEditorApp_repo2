Write-Host "🔍 Requirements Main Workspace Fix Verification" -ForegroundColor Cyan
Write-Host "=============================================" -ForegroundColor Cyan
Write-Host ""

# Check the key fix
$requirementsSingleton = Select-String -Path "App.xaml.cs" -Pattern "AddSingleton.*Requirements_MainViewModel" -Quiet
$navigationSingleton = Select-String -Path "App.xaml.cs" -Pattern "AddSingleton.*NavigationViewModel" -Quiet

if ($requirementsSingleton) {
    Write-Host "✅ Requirements_MainViewModel: Registered as Singleton" -ForegroundColor Green
} else {
    Write-Host "❌ Requirements_MainViewModel: NOT Singleton" -ForegroundColor Red
}

if ($navigationSingleton) {
    Write-Host "✅ NavigationViewModel: Registered as Singleton" -ForegroundColor Green  
} else {
    Write-Host "❌ NavigationViewModel: NOT Singleton" -ForegroundColor Red
}

Write-Host ""
Write-Host "🎯 Expected behavior after fix:" -ForegroundColor Yellow
Write-Host "  1. Create new project with Jama import" -ForegroundColor Gray
Write-Host "  2. Requirements load with proper names (Boundary Scan, etc.)" -ForegroundColor Gray
Write-Host "  3. Select different requirements in navigation" -ForegroundColor Gray
Write-Host "  4. Main workspace updates to show selected requirement details" -ForegroundColor Gray

Write-Host ""
if ($requirementsSingleton -and $navigationSingleton) {
    Write-Host "🎉 BOTH ViewModels are Singletons - Fix should work!" -ForegroundColor Green
    Write-Host "   The same ViewModel instance will now be used for both navigation" -ForegroundColor Green
    Write-Host "   and main workspace, ensuring selection changes are visible." -ForegroundColor Green
} else {
    Write-Host "⚠️  Fix incomplete - some ViewModels not Singleton" -ForegroundColor Red
}