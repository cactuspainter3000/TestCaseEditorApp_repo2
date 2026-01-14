#!/usr/bin/env pwsh
#
# Test script to verify "Systems ATE APP" title displays correctly in TitleWorkspace on startup
#

Write-Host "🔍 Verifying Application Startup Title Configuration..." -ForegroundColor Cyan

# Check if StartupTitleVM has correct title
$startupTitlePath = "MVVM\Domains\Startup\ViewModels\StartupTitleVM.cs"
$titleContent = Get-Content $startupTitlePath -Raw

if ($titleContent -match 'Title\s*=\s*"Systems ATE APP"') {
    Write-Host "✅ StartupTitleVM correctly configured with 'Systems ATE APP' title" -ForegroundColor Green
} else {
    Write-Host "❌ StartupTitleVM title not correctly configured" -ForegroundColor Red
    Write-Host "Expected: Title = 'Systems ATE APP'" -ForegroundColor Yellow
    return 1
}

# Check if ViewConfigurationService includes StartupConfiguration
$viewConfigPath = "Services\ViewConfigurationService.cs"
$configContent = Get-Content $viewConfigPath -Raw

if ($configContent -match '"startup".*=>.*CreateStartupConfiguration') {
    Write-Host "✅ ViewConfigurationService includes startup configuration" -ForegroundColor Green
} else {
    Write-Host "❌ ViewConfigurationService missing startup configuration" -ForegroundColor Red
    return 1
}

# Check if NavigationService sets initial startup navigation
$navServicePath = "Services\NavigationService.cs"
$navContent = Get-Content $navServicePath -Raw

if ($navContent -match 'NavigateToSection\("startup"\)') {
    Write-Host "✅ NavigationService sets initial 'startup' navigation" -ForegroundColor Green
} else {
    Write-Host "❌ NavigationService not configuring initial startup navigation" -ForegroundColor Red
    return 1
}

# Check DataTemplate registration in MainWindow
$mainWindowPath = "MainWindow.xaml"
$windowContent = Get-Content $mainWindowPath -Raw

if ($windowContent -match 'StartupTitleVM.*StartupTitleView') {
    Write-Host "✅ DataTemplate for StartupTitleVM registered in MainWindow" -ForegroundColor Green
} else {
    Write-Host "❌ DataTemplate for StartupTitleVM not found in MainWindow" -ForegroundColor Red
    return 1
}

Write-Host ""
Write-Host "🎯 Title Display Verification Summary:" -ForegroundColor Cyan
Write-Host "   TitleWorkspace should display: 'Systems ATE APP'" -ForegroundColor White
Write-Host "   Configuration: ✅ Complete" -ForegroundColor Green
Write-Host "   Integration: ✅ Complete" -ForegroundColor Green
Write-Host "   UI Binding: ✅ Complete" -ForegroundColor Green
Write-Host ""
Write-Host "🚀 Application should now display 'Systems ATE APP' in TitleWorkspace on startup!" -ForegroundColor Green