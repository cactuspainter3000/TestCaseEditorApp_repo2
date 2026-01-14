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

# Check mediator registration
$mediatorReg = Select-String -Path "App.xaml.cs" -Pattern "IDummyMediator|DummyMediator"
if ($mediatorReg) {
    Write-Host "  ✅ Found mediator registrations:" -ForegroundColor Green
    $mediatorReg | ForEach-Object { Write-Host "    $_" -ForegroundColor White }
} else {
    Write-Host "  ❌ No DummyMediator registration found!" -ForegroundColor Red
}

# Step 3: Create simple test
Write-Host "`n=== STEP 2: CREATING SIMPLE DI TEST ===" -ForegroundColor Yellow

$testScript = @"
using Microsoft.Extensions.DependencyInjection;
using System;

public static class QuickDITest 
{
    public static void RunTest()
    {
        Console.WriteLine("=== QUICK DI TEST ===");
        
        if (App.ServiceProvider == null) {
            Console.WriteLine("❌ App.ServiceProvider is NULL!");
            return;
        }
        
        Console.WriteLine("✅ App.ServiceProvider exists");
        Console.WriteLine(`"ServiceProvider type: `{App.ServiceProvider.GetType()}`");
        
        // Test basic logger
        try {
            var logger = App.ServiceProvider.GetService<Microsoft.Extensions.Logging.ILogger>();
            Console.WriteLine(`"Basic logger: `{(logger != null ? "✅" : "❌")}`");
        } catch (Exception ex) {
            Console.WriteLine(`"Logger error: `{ex.Message}`");
        }
        
        // Test Dummy ViewModels one by one
        try {
            var dummyMain = App.ServiceProvider.GetService<TestCaseEditorApp.MVVM.Domains.Dummy.ViewModels.DummyMainWorkspaceViewModel>();
            Console.WriteLine(`"DummyMainWorkspaceViewModel: `{(dummyMain != null ? "✅ SUCCESS" : "❌ NULL")}`");
        } catch (Exception ex) {
            Console.WriteLine(`"DummyMainWorkspaceViewModel ERROR: `{ex.Message}`");
        }
    }
}
"@

Set-Content -Path "QuickDITest.cs" -Value $testScript

# Step 4: Build 
Write-Host "`n=== STEP 3: BUILD TEST ===" -ForegroundColor Yellow
$buildResult = dotnet build --verbosity minimal
if ($LASTEXITCODE -eq 0) {
    Write-Host "  ✅ Build successful" -ForegroundColor Green
} else {
    Write-Host "  ❌ Build failed!" -ForegroundColor Red
    exit 1
}

# Step 5: Manual test suggestion
Write-Host "`n=== STEP 4: MANUAL TESTING NEEDED ===" -ForegroundColor Yellow
Write-Host "📝 To test DI resolution:" -ForegroundColor Cyan
Write-Host "  1. Add this line to App.xaml.cs OnStartup method:" -ForegroundColor White
Write-Host "     QuickDITest.RunTest();" -ForegroundColor Yellow
Write-Host "  2. Build and run the application" -ForegroundColor White
Write-Host "  3. Check console output for DI test results" -ForegroundColor White
Write-Host "  4. Remove the test line when done" -ForegroundColor White

Write-Host "`n🎯 OR run this quick test:" -ForegroundColor Cyan
Write-Host "dotnet run > di-output.txt 2>&1" -ForegroundColor Yellow

# Clean up
if (Test-Path "QuickDITest.cs") {
    Remove-Item "QuickDITest.cs" -Force
}

Write-Host "`n🎯 Next Steps Based on AI Guide:" -ForegroundColor Cyan
Write-Host "  - If ServiceProvider is NULL: App.xaml.cs startup issue" -ForegroundColor Yellow  
Write-Host "  - If basic services work but Dummy fails: Registration issue" -ForegroundColor Yellow
Write-Host "  - If constructor exceptions: Dependency chain broken" -ForegroundColor Yellow