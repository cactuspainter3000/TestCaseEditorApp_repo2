#!/usr/bin/env pwsh
# Quick diagnostic test to verify TestCaseGenerator configuration is working

Write-Host "Testing TestCaseGenerator configuration fix..." -ForegroundColor Green

$testCode = @'
using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace TestCaseEditorApp
{
    public class TestCaseGeneratorDiagnostic
    {
        public static void RunTest()
        {
            try
            {
                Console.WriteLine("=== TestCaseGenerator Configuration Test ===");
                
                var serviceCollection = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
                
                // Add minimal logging
                serviceCollection.AddLogging(builder => builder.AddConsole());
                
                // Build services
                var services = serviceCollection.BuildServiceProvider();
                
                // Test ViewConfigurationService creation
                var viewConfigService = new TestCaseEditorApp.Services.ViewConfigurationService(services);
                Console.WriteLine("✓ ViewConfigurationService created successfully");
                
                // Test TestCaseGenerator configuration
                var config = viewConfigService.GetConfigurationForSection("testcasegenerator");
                Console.WriteLine($"✓ TestCaseGenerator config retrieved: {config?.SectionName}");
                
                if (config != null)
                {
                    Console.WriteLine($"   Title ViewModel: {config.TitleViewModel?.GetType().Name ?? "NULL"}");
                    Console.WriteLine($"   Header ViewModel: {config.HeaderViewModel?.GetType().Name ?? "NULL"}");
                    Console.WriteLine($"   Content ViewModel: {config.ContentViewModel?.GetType().Name ?? "NULL"}");
                    Console.WriteLine($"   Navigation ViewModel: {config.NavigationViewModel?.GetType().Name ?? "NULL"}");
                    Console.WriteLine($"   Notification ViewModel: {config.NotificationViewModel?.GetType().Name ?? "NULL"}");
                }
                else
                {
                    Console.WriteLine("❌ Configuration was null!");
                }
                
                Console.WriteLine("=== Test Complete ===");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Exception: {ex.GetType().Name}: {ex.Message}");
                Console.WriteLine($"   Stack: {ex.StackTrace}");
            }
        }
    }
}
'@

$tempFile = [System.IO.Path]::GetTempFileName() + ".cs"
Set-Content -Path $tempFile -Value $testCode
Write-Host "Created test file: $tempFile"

try {
    # Build and run the test
    $result = dotnet run --project TestCaseEditorApp.csproj --no-build -- --test-mode 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Application failed to start. Output:" -ForegroundColor Red
        Write-Host $result
    } else {
        Write-Host "Application started successfully!" -ForegroundColor Green
    }
} catch {
    Write-Host "Error running test: $_" -ForegroundColor Red
} finally {
    # Clean up
    Remove-Item $tempFile -ErrorAction SilentlyContinue
}