#!/usr/bin/env pwsh

<#
.SYNOPSIS
    Test navigation to TestCaseCreation to debug ViewModel creation issues
#>

Write-Host "Testing TestCaseCreation navigation..." -ForegroundColor Green

# Build first to ensure no compilation errors
& dotnet build | Tee-Object -Variable buildOutput
if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed!" -ForegroundColor Red
    exit 1
}

Write-Host "Build successful, testing DI resolution..." -ForegroundColor Green

# Create a simple test program to verify DI resolution
$testProgram = @"
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TestCaseEditorApp.MVVM.Domains.TestCaseCreation.ViewModels;
using TestCaseEditorApp.MVVM.Domains.TestCaseCreation.Mediators;

var hostBuilder = Host.CreateDefaultBuilder()
    .ConfigureLogging(logging =>
    {
        logging.AddConsole();
    })
    .ConfigureServices((ctx, services) =>
    {
        // Copy the DI setup from App.xaml.cs for TestCaseCreation
        services.AddSingleton<TestCaseEditorApp.MVVM.Domains.TestCaseCreation.Mediators.ITestCaseCreationMediator, TestCaseEditorApp.MVVM.Domains.TestCaseCreation.Mediators.TestCaseCreationMediator>();
        services.AddTransient<TestCaseEditorApp.MVVM.Domains.TestCaseCreation.ViewModels.TestCaseCreationMainVM>();
        
        // Add minimal required dependencies
        services.AddSingleton<TestCaseEditorApp.Services.IDomainUICoordinator>(sp => 
            new TestCaseEditorApp.Services.DomainUICoordinator());
    });

var host = hostBuilder.Build();

try 
{
    Console.WriteLine("Testing mediator resolution...");
    var mediator = host.Services.GetService<TestCaseEditorApp.MVVM.Domains.TestCaseCreation.Mediators.ITestCaseCreationMediator>();
    Console.WriteLine(`$"Mediator resolved: {mediator != null}");
    
    Console.WriteLine("Testing logger resolution...");
    var logger = host.Services.GetService<Microsoft.Extensions.Logging.ILogger<TestCaseCreationMainVM>>();
    Console.WriteLine(`$"Logger resolved: {logger != null}");
    
    Console.WriteLine("Testing ViewModel resolution...");
    var viewModel = host.Services.GetService<TestCaseCreationMainVM>();
    Console.WriteLine(`$"ViewModel resolved: {viewModel != null}");
    Console.WriteLine(`$"ViewModel type: {viewModel?.GetType().Name}");
    
    Console.WriteLine("All tests passed!");
}
catch (Exception ex)
{
    Console.WriteLine(`$"ERROR: {ex.Message}");
    Console.WriteLine(`$"Stack trace: {ex.StackTrace}");
    Environment.Exit(1);
}
"@

# Write test to temp file and run it
$tempFile = [System.IO.Path]::GetTempFileName() + ".cs"
$testProgram | Out-File -FilePath $tempFile -Encoding UTF8

try {
    & dotnet script $tempFile
} finally {
    Remove-Item $tempFile -ErrorAction SilentlyContinue
}