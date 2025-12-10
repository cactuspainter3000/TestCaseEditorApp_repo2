#!/usr/bin/env pwsh
# Quick API test for AnythingLLM debugging

Write-Host "=== AnythingLLM API Detection Test ===" -ForegroundColor Cyan

# Test endpoints
$endpoints = @(
    "http://localhost:3001",
    "http://localhost:3001/api",
    "http://localhost:3001/api/workspaces",
    "http://localhost:3000",
    "http://localhost:3000/api",
    "http://localhost:3000/api/workspaces"
)

foreach ($endpoint in $endpoints) {
    Write-Host "`nTesting: $endpoint" -ForegroundColor Yellow
    
    try {
        $response = Invoke-WebRequest -Uri $endpoint -TimeoutSec 3 -ErrorAction Stop
        Write-Host "✅ Status: $($response.StatusCode)" -ForegroundColor Green
        
        if ($endpoint -match "workspaces") {
            $content = $response.Content | ConvertFrom-Json -ErrorAction SilentlyContinue
            if ($content.workspaces) {
                Write-Host "📋 Found $($content.workspaces.Count) workspaces:" -ForegroundColor Green
                $content.workspaces | ForEach-Object { Write-Host "  - $($_.name) (slug: $($_.slug))" }
            }
        }
    }
    catch [System.Net.WebException] {
        $statusCode = $_.Exception.Response.StatusCode
        Write-Host "⚠️  HTTP Error: $statusCode" -ForegroundColor Yellow
    }
    catch {
        Write-Host "❌ Error: $($_.Exception.Message)" -ForegroundColor Red
    }
}

Write-Host "`n=== Process Check ===" -ForegroundColor Cyan
$anythingLLM = Get-Process -Name "*AnythingLLM*" -ErrorAction SilentlyContinue
if ($anythingLLM) {
    Write-Host "✅ AnythingLLM process found:" -ForegroundColor Green
    $anythingLLM | ForEach-Object { Write-Host "  PID $($_.Id): $($_.ProcessName)" }
} else {
    Write-Host "❌ No AnythingLLM process found" -ForegroundColor Red
}

Write-Host "`n=== Port Check ===" -ForegroundColor Cyan
$ports = netstat -an | Select-String ":300[0-1]"
if ($ports) {
    Write-Host "✅ Ports in 3000-3001 range:" -ForegroundColor Green
    $ports | ForEach-Object { Write-Host "  $($_.Line)" }
} else {
    Write-Host "❌ No listeners on ports 3000-3001" -ForegroundColor Red
}