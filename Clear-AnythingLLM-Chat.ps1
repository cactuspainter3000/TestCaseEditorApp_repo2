#!/usr/bin/env pwsh

<#
.SYNOPSIS
Clears AnythingLLM chat content and workspace history

.DESCRIPTION
This script provides multiple options to clear AnythingLLM chat content:
1. Clear specific workspace chat history
2. Clear all workspace chat histories
3. Reset workspace to fresh state
4. Delete and recreate workspace

.PARAMETER WorkspaceName
Name of the workspace to clear (optional - will prompt if not provided)

.PARAMETER Mode
Clear mode: "chat" (clear chat only), "reset" (reset workspace), "recreate" (delete and recreate)

.PARAMETER ApiKey
AnythingLLM API key (optional - will use environment variable if not provided)

.EXAMPLE
.\Clear-AnythingLLM-Chat.ps1 -WorkspaceName "Test Case Editor" -Mode "chat"
.\Clear-AnythingLLM-Chat.ps1 -Mode "reset"
#>

param(
    [string]$WorkspaceName,
    [ValidateSet("chat", "reset", "recreate", "list")]
    [string]$Mode = "chat",
    [string]$ApiKey
)

# Configuration
$AnythingLLMUrl = "http://localhost:3001"
$ApiVersion = "v1"

# Get API Key
if (-not $ApiKey) {
    $ApiKey = $env:ANYTHINGLM_API_KEY
    if (-not $ApiKey) {
        Write-Host "❌ No API key provided. Set ANYTHINGLM_API_KEY environment variable or use -ApiKey parameter" -ForegroundColor Red
        exit 1
    }
}

# Setup headers
$Headers = @{
    "Authorization" = "Bearer $ApiKey"
    "Content-Type" = "application/json"
}

Write-Host "=== AnythingLLM Chat Cleaner ===" -ForegroundColor Cyan
Write-Host "Mode: $Mode" -ForegroundColor Yellow
Write-Host ""

# Function to get all workspaces
function Get-Workspaces {
    try {
        $response = Invoke-RestMethod -Uri "$AnythingLLMUrl/api/$ApiVersion/workspaces" -Headers $Headers -Method GET
        return $response.workspaces
    }
    catch {
        Write-Host "❌ Failed to get workspaces: $($_.Exception.Message)" -ForegroundColor Red
        return $null
    }
}

# Function to get workspace threads/chat history
function Get-WorkspaceThreads {
    param([string]$slug)
    try {
        # Try to get workspace details which should include threads
        $response = Invoke-RestMethod -Uri "$AnythingLLMUrl/api/$ApiVersion/workspace/$slug" -Headers $Headers -Method GET
        return $response.threads
    }
    catch {
        Write-Host "⚠️  Could not retrieve threads for workspace '$slug': $($_.Exception.Message)" -ForegroundColor Yellow
        return $null
    }
}

# Function to clear workspace chat (if API supports it)
function Clear-WorkspaceChat {
    param([string]$slug)
    
    Write-Host "🧹 Attempting to clear chat for workspace '$slug'..." -ForegroundColor Blue
    
    # Method 1: Try to delete individual threads if we can get them
    $threads = Get-WorkspaceThreads -slug $slug
    if ($threads -and $threads.Count -gt 0) {
        Write-Host "Found $($threads.Count) thread(s) to clear..." -ForegroundColor Gray
        
        foreach ($thread in $threads) {
            try {
                $response = Invoke-RestMethod -Uri "$AnythingLLMUrl/api/$ApiVersion/workspace/$slug/thread/$($thread.id)" -Headers $Headers -Method DELETE
                Write-Host "  ✅ Deleted thread: $($thread.id)" -ForegroundColor Green
            }
            catch {
                Write-Host "  ❌ Failed to delete thread $($thread.id): $($_.Exception.Message)" -ForegroundColor Red
            }
        }
    }
    else {
        Write-Host "  ℹ️  No threads found or unable to retrieve threads" -ForegroundColor Gray
    }
    
    # Method 2: Try workspace chat reset endpoint (if it exists)
    try {
        $response = Invoke-RestMethod -Uri "$AnythingLLMUrl/api/$ApiVersion/workspace/$slug/reset-chat" -Headers $Headers -Method POST
        Write-Host "  ✅ Successfully reset chat history" -ForegroundColor Green
    }
    catch {
        Write-Host "  ℹ️  Chat reset endpoint not available: $($_.Exception.Message)" -ForegroundColor Gray
    }
    
    # Method 3: Clear via chat mode reset
    try {
        $resetPayload = @{
            chatMode = "chat"
            openAiHistory = 0  # Clear history
        } | ConvertTo-Json
        
        $response = Invoke-RestMethod -Uri "$AnythingLLMUrl/api/$ApiVersion/workspace/$slug/update" -Headers $Headers -Method POST -Body $resetPayload
        Write-Host "  ✅ Reset chat history via workspace update" -ForegroundColor Green
    }
    catch {
        Write-Host "  ℹ️  Workspace update method failed: $($_.Exception.Message)" -ForegroundColor Gray
    }
}

