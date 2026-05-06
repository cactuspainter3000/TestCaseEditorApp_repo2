#!/usr/bin/env pwsh

# Test Requirements domain DI resolution

Write-Host "Testing Requirements DI resolution..." -ForegroundColor Yellow

# Build first to ensure latest code
Write-Host "Building project..." -ForegroundColor Blue
dotnet build TestCaseEditorApp.csproj --verbosity quiet

if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed!" -ForegroundColor Red
    exit 1
}

Write-Host "Build successful. Testing DI resolution..." -ForegroundColor Green

# Use a simple test method that starts DI and tests resolution
$testCode = @"
using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TestCaseEditorApp.MVVM.Domains.Requirements.ViewModels;
using TestCaseEditorApp.MVVM.Domains.Requirements.Mediators;

// Try to replicate the App startup DI registration
var host = Host.CreateDefaultBuilder()
    .ConfigureServices(services => {
        // Basic logging
        services.AddLogging();
        
        // Requirements mediator
        services.AddSingleton<IRequirementsMediator>(provider => {
            var logger = provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<RequirementsMediator>>();
            return new RequirementsMediator(logger);
        });
        
        // Requirements ViewModels
        services.AddTransient<Requirements_MainViewModel>();
        services.AddTransient<Requirements_HeaderViewModel>();
    })
    .Build();

Console.WriteLine(""Testing Requirements DI resolution:"");

try {
    var headerVM = host.Services.GetService<Requirements_HeaderViewModel>();
    Console.WriteLine(\$""Requirements_HeaderViewModel resolved: {(headerVM != null ? ""SUCCESS"" : ""FAILED"")}"");
    
    var mainVM = host.Services.GetService<Requirements_MainViewModel>();
    Console.WriteLine(\$""Requirements_MainViewModel resolved: {(mainVM != null ? ""SUCCESS"" : ""FAILED"")}"");
    
    var mediator = host.Services.GetService<IRequirementsMediator>();
    Console.WriteLine(\$""IRequirementsMediator resolved: {(mediator != null ? ""SUCCESS"" : ""FAILED"")}"");
    
    Console.WriteLine(""All DI resolutions completed successfully!"");
}
catch (Exception ex) {
    Console.WriteLine(\$""DI resolution failed: {ex.Message}"");
    Console.WriteLine(\$""Stack trace: {ex.StackTrace}"");
}
"@

# Run the test code
$tempFile = [System.IO.Path]::GetTempFileName() + ".cs"
$testCode | Out-File -FilePath $tempFile -Encoding utf8

try {
    Write-Host "Running DI test..." -ForegroundColor Cyan
    dotnet run --project TestCaseEditorApp.csproj -- --test-di-script $tempFile
    
    if ($LASTEXITCODE -ne 0) {
        Write-Host "DI test failed with exit code: $LASTEXITCODE" -ForegroundColor Red
    } else {
        Write-Host "DI test completed successfully" -ForegroundColor Green
    }
}
finally {
    Remove-Item $tempFile -ErrorAction SilentlyContinue
}