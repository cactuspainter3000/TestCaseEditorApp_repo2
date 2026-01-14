#!/usr/bin/env pwsh
#
# Diagnostic script to identify title display issues
#

Write-Host "🔍 TITLE DISPLAY DIAGNOSTIC" -ForegroundColor Cyan
Write-Host ""

# Test 1: Check if StartupTitleVM binding is correct
Write-Host "1. Checking StartupTitleVM configuration..." -ForegroundColor Yellow
$titleVMContent = Get-Content "MVVM\Domains\Startup\ViewModels\StartupTitleVM.cs" -Raw
if ($titleVMContent -match 'Title\s*=\s*"([^"]+)"') {
    Write-Host "   ✅ StartupTitleVM.Title = '$($matches[1])'" -ForegroundColor Green
} else {
    Write-Host "   ❌ StartupTitleVM.Title not found" -ForegroundColor Red
}

# Test 2: Check if DataTemplate is registered  
Write-Host "2. Checking DataTemplate registration..." -ForegroundColor Yellow
$mainWindowContent = Get-Content "MVVM\Views\MainWindow.xaml" -Raw
if ($mainWindowContent -match 'StartupTitleVM.*StartupTitleView') {
    Write-Host "   ✅ DataTemplate registered: StartupTitleVM → StartupTitleView" -ForegroundColor Green
} else {
    Write-Host "   ❌ DataTemplate NOT registered" -ForegroundColor Red
}

# Test 3: Check if MainViewModel.DisplayName is set
Write-Host "3. Checking MainViewModel.DisplayName..." -ForegroundColor Yellow
$mainVMContent = Get-Content "MVVM\ViewModels\MainViewModel.cs" -Raw
if ($mainVMContent -match '_displayName\s*=\s*"([^"]+)"') {
    Write-Host "   ✅ MainViewModel.DisplayName = '$($matches[1])'" -ForegroundColor Green
} else {
    Write-Host "   ❌ MainViewModel.DisplayName not found" -ForegroundColor Red
}

# Test 4: Check if Window.Title binding exists
Write-Host "4. Checking Window.Title binding..." -ForegroundColor Yellow
if ($mainWindowContent -match 'Title\s*=\s*"[^"]*Binding[^"]*DisplayName[^"]*"') {
    Write-Host "   ✅ Window.Title bound to DisplayName" -ForegroundColor Green
} else {
    Write-Host "   ❌ Window.Title binding issue" -ForegroundColor Red
}

# Test 5: Check if TitleWorkspace binding exists
Write-Host "5. Checking TitleWorkspace binding..." -ForegroundColor Yellow
if ($mainWindowContent -match 'Content\s*=\s*"[^"]*Binding[^"]*TitleWorkspace[^"]*"') {
    Write-Host "   ✅ TitleWorkspace content binding found" -ForegroundColor Green
} else {
    Write-Host "   ❌ TitleWorkspace binding issue" -ForegroundColor Red
}

Write-Host ""
Write-Host "🔬 DIAGNOSIS:" -ForegroundColor Cyan
Write-Host "If you see window controls but no title, the issue is likely:" -ForegroundColor White
Write-Host "• OS Window Title: Should come from MainViewModel.DisplayName" -ForegroundColor White  
Write-Host "• TitleWorkspace Area: Should come from StartupTitleView" -ForegroundColor White
Write-Host "• Window Controls: Are hardcoded in MainWindow.xaml (separate from title)" -ForegroundColor White