# Function to reset workspace
function Reset-Workspace {
    param([string]$slug, [string]$name)
    
    Write-Host "🔄 Resetting workspace '$name' ($slug)..." -ForegroundColor Blue
    
    try {
        # Reset workspace settings to default
        $resetPayload = @{
            openAiTemp = 0.3
            openAiHistory = 20
            similarityThreshold = 0.25
            topN = 4
            chatMode = "chat"
        } | ConvertTo-Json
        
        $response = Invoke-RestMethod -Uri "$AnythingLLMUrl/api/$ApiVersion/workspace/$slug/update" -Headers $Headers -Method POST -Body $resetPayload
        Write-Host "  ✅ Workspace reset successfully" -ForegroundColor Green
        
        # Also clear chat
        Clear-WorkspaceChat -slug $slug
    }
    catch {
        Write-Host "  ❌ Failed to reset workspace: $($_.Exception.Message)" -ForegroundColor Red
    }
}

# Function to recreate workspace
function Recreate-Workspace {
    param([string]$slug, [string]$name)
    
    Write-Host "♻️  Recreating workspace '$name'..." -ForegroundColor Blue
    
    try {
        # Delete workspace
        Write-Host "  🗑️  Deleting existing workspace..." -ForegroundColor Gray
        $response = Invoke-RestMethod -Uri "$AnythingLLMUrl/api/$ApiVersion/workspace/$slug" -Headers $Headers -Method DELETE
        Write-Host "  ✅ Workspace deleted successfully" -ForegroundColor Green
        
        Start-Sleep -Seconds 2
        
        # Recreate workspace
        Write-Host "  🆕 Creating new workspace..." -ForegroundColor Gray
        $createPayload = @{
            name = $name
        } | ConvertTo-Json
        
        $response = Invoke-RestMethod -Uri "$AnythingLLMUrl/api/$ApiVersion/workspace/new" -Headers $Headers -Method POST -Body $createPayload
        Write-Host "  ✅ Workspace recreated successfully" -ForegroundColor Green
        Write-Host "  📋 New slug: $($response.workspace.slug)" -ForegroundColor Cyan
    }
    catch {
        Write-Host "  ❌ Failed to recreate workspace: $($_.Exception.Message)" -ForegroundColor Red
    }
}

# Main execution
try {
    # Test connection
    Write-Host "🔗 Testing AnythingLLM connection..." -ForegroundColor Blue
    $workspaces = Get-Workspaces
    if (-not $workspaces) {
        Write-Host "❌ Cannot connect to AnythingLLM API" -ForegroundColor Red
        exit 1
    }
    
    Write-Host "✅ Connected successfully. Found $($workspaces.Count) workspace(s)" -ForegroundColor Green
    Write-Host ""
    
    if ($Mode -eq "list") {
        Write-Host "📋 Available Workspaces:" -ForegroundColor Yellow
        foreach ($ws in $workspaces) {
            Write-Host "  • $($ws.name) (slug: $($ws.slug))" -ForegroundColor White
        }
        exit 0
    }
    
    # Select workspace if not specified
    if (-not $WorkspaceName) {
        Write-Host "Available Workspaces:" -ForegroundColor Yellow
        for ($i = 0; $i -lt $workspaces.Count; $i++) {
            Write-Host "  $($i + 1). $($workspaces[$i].name)" -ForegroundColor White
        }
        Write-Host ""
        
        do {
            $selection = Read-Host "Select workspace (1-$($workspaces.Count)) or 'all' for all workspaces"
            
            if ($selection -eq "all") {
                $selectedWorkspaces = $workspaces
                break
            }
            elseif ($selection -match '^\d+$' -and [int]$selection -ge 1 -and [int]$selection -le $workspaces.Count) {
                $selectedWorkspaces = @($workspaces[[int]$selection - 1])
                break
            }
            else {
                Write-Host "Invalid selection. Please try again." -ForegroundColor Red
            }
        } while ($true)
    }
    else {
        # Find workspace by name
        $selectedWorkspaces = $workspaces | Where-Object { $_.name -eq $WorkspaceName }
        if (-not $selectedWorkspaces) {
            Write-Host "❌ Workspace '$WorkspaceName' not found" -ForegroundColor Red
            exit 1
        }
    }
    
    # Process selected workspaces
    foreach ($workspace in $selectedWorkspaces) {
        Write-Host ""
        Write-Host "🎯 Processing workspace: $($workspace.name)" -ForegroundColor Magenta
        
        switch ($Mode) {
            "chat" { Clear-WorkspaceChat -slug $workspace.slug }
            "reset" { Reset-Workspace -slug $workspace.slug -name $workspace.name }
            "recreate" { Recreate-Workspace -slug $workspace.slug -name $workspace.name }
        }
    }
    
    Write-Host ""
    Write-Host "✅ Operation completed!" -ForegroundColor Green
}
catch {
    Write-Host "❌ Script error: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}