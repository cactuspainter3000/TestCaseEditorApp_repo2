#!/usr/bin/env pwsh

# DI Diagnostics: Following AI Guide "Questions First, Code Second" Methodology
# Purpose: Identify exact DI resolution failure point using existing patterns

Write-Host "🔍 DI DIAGNOSTIC: Following AI Guide Patterns" -ForegroundColor Cyan

# Step 1: Analysis of Existing Code - Find Working Examples
Write-Host "`n=== STEP 1: ANALYZING EXISTING DI PATTERNS ===" -ForegroundColor Yellow

Write-Host "`n🔍 Searching for working App.ServiceProvider patterns..."
$workingPatterns = Select-String -Path "*.cs" -Recursive -Pattern "App\.ServiceProvider\?\.GetService" | Select-Object -First 5
$workingPatterns | ForEach-Object { Write-Host "  ✅ $_" -ForegroundColor Green }

# Step 2: Check DI Registration Patterns
Write-Host "`n🔍 Checking DI registration patterns in App.xaml.cs..."
$dummyRegistrations = Select-String -Path "App.xaml.cs" -Pattern "Dummy.*ViewModel"
if ($dummyRegistrations) {
    Write-Host "  ✅ Found Dummy ViewModel registrations:" -ForegroundColor Green
    $dummyRegistrations | ForEach-Object { Write-Host "    $_" -ForegroundColor White }
} else {
    Write-Host "  ❌ No Dummy ViewModel registrations found!" -ForegroundColor Red
}

# Step 3: Verify Complete Dependency Chains
Write-Host "`n🔍 Checking Dummy domain dependency chains..."

# Check mediator registration
$mediatorReg = Select-String -Path "App.xaml.cs" -Pattern "IDummyMediator|DummyMediator"
if ($mediatorReg) {
    Write-Host "  ✅ Found mediator registrations:" -ForegroundColor Green
    $mediatorReg | ForEach-Object { Write-Host "    $_" -ForegroundColor White }
} else {
    Write-Host "  ❌ No DummyMediator registration found!" -ForegroundColor Red
}

# Step 4: Build and Launch for Runtime Testing
Write-Host "`n=== STEP 2: BUILD VALIDATION ===" -ForegroundColor Yellow

Write-Host "🔨 Building application..."
$buildResult = dotnet build --verbosity minimal
if ($LASTEXITCODE -eq 0) {
    Write-Host "  ✅ Build successful" -ForegroundColor Green
} else {
    Write-Host "  ❌ Build failed!" -ForegroundColor Red
    Write-Host "Build output:" -ForegroundColor Red
    Write-Host $buildResult -ForegroundColor Red
    exit 1
}

# Step 5: Runtime DI Testing using existing patterns
Write-Host "`n=== STEP 3: RUNTIME DI RESOLUTION TESTING ===" -ForegroundColor Yellow

# Create a minimal test using the exact same pattern as ViewConfigurationService
Write-Host "📝 Creating DI test file using exact ViewConfigurationService patterns..."

$testContent = @"
using Microsoft.Extensions.DependencyInjection;
using System;
using TestCaseEditorApp.MVVM.Domains.Dummy.ViewModels;

