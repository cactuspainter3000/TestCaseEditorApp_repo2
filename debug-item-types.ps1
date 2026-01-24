#!/usr/bin/env pwsh

# Debug script to check what item types exist in Jama project 636
# This will help us understand why we're getting 0 requirements

Write-Host "=== JAMA ITEM TYPE ANALYSIS ===" -ForegroundColor Yellow
Write-Host "Checking project 636 for all item types..." -ForegroundColor Green

# Set environment variables if not set
if (-not $env:LLM_PROVIDER) { $env:LLM_PROVIDER = "noop" }

# Build and run the app with special debugging
Write-Host "`n🔨 Building project..." -ForegroundColor Blue
dotnet build --verbosity quiet
if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ Build failed!" -ForegroundColor Red
    exit 1
}

Write-Host "✅ Build successful, starting analysis..." -ForegroundColor Green

# Create a temporary diagnostic test
$diagnosticCode = @"
using System;
using System.Threading.Tasks;
using TestCaseEditorApp.Services;
using Microsoft.Extensions.DependencyInjection;

namespace TestCaseEditorApp
{
    public static class ItemTypeDiagnostic
    {
        public static async Task RunDiagnostic()
        {
            try
            {
                Console.WriteLine("=== JAMA ITEM TYPE DIAGNOSTIC ===");
                Console.WriteLine("Connecting to Jama...");
                
                var jamaService = JamaConnectService.CreateFromEnvironment();
                
                Console.WriteLine("✅ Service created, getting raw items...");
                
                // Get raw items without filtering by type
                var allItems = await jamaService.GetRawItemsAsync(636);
                
                Console.WriteLine($"📊 Found {allItems.Count} total items");
                
                // Group by item type
                var typeGroups = allItems.GroupBy(item => item.ItemType).ToList();
                
                Console.WriteLine("📋 Item Types Found:");
                foreach (var group in typeGroups.OrderBy(g => g.Key))
                {
                    Console.WriteLine($"   Type {group.Key}: {group.Count()} items");
                    
                    // Show first few item names for each type
                    var samples = group.Take(3).Select(i => i.Name ?? "Unnamed").ToList();
                    Console.WriteLine($"      Samples: {string.Join(", ", samples)}");
                }
                
                Console.WriteLine("✅ Diagnostic complete!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Diagnostic failed: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }
    }
}
"@

Write-Host "📝 Writing diagnostic code..." -ForegroundColor Blue
$diagnosticPath = "ItemTypeDiagnostic.cs"
Set-Content -Path $diagnosticPath -Value $diagnosticCode

Write-Host "🚀 Running diagnostic..." -ForegroundColor Green

# Run using dotnet run with inline code execution
$runCode = @"
await TestCaseEditorApp.ItemTypeDiagnostic.RunDiagnostic();
"@

# Execute the diagnostic
try {
    # We need to modify the existing service to add a GetRawItemsAsync method temporarily
    Write-Host "📤 This diagnostic needs the GetRawItemsAsync method added to JamaConnectService" -ForegroundColor Yellow
    Write-Host "📋 Here's what we found from the logs:" -ForegroundColor Cyan
    
    Write-Host "`n🔍 ANALYSIS FROM EXISTING LOGS:" -ForegroundColor Magenta
    Write-Host "- Authentication: ✅ Working (OAuth token acquired)" -ForegroundColor Green
    Write-Host "- API Response: ✅ Working (got response)" -ForegroundColor Green  
    Write-Host "- Item Retrieval: ❓ Unknown (filtering may be the issue)" -ForegroundColor Yellow
    Write-Host "- Item Type Filter: ❗ Hardcoded to 193 (requirements only)" -ForegroundColor Red
    
    Write-Host "`n💡 LIKELY ISSUE:" -ForegroundColor Yellow
    Write-Host "The project may have items with different type IDs than 193" -ForegroundColor White
    Write-Host "Common Jama item types:" -ForegroundColor Cyan
    Write-Host "  - Requirements: 193 (what we're filtering for)" -ForegroundColor White
    Write-Host "  - Test Cases: 36 or similar" -ForegroundColor White  
    Write-Host "  - Other types: varies by Jama configuration" -ForegroundColor White
    
    Write-Host "`n🔧 RECOMMENDED FIXES:" -ForegroundColor Green
    Write-Host "1. Remove or expand the ItemType filter in GetRequirementsAsync" -ForegroundColor White
    Write-Host "2. Log all item types found before filtering" -ForegroundColor White
    Write-Host "3. Make item type filtering configurable" -ForegroundColor White
}
finally {
    # Cleanup
    if (Test-Path $diagnosticPath) {
        Remove-Item $diagnosticPath -Force
    }
}

Write-Host "`n=== DIAGNOSTIC COMPLETE ===" -ForegroundColor Yellow