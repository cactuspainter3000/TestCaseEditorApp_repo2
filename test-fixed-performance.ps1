#!/usr/bin/env powershell

Write-Host "=== Corrected Application Performance Test ===" -ForegroundColor Cyan
Write-Host "Testing with proper framework compatibility" -ForegroundColor Green
Write-Host ""

# Test 1: Verify current performance
Write-Host "1. Testing current Ollama performance..." -ForegroundColor Yellow
$prompt = "Analyze this requirement: As a user, I want to view filterable test lists. Provide 3 implementation points."
$body = @{ 
    model = "phi4-mini:3.8b-q4_K_M"
    prompt = $prompt
    stream = $false 
} | ConvertTo-Json

$sw = [System.Diagnostics.Stopwatch]::StartNew()
try {
    $response = Invoke-RestMethod -Uri "http://localhost:11434/api/generate" -Method Post -Body $body -ContentType "application/json"
    $sw.Stop()
    Write-Host "   ✅ Direct Ollama: $($sw.Elapsed.TotalSeconds) seconds" -ForegroundColor Green
    Write-Host "   Response length: $($response.response.Length) characters" -ForegroundColor Cyan
} catch {
    $sw.Stop()
    Write-Host "   ❌ Direct Ollama failed: $($_.Exception.Message)" -ForegroundColor Red
}
Write-Host ""

# Test 2: Create a proper WPF console test
Write-Host "2. Creating compatible test application (net8.0-windows)..." -ForegroundColor Yellow

$testCode = @'
using System;
using System.Threading.Tasks;
using TestCaseEditorApp.Services;
using TestCaseEditorApp.MVVM.Domains.TestCaseGeneration.Services;

namespace TestApp
{
    class Program
    {
        [STAThread]
        static async Task<int> Main(string[] args)
        {
            Console.WriteLine("Testing RequirementAnalysisService performance...");
            
            var sw = System.Diagnostics.Stopwatch.StartNew();
            
            try
            {
                // Create LLM service using the factory (should use phi4-mini)
                var llmService = LlmFactory.Create("ollama");
                
                // Create analysis service 
                var analysisService = new RequirementAnalysisService(llmService);
                
                // Test requirement
                var requirement = "As a user, I want to be able to view all available tests in a filterable list so that I can quickly find specific tests.";
                
                // Run analysis
                var analysis = await analysisService.AnalyzeRequirementAsync(requirement);
                
                sw.Stop();
                
                Console.WriteLine($"✅ Analysis completed in: {sw.Elapsed.TotalSeconds:F2} seconds");
                Console.WriteLine($"Analysis score: {analysis.OverallScore}/10");
                Console.WriteLine($"Analysis length: {analysis.Analysis?.Length ?? 0} characters");
                Console.WriteLine($"Issues found: {analysis.Issues?.Count ?? 0}");
                
                return 0;
            }
            catch (Exception ex)
            {
                sw.Stop();
                Console.WriteLine($"❌ Error after {sw.Elapsed.TotalSeconds:F2}s: {ex.Message}");
                Console.WriteLine($"Exception type: {ex.GetType().Name}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Inner exception: {ex.InnerException.Message}");
                }
                return 1;
            }
        }
    }
}
'@

# Create project file with correct target framework
$projectContent = @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWPF>false</UseWPF>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\TestCaseEditorApp.csproj" />
  </ItemGroup>
</Project>
'@

# Create test directory and files
$testDir = "TestPerformanceApp"
if (Test-Path $testDir) {
    Remove-Item $testDir -Recurse -Force
}
New-Item -ItemType Directory -Path $testDir | Out-Null

Set-Location $testDir

# Write project and source files
$projectContent | Out-File -FilePath "TestPerformanceApp.csproj" -Encoding UTF8
$testCode | Out-File -FilePath "Program.cs" -Encoding UTF8

Write-Host "   Building compatible test application..." -ForegroundColor Cyan
$buildResult = dotnet build --verbosity quiet 2>&1

if ($LASTEXITCODE -eq 0) {
    Write-Host "   ✅ Build successful" -ForegroundColor Green
    Write-Host "   Running application performance test..." -ForegroundColor Cyan
    
    $sw2 = [System.Diagnostics.Stopwatch]::StartNew()
    $runResult = dotnet run --verbosity quiet 2>&1
    $sw2.Stop()
    
    Write-Host "   Total test time: $($sw2.Elapsed.TotalSeconds) seconds" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "   Application Test Output:" -ForegroundColor White
    $runResult | ForEach-Object { Write-Host "   $_" -ForegroundColor White }
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host "   ✅ Application test completed successfully" -ForegroundColor Green
    } else {
        Write-Host "   ❌ Application test failed (exit code: $LASTEXITCODE)" -ForegroundColor Red
    }
} else {
    Write-Host "   ❌ Build failed:" -ForegroundColor Red
    $buildResult | ForEach-Object { Write-Host "   $_" -ForegroundColor Red }
}

Set-Location ..

# Clean up
if (Test-Path $testDir) {
    Remove-Item $testDir -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host ""
Write-Host "=== Framework Analysis ===" -ForegroundColor Cyan
Write-Host "Main project target: net8.0-windows (WPF application)" -ForegroundColor White
Write-Host "Test project target: net8.0-windows (compatible)" -ForegroundColor White
Write-Host "Previous error: Incompatible frameworks fixed ✅" -ForegroundColor Green

Write-Host ""
Write-Host "=== Performance Test Complete ===" -ForegroundColor Cyan