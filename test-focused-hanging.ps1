#!/usr/bin/env pwsh
# Focused test for the hanging GetRequirementsWithUserMetadataAsync method

Write-Host "=== FOCUSED JAMA API TEST ===" -ForegroundColor Yellow

# Set environment variables
$env:JAMA_BASE_URL = "https://jama02.rockwellcollins.com/contour"
$env:JAMA_CLIENT_ID = "hycv5tyzpvyvhmi" 
$env:JAMA_CLIENT_SECRET = "Wy+qLYTczFkxwZIhJJ/I4Q=="

Write-Host "Environment configured" -ForegroundColor Green

# Create simple C# test program
$testCode = @'
using System;
using System.Threading.Tasks;
using TestCaseEditorApp.Services;
using Microsoft.Extensions.DependencyInjection;
using TestCaseEditorApp;

Console.WriteLine("[TEST] Starting focused API test...");

try {
    Console.WriteLine("[TEST] Creating JamaConnectService...");
    var jamaService = new JamaConnectService();
    
    Console.WriteLine("[TEST] Testing connection first...");
    var (isConnected, message) = await jamaService.TestConnectionAsync();
    Console.WriteLine($"[TEST] Connection test: {isConnected} - {message}");
    
    if (!isConnected) {
        Console.WriteLine("[TEST] Connection failed, stopping test");
        return;
    }
    
    Console.WriteLine("[TEST] Connection successful, now testing GetRequirementsWithUserMetadataAsync...");
    
    // This is the hanging call
    var requirements = await jamaService.GetRequirementsWithUserMetadataAsync(636);
    Console.WriteLine($"[TEST] ✅ SUCCESS! Retrieved {requirements.Count} requirements");
    
} catch (Exception ex) {
    Console.WriteLine($"[TEST] ❌ ERROR: {ex.Message}");
    Console.WriteLine($"[TEST] Stack trace: {ex.StackTrace}");
}

Console.WriteLine("[TEST] Test complete");
'@

# Save test program
$testCode | Set-Content -Path "FocusedTest.cs" -Encoding UTF8

Write-Host "Running focused test..." -ForegroundColor Cyan

# Run the test with timeout
$process = Start-Process -FilePath "dotnet" -ArgumentList "run --verbosity minimal --project . FocusedTest.cs" -NoNewWindow -PassThru

Write-Host "Waiting for test to complete (30 second timeout)..." -ForegroundColor Yellow

if ($process.WaitForExit(30000)) {
    Write-Host "Test completed within timeout" -ForegroundColor Green
    Write-Host "Exit Code: $($process.ExitCode)" -ForegroundColor Cyan
} else {
    Write-Host "TEST HUNG - Killing process" -ForegroundColor Red
    $process.Kill()
    Write-Host "Process killed - confirms hanging issue" -ForegroundColor Red
}

# Clean up
if (Test-Path "FocusedTest.cs") {
    Remove-Item "FocusedTest.cs"
}

Write-Host "=== FOCUSED TEST COMPLETE ===" -ForegroundColor Yellow