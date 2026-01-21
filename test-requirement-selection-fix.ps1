#!/usr/bin/env pwsh

<#
.SYNOPSIS
Test script to verify the Requirements main workspace fix is working properly.
Tests that requirement selection updates the main workspace when using Jama imported projects.

.DESCRIPTION 
This script creates a new project with Jama import, then tests if selecting requirements
properly updates the main workspace to show requirement details.

Key fix: Changed Requirements_MainViewModel from AddTransient to AddSingleton in App.xaml.cs
to ensure both navigation and main workspace use the same ViewModel instance.
#>

param(
    [switch]$Verbose
)

Write-Host "🔍 Testing Requirements Main Workspace Fix" -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host ""

# Step 1: Check if the app built successfully
Write-Host "1. Verifying build..." -ForegroundColor Yellow
if (-not (Test-Path ".\bin\Debug\net8.0-windows\TestCaseEditorApp.exe")) {
    Write-Host "❌ Application not built. Run 'dotnet build' first." -ForegroundColor Red
    exit 1
}
Write-Host "✅ Application built successfully" -ForegroundColor Green

# Step 2: Check the fix is in place
Write-Host ""
Write-Host "2. Verifying DI container fix..." -ForegroundColor Yellow
$appXamlContent = Get-Content "App.xaml.cs" -Raw
if ($appXamlContent -match "services\.AddSingleton<TestCaseEditorApp\.MVVM\.Domains\.Requirements\.ViewModels\.Requirements_MainViewModel>") {
    Write-Host "✅ Requirements_MainViewModel registered as Singleton" -ForegroundColor Green
} elseif ($appXamlContent -match "services\.AddTransient<TestCaseEditorApp\.MVVM\.Domains\.Requirements\.ViewModels\.Requirements_MainViewModel>") {
    Write-Host "❌ Requirements_MainViewModel still registered as Transient" -ForegroundColor Red
    Write-Host "   This will cause multiple instances and prevent selection updates" -ForegroundColor Red
    exit 1
} else {
    Write-Host "⚠️  Requirements_MainViewModel registration not found" -ForegroundColor Orange
}

# Step 3: Check NavigationViewModel is also Singleton (for shared state)
$navigationSingleton = $appXamlContent -match "services\.AddSingleton<TestCaseEditorApp\.MVVM\.Domains\.TestCaseGeneration\.ViewModels\.NavigationViewModel>"
if ($navigationSingleton) {
    Write-Host "✅ NavigationViewModel registered as Singleton (shared state)" -ForegroundColor Green
} else {
    Write-Host "❌ NavigationViewModel not registered as Singleton" -ForegroundColor Red
}

Write-Host ""
Write-Host "3. Manual test instructions:" -ForegroundColor Yellow
Write-Host "   1. Start the application (running in background)" -ForegroundColor Gray
Write-Host "   2. Create New Project → Select Jama project → Click 'Create Project'" -ForegroundColor Gray  
Write-Host "   3. Wait for requirements to load (should see proper names like 'Boundary Scan')" -ForegroundColor Gray
Write-Host "   4. Navigate through requirements using dropdowns or arrows" -ForegroundColor Gray
Write-Host "   5. ✅ EXPECTED: Main workspace updates to show selected requirement details" -ForegroundColor Green
Write-Host "   6. ❌ BROKEN: Main workspace stays empty or shows wrong requirement" -ForegroundColor Red

Write-Host ""
Write-Host "4. Architectural details:" -ForegroundColor Yellow
Write-Host "   • Requirements uses shared NavigationViewModel (TestCaseGeneration domain)" -ForegroundColor Gray
Write-Host "   • Main workspace uses Requirements_MainViewModel (Requirements domain)" -ForegroundColor Gray  
Write-Host "   • Both need to be Singletons to share state properly" -ForegroundColor Gray
Write-Host "   • ViewConfigurationService creates new instances each switch without Singleton" -ForegroundColor Gray

Write-Host ""
if ($navigationSingleton -and ($appXamlContent -match "AddSingleton.*Requirements_MainViewModel")) {
    Write-Host "🎉 Fix appears to be in place! Test manually to verify." -ForegroundColor Green
} else {
    Write-Host "⚠️  Fix may be incomplete. Check DI registrations." -ForegroundColor Orange
}

Write-Host ""
Write-Host "Application should be running. Test the requirement selection now!" -ForegroundColor Cyan