namespace TestCaseEditorApp.Tests
{
    public class DIResolutionTest
    {
        public static void TestDummyResolution()
        {
            Console.WriteLine("=== DI RESOLUTION TEST ===");
            
            try
            {
                // Using EXACT same pattern as ViewConfigurationService.cs line 226
                var dummyMainVM = App.ServiceProvider?.GetService<DummyMainWorkspaceViewModel>();
                Console.WriteLine($"DummyMainWorkspaceViewModel resolution: {(dummyMainVM != null ? "✅ SUCCESS" : "❌ FAILED - NULL")}");
                
                var dummyHeaderVM = App.ServiceProvider?.GetService<DummyHeaderWorkspaceViewModel>();
                Console.WriteLine($"DummyHeaderWorkspaceViewModel resolution: {(dummyHeaderVM != null ? "✅ SUCCESS" : "❌ FAILED - NULL")}");
                
                var dummyNavigationVM = App.ServiceProvider?.GetService<DummyNavigationWorkspaceViewModel>();
                Console.WriteLine($"DummyNavigationWorkspaceViewModel resolution: {(dummyNavigationVM != null ? "✅ SUCCESS" : "❌ FAILED - NULL")}");
                
                var dummyTitleVM = App.ServiceProvider?.GetService<DummyTitleWorkspaceViewModel>();
                Console.WriteLine($"DummyTitleWorkspaceViewModel resolution: {(dummyTitleVM != null ? "✅ SUCCESS" : "❌ FAILED - NULL")}");
                
                var dummyNotificationVM = App.ServiceProvider?.GetService<DummyNotificationWorkspaceViewModel>();
                Console.WriteLine($"DummyNotificationWorkspaceViewModel resolution: {(dummyNotificationVM != null ? "✅ SUCCESS" : "❌ FAILED - NULL")}");
                
                // Test ServiceProvider itself
                Console.WriteLine($"App.ServiceProvider is null: {App.ServiceProvider == null}");
                
                if (App.ServiceProvider != null)
                {
                    // Test if ANY service can be resolved
                    var logger = App.ServiceProvider.GetService<Microsoft.Extensions.Logging.ILogger>();
                    Console.WriteLine($"Basic logger resolution: {(logger != null ? "✅ SUCCESS" : "❌ FAILED")}");
                    
                    // List all registered services (if possible)
                    var serviceProvider = App.ServiceProvider as Microsoft.Extensions.DependencyInjection.ServiceCollection;
                    Console.WriteLine($"ServiceProvider type: {App.ServiceProvider.GetType()}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ EXCEPTION during DI resolution: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }
    }
}
"@

Set-Content -Path "DIResolutionTest.cs" -Value $testContent

# Step 6: Add test call to App.xaml.cs temporarily
Write-Host "📝 Adding test call to App.xaml.cs..."

# Read current App.xaml.cs
$appContent = Get-Content "App.xaml.cs" -Raw

# Find the end of OnStartup method and add test call
if ($appContent -match 'OnStartup.*?\{.*?\}') {
    # Add test call before the closing brace of OnStartup  
    $modifiedContent = $appContent -replace '(\s+)(\})(\s*$)', '$1    // DI Diagnostic Test$1    TestCaseEditorApp.Tests.DIResolutionTest.TestDummyResolution();$1$2$3'
    Set-Content -Path "App.xaml.cs.backup" -Value $appContent
    Set-Content -Path "App.xaml.cs" -Value $modifiedContent
    Write-Host "  ✅ Added DI test call to OnStartup method" -ForegroundColor Green
} else {
    Write-Host "  ❌ Could not find OnStartup method to modify" -ForegroundColor Red
}

# Step 7: Build and Run with DI Testing
Write-Host "`n🚀 Building with DI test..."
$buildResult2 = dotnet build --verbosity minimal
if ($LASTEXITCODE -ne 0) {
    Write-Host "  ❌ Build failed with test code!" -ForegroundColor Red
    # Restore original App.xaml.cs
    if (Test-Path "App.xaml.cs.backup") {
        Copy-Item "App.xaml.cs.backup" "App.xaml.cs" -Force
        Remove-Item "App.xaml.cs.backup" -Force
        Write-Host "  🔄 Restored original App.xaml.cs" -ForegroundColor Yellow
    }
    exit 1
}

Write-Host "`n🎯 LAUNCHING APPLICATION WITH DI DIAGNOSTICS..." -ForegroundColor Cyan
Write-Host "   Watch console output for DI resolution test results!" -ForegroundColor Yellow
Write-Host "   Application will launch and show diagnostic info in console" -ForegroundColor Yellow

# Run the application
Start-Process -FilePath "dotnet" -ArgumentList "run" -NoNewWindow -Wait

# Cleanup
Write-Host "`n🧹 Cleaning up test files..."
if (Test-Path "App.xaml.cs.backup") {
    Copy-Item "App.xaml.cs.backup" "App.xaml.cs" -Force
    Remove-Item "App.xaml.cs.backup" -Force
    Write-Host "  ✅ Restored original App.xaml.cs" -ForegroundColor Green
}
if (Test-Path "DIResolutionTest.cs") {
    Remove-Item "DIResolutionTest.cs" -Force
    Write-Host "  ✅ Removed test file" -ForegroundColor Green
}

Write-Host "`n🎯 DIAGNOSTIC COMPLETE" -ForegroundColor Cyan
Write-Host "   Check console output above for DI resolution results" -ForegroundColor Yellow
Write-Host "   If all show FAILED-NULL, the issue is in DI container setup" -ForegroundColor Yellow
Write-Host "   If some work but Dummy ones fail, the issue is Dummy-specific registration" -ForegroundColor Yellow