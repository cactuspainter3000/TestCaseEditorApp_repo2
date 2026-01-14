#!/usr/bin/env pwsh
# test-startup-di-resolution.ps1 - Test if all StartUp ViewModels can be resolved from DI

Write-Host "=== Testing StartUp ViewModel DI Resolution ===" -ForegroundColor Cyan

# Build first
Write-Host "`nBuilding application..." -ForegroundColor Yellow
dotnet build --configuration Debug --verbosity quiet | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed!" -ForegroundColor Red
    exit 1
}
Write-Host "Build successful" -ForegroundColor Green

# Create a simple test program to check DI resolution
$testCode = @'
using Microsoft.Extensions.DependencyInjection;
using System;
using TestCaseEditorApp;

// Initialize DI just like the real app
var serviceCollection = new ServiceCollection();
TestCaseEditorApp.App.ConfigureServices(serviceCollection);
var serviceProvider = serviceCollection.BuildServiceProvider();

Console.WriteLine("=== Testing StartUp ViewModel Resolution ===");

try {
    // Test each StartUp ViewModel
    Console.WriteLine("\n1. Testing StartUp_MainViewModel...");
    var mainVM = serviceProvider.GetService<TestCaseEditorApp.MVVM.Domains.Startup.ViewModels.StartUp_MainViewModel>();
    Console.WriteLine($"   Result: {(mainVM != null ? "SUCCESS" : "FAILED")}");
    
    Console.WriteLine("\n2. Testing StartUp_HeaderViewModel...");
    var headerVM = serviceProvider.GetService<TestCaseEditorApp.MVVM.Domains.Startup.ViewModels.StartUp_HeaderViewModel>();
    Console.WriteLine($"   Result: {(headerVM != null ? "SUCCESS" : "FAILED")}");
    
    Console.WriteLine("\n3. Testing StartUp_NavigationViewModel...");
    var navVM = serviceProvider.GetService<TestCaseEditorApp.MVVM.Domains.Startup.ViewModels.StartUp_NavigationViewModel>();
    Console.WriteLine($"   Result: {(navVM != null ? "SUCCESS" : "FAILED")}");
    
    Console.WriteLine("\n4. Testing StartUp_TitleViewModel...");
    var titleVM = serviceProvider.GetService<TestCaseEditorApp.MVVM.Domains.Startup.ViewModels.StartUp_TitleViewModel>();
    Console.WriteLine($"   Result: {(titleVM != null ? "SUCCESS" : "FAILED")}");
    
    Console.WriteLine("\n5. Testing StartUp_NotificationViewModel...");
    var notificationVM = serviceProvider.GetService<TestCaseEditorApp.MVVM.Domains.Startup.ViewModels.StartUp_NotificationViewModel>();
    Console.WriteLine($"   Result: {(notificationVM != null ? "SUCCESS" : "FAILED")}");
    
    // Test ViewConfigurationService
    Console.WriteLine("\n6. Testing ViewConfigurationService...");
    var viewConfigService = serviceProvider.GetService<TestCaseEditorApp.Services.IViewConfigurationService>();
    Console.WriteLine($"   Result: {(viewConfigService != null ? "SUCCESS" : "FAILED")}");
    
    if (viewConfigService != null) {
        Console.WriteLine("\n7. Testing startup configuration creation...");
        try {
            var startupConfig = viewConfigService.GetConfigurationForSection("startup", null);
            Console.WriteLine($"   Startup config created: {(startupConfig != null ? "SUCCESS" : "FAILED")}");
            if (startupConfig != null) {
                Console.WriteLine($"   Section Name: {startupConfig.SectionName}");
                Console.WriteLine($"   Content ViewModel: {startupConfig.ContentViewModel?.GetType().Name}");
                Console.WriteLine($"   Header ViewModel: {startupConfig.HeaderViewModel?.GetType().Name}");
                Console.WriteLine($"   Navigation ViewModel: {startupConfig.NavigationViewModel?.GetType().Name}");
                Console.WriteLine($"   Title ViewModel: {startupConfig.TitleViewModel?.GetType().Name}");
                Console.WriteLine($"   Notification ViewModel: {startupConfig.NotificationViewModel?.GetType().Name}");
            }
        } catch (Exception ex) {
            Console.WriteLine($"   ERROR creating startup config: {ex.Message}");
        }
    }
    
    Console.WriteLine("\n=== All Tests Completed ===");
} catch (Exception ex) {
    Console.WriteLine($"FATAL ERROR: {ex.Message}");
    Console.WriteLine($"Stack Trace: {ex.StackTrace}");
}
'@

# Write the test code to a temporary file
$testFile = "StartupDITest.cs"
Set-Content -Path $testFile -Value $testCode

# Compile and run the test
Write-Host "`nRunning DI resolution test..." -ForegroundColor Yellow
try {
    # Reference the main DLLs needed
    $refs = @(
        ".\bin\Debug\net8.0-windows\TestCaseEditorApp.dll"
        ".\bin\Debug\net8.0-windows\Microsoft.Extensions.DependencyInjection.dll"
        ".\bin\Debug\net8.0-windows\Microsoft.Extensions.DependencyInjection.Abstractions.dll"
    )
    
    $refArgs = $refs | ForEach-Object { "-r `"$_`"" }
    $compileCmd = "dotnet run --project . -- compile $($refArgs -join ' ') $testFile"
    
    # Simpler approach - use dotnet-script if available, otherwise use csi
    if (Get-Command csi -ErrorAction SilentlyContinue) {
        $output = & csi $refArgs $testFile
        Write-Host $output -ForegroundColor White
    } else {
        Write-Host "CSI not available. Creating console app..." -ForegroundColor Yellow
        
        # Create a minimal console app project
        mkdir "TestStartupDI" -Force | Out-Null
        Set-Content -Path "TestStartupDI\TestStartupDI.csproj" -Value @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\TestCaseEditorApp.csproj" />
  </ItemGroup>
</Project>
'@
        Set-Content -Path "TestStartupDI\Program.cs" -Value $testCode
        
        # Build and run
        pushd "TestStartupDI"
        dotnet run
        popd
        
        # Cleanup
        Remove-Item "TestStartupDI" -Recurse -Force -ErrorAction SilentlyContinue
    }
} finally {
    # Cleanup
    Remove-Item $testFile -ErrorAction SilentlyContinue
}

Write-Host "`n=== Test Complete ===" -ForegroundColor Cyan