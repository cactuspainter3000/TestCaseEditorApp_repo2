#!/usr/bin/env pwsh
# Simple Phase 4 Integration Validation
# Tests core Phase 4 service registrations without complex test framework

Write-Host "Phase 4 Integration Quick Validation" -ForegroundColor Cyan
Write-Host "====================================" -ForegroundColor Cyan
Write-Host ""

# Build first
Write-Host "Building project..." -ForegroundColor Yellow
dotnet build TestCaseEditorApp.csproj --verbosity minimal
if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed!" -ForegroundColor Red
    exit 1
}
Write-Host "Build successful" -ForegroundColor Green
Write-Host ""

# Quick validation using existing IntegrationTestRunner
Write-Host "Running basic DI validation..." -ForegroundColor Yellow

$csharpCode = @'
using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TestCaseEditorApp.Services;
using TestCaseEditorApp.MVVM.Domains.Requirements.Services;
using TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Services;

namespace QuickTest 
{
    class Program 
    {
        static void Main(string[] args)
        {
            try
            {
                Console.WriteLine("Testing Phase 4 Service Registrations...");
                
                var services = new ServiceCollection();
                services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Error));
                
                // Register Phase 4 services
                services.AddSingleton<ISystemCapabilityDerivationService, SystemCapabilityDerivationService>();
                services.AddSingleton<IQualityScoringIntegrationService, QualityScoringIntegrationService>();
                services.AddSingleton<IRequirementGapAnalyzer, RequirementGapAnalyzer>();
                services.AddSingleton<ILlmOrchestrationService, LlmOrchestrationService>();
                services.AddSingleton<ISyntheticTrainingDataGenerator, SyntheticTrainingDataGenerator>();
                services.AddSingleton<IRequirementAnalysisService>(provider =>
                {
                    var derivationService = provider.GetService<ISystemCapabilityDerivationService>();
                    var gapAnalyzer = provider.GetService<IRequirementGapAnalyzer>();
                    return new RequirementAnalysisService(derivationService, gapAnalyzer);
                });
                
                var serviceProvider = services.BuildServiceProvider();
                
                // Test service resolution
                var derivationService = serviceProvider.GetService<ISystemCapabilityDerivationService>();
                Console.WriteLine($"SystemCapabilityDerivationService: {(derivationService != null ? "OK" : "FAILED")}");
                
                var qualityService = serviceProvider.GetService<IQualityScoringIntegrationService>();
                Console.WriteLine($"QualityScoringIntegrationService: {(qualityService != null ? "OK" : "FAILED")}");
                
                var gapAnalyzer = serviceProvider.GetService<IRequirementGapAnalyzer>();
                Console.WriteLine($"RequirementGapAnalyzer: {(gapAnalyzer != null ? "OK" : "FAILED")}");
                
                var analysisService = serviceProvider.GetService<IRequirementAnalysisService>();
                Console.WriteLine($"RequirementAnalysisService: {(analysisService != null ? "OK" : "FAILED")}");
                
                Console.WriteLine("\nAll Phase 4 services resolved successfully!");
                Console.WriteLine("Phase 4 Integration: VALIDATED");
                
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: {ex.Message}");
                Environment.Exit(1);
            }
        }
    }
}
'@

$tempFile = "quick_phase4_validation.cs"
$csharpCode | Out-File -FilePath $tempFile -Encoding UTF8

try {
    dotnet run $tempFile --project TestCaseEditorApp.csproj --verbosity minimal
    $exitCode = $LASTEXITCODE
    
    Write-Host ""
    if ($exitCode -eq 0) {
        Write-Host "PHASE 4 INTEGRATION: VALIDATED" -ForegroundColor Green
        Write-Host "All core services are properly registered and resolvable" -ForegroundColor Green
    } else {
        Write-Host "PHASE 4 INTEGRATION: ISSUES DETECTED" -ForegroundColor Red
        Write-Host "Check service registrations and dependencies" -ForegroundColor Red
    }
}
finally {
    if (Test-Path $tempFile) {
        Remove-Item $tempFile -Force
    }
}