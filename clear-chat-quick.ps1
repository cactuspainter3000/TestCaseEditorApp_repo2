#!/usr/bin/env pwsh

<#
.SYNOPSIS
Quick AnythingLLM chat cleaner

.DESCRIPTION
Simple script to quickly clear AnythingLLM chat content
#>

param(
    [string]$WorkspaceName = "Test Case Editor - New Project 2026"
)

$ApiKey = $env:ANYTHINGLM_API_KEY
$AnythingLLMUrl = "http://localhost:3001"

if (-not $ApiKey) {
    Write-Host "❌ Set ANYTHINGLM_API_KEY environment variable" -ForegroundColor Red
    exit 1
}

$Headers = @{
    "Authorization" = "Bearer $ApiKey"
    "Content-Type" = "application/json"
}

Write-Host "🧹 Quick AnythingLLM Chat Cleaner" -ForegroundColor Cyan
Write-Host "Targeting workspace: $WorkspaceName" -ForegroundColor Yellow
Write-Host ""

try {
    # Get workspaces
    Write-Host "📋 Getting workspaces..." -ForegroundColor Blue
    $response = Invoke-RestMethod -Uri "$AnythingLLMUrl/api/v1/workspaces" -Headers $Headers -Method GET
    $workspaces = $response.workspaces
    
    # Find target workspace
    $targetWorkspace = $workspaces | Where-Object { $_.name -like "*$WorkspaceName*" }
    
    if (-not $targetWorkspace) {
        Write-Host "Available workspaces:" -ForegroundColor Yellow
        $workspaces | ForEach-Object { Write-Host "  • $($_.name)" -ForegroundColor Gray }
        Write-Host "❌ Workspace matching '$WorkspaceName' not found" -ForegroundColor Red
        exit 1
    }
    
    $slug = $targetWorkspace.slug
    Write-Host "✅ Found workspace: $($targetWorkspace.name) (slug: $slug)" -ForegroundColor Green
    
    # Method 1: Reset chat history via workspace update
    Write-Host "🔄 Resetting chat history..." -ForegroundColor Blue
    $resetPayload = @{
        openAiHistory = 0  # Clear message history
        chatMode = "chat"
    } | ConvertTo-Json
    
    $updateResponse = Invoke-RestMethod -Uri "$AnythingLLMUrl/api/v1/workspace/$slug/update" -Headers $Headers -Method POST -Body $resetPayload
    Write-Host "✅ Chat history reset via workspace update" -ForegroundColor Green
    
    # Method 2: Try to clear any existing threads
    try {
        Write-Host "🧹 Checking for chat threads..." -ForegroundColor Blue
        $threadsResponse = Invoke-RestMethod -Uri "$AnythingLLMUrl/api/v1/workspace/$slug/threads" -Headers $Headers -Method GET -ErrorAction SilentlyContinue
        
        if ($threadsResponse.threads -and $threadsResponse.threads.Count -gt 0) {
            Write-Host "Found $($threadsResponse.threads.Count) thread(s) to clear" -ForegroundColor Gray
            foreach ($thread in $threadsResponse.threads) {
                try {
                    Invoke-RestMethod -Uri "$AnythingLLMUrl/api/v1/workspace/$slug/thread/$($thread.slug)" -Headers $Headers -Method DELETE
                    Write-Host "  ✅ Deleted thread: $($thread.slug)" -ForegroundColor Green
                }
                catch {
                    Write-Host "  ⚠️  Could not delete thread: $($thread.slug)" -ForegroundColor Yellow
                }
            }
        }
        else {
            Write-Host "  ℹ️  No active threads found" -ForegroundColor Gray
        }
    }
    catch {
        Write-Host "  ℹ️  Threads endpoint not accessible (normal for some AnythingLLM versions)" -ForegroundColor Gray
    }
    
    Write-Host ""
    Write-Host "✅ Chat clearing completed for workspace: $($targetWorkspace.name)" -ForegroundColor Green
    Write-Host "💡 The workspace is now ready for fresh conversations" -ForegroundColor Cyan
}
catch {
    Write-Host "❌ Error: $($_.Exception.Message)" -ForegroundColor Red
    
    # Show response details if available
    if ($_.Exception.Response) {
        $statusCode = $_.Exception.Response.StatusCode
        Write-Host "Status Code: $statusCode" -ForegroundColor Red
    }
    
    exit 1
}