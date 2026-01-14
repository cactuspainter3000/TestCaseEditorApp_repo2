#!/usr/bin/env pwsh

<#
.SYNOPSIS
Step-by-step DI Resolution Test - AI Guide Methodology

.DESCRIPTION
Tests each component in the DI chain individually to isolate the failure point.
#>

Write-Host "🔍 Step-by-Step DI Resolution Analysis" -ForegroundColor Cyan
Write-Host "=====================================" -ForegroundColor Cyan

# Create a temporary test file to verify DI resolution step by step
$testCode = @'
using System;
using Microsoft.Extensions.DependencyInjection;
using TestCaseEditorApp;

class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine("🧪 Testing DI Resolution Chain Step by Step");
        
        try
        {
            // Initialize app to set up DI container
            var app = new App();
            app.InitializeComponent();
            
            Console.WriteLine("✅ App initialized");
            
            if (App.ServiceProvider == null)
            {
                Console.WriteLine("❌ ServiceProvider is null!");
                return;
            }
            
            Console.WriteLine("✅ ServiceProvider exists");
            
            // Test 1: Basic logger resolution
            Console.WriteLine("\n🔍 Testing Logger resolution...");
            var logger = App.ServiceProvider.GetService<Microsoft.Extensions.Logging.ILogger<TestCaseEditorApp.MVVM.Domains.Dummy.ViewModels.DummyMainWorkspaceViewModel>>();
            Console.WriteLine($"Logger: {(logger != null ? "✅" : "❌")}");
            
            // Test 2: IDummyMediator resolution  
            Console.WriteLine("\n🔍 Testing IDummyMediator resolution...");
            var mediator = App.ServiceProvider.GetService<TestCaseEditorApp.MVVM.Domains.Dummy.Mediators.IDummyMediator>();
            Console.WriteLine($"IDummyMediator: {(mediator != null ? "✅" : "❌")}");
            
            if (mediator != null)
            {
                Console.WriteLine($"Mediator type: {mediator.GetType().Name}");
                
                // Check if it has IsRegistered property
                var isRegisteredProp = mediator.GetType().GetProperty("IsRegistered");
                if (isRegisteredProp != null)
                {
                    var isRegistered = (bool)isRegisteredProp.GetValue(mediator);
                    Console.WriteLine($"IsRegistered: {isRegistered}");
                }
                else
                {
                    Console.WriteLine("❌ No IsRegistered property found");
                }
            }
            
            // Test 3: Direct ViewModel resolution
            Console.WriteLine("\n🔍 Testing DummyMainWorkspaceViewModel resolution...");
            try
            {
                var viewModel = App.ServiceProvider.GetService<TestCaseEditorApp.MVVM.Domains.Dummy.ViewModels.DummyMainWorkspaceViewModel>();
                Console.WriteLine($"DummyMainWorkspaceViewModel: {(viewModel != null ? "✅ RESOLVED!" : "❌ NULL")}");
                
                if (viewModel != null)
                {
                    Console.WriteLine("🎉 SUCCESS: ViewModel resolution worked!");
                }
            }
            catch (Exception vmEx)
            {
                Console.WriteLine($"❌ ViewModel creation failed: {vmEx.Message}");
                Console.WriteLine($"Stack trace: {vmEx.StackTrace}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"💥 Error: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
        }
        
        Console.WriteLine("\nPress any key to exit...");
        Console.ReadKey();
    }
}
'@

Write-Host "📝 Creating DI test program..." -ForegroundColor Yellow
Set-Content -Path "DITest.cs" -Value $testCode

Write-Host "🔨 Compiling test..." -ForegroundColor Yellow
dotnet run --configuration Debug -- testdi 2>&1

# Clean up
Remove-Item "DITest.cs" -ErrorAction SilentlyContinue