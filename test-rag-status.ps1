#!/usr/bin/env pwsh
# Test script to validate RAG status indicator functionality
# This script will help debug why the RAG indicator isn't showing up

Write-Host "🔍 Testing RAG Status Indicator Functionality" -ForegroundColor Cyan

# 1. Check if the application builds successfully
Write-Host "`n1. Building application..." -ForegroundColor Yellow
$buildResult = & dotnet build --verbosity quiet
if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ Build failed!" -ForegroundColor Red
    exit 1
}
Write-Host "✅ Build successful" -ForegroundColor Green

# 2. Check for key RAG components
Write-Host "`n2. Checking for key RAG components..." -ForegroundColor Yellow

$ragComponents = @(
    @{ File = "MVVM\Utils\RAGProgressMediator.cs"; Pattern = "NotifyProgressUpdated"; Name = "RAG Progress Mediator" },
    @{ File = "MVVM\ViewModels\WorkspaceHeaderViewModel.cs"; Pattern = "OnRAGProgressUpdated"; Name = "Workspace Header RAG Handler" },
    @{ File = "MVVM\Views\WorkspaceHeaderView.xaml"; Pattern = "IsRagReady"; Name = "RAG Status XAML Binding" },
    @{ File = "MVVM\ViewModels\MainViewModel.cs"; Pattern = "InitializeRagForWorkspaceAsync"; Name = "RAG Initialization Method" }
)

foreach ($component in $ragComponents) {
    $found = Select-String -Path $component.File -Pattern $component.Pattern -Quiet
    if ($found) {
        Write-Host "  ✅ $($component.Name)" -ForegroundColor Green
    } else {
        Write-Host "  ❌ $($component.Name) - Pattern '$($component.Pattern)' not found" -ForegroundColor Red
    }
}

# 3. Check if LoadWorkspaceFromPath has RAG initialization
Write-Host "`n3. Checking LoadWorkspaceFromPath for RAG initialization..." -ForegroundColor Yellow
$loadWorkspaceRAG = Select-String -Path "MVVM\ViewModels\MainViewModel.cs" -Pattern "Auto-initializing RAG for workspace"
if ($loadWorkspaceRAG) {
    Write-Host "  ✅ LoadWorkspaceFromPath includes RAG initialization" -ForegroundColor Green
} else {
    Write-Host "  ❌ LoadWorkspaceFromPath missing RAG initialization" -ForegroundColor Red
}

# 4. Check if manual RAG initialization commands are removed
Write-Host "`n4. Checking for removed manual RAG commands..." -ForegroundColor Yellow
$manualRAG = Select-String -Path "MVVM\ViewModels\MainViewModel.cs" -Pattern "InitializeRagCommand" -Quiet
if ($manualRAG) {
    Write-Host "  ⚠️  Manual RAG commands still present (should be removed)" -ForegroundColor Yellow
} else {
    Write-Host "  ✅ Manual RAG commands properly removed" -ForegroundColor Green
}

# 5. Check if workspace header properly subscribes to RAG updates
Write-Host "`n5. Checking workspace header RAG subscription..." -ForegroundColor Yellow
$ragSubscription = Select-String -Path "MVVM\ViewModels\WorkspaceHeaderViewModel.cs" -Pattern "RAGProgressMediator.ProgressUpdated \+=" -Quiet
if ($ragSubscription) {
    Write-Host "  ✅ WorkspaceHeaderViewModel subscribes to RAG updates" -ForegroundColor Green
} else {
    Write-Host "  ❌ WorkspaceHeaderViewModel missing RAG subscription" -ForegroundColor Red
}

Write-Host "`n🎯 Key Debugging Questions:" -ForegroundColor Cyan
Write-Host "   1. Does CurrentAnythingLLMWorkspaceSlug get set when opening a workspace?" -ForegroundColor White
Write-Host "   2. Is the WorkspaceHeaderViewModel created when switching to Requirements?" -ForegroundColor White
Write-Host "   3. Are RAGProgressMediator events being fired during initialization?" -ForegroundColor White

Write-Host "`n📋 Next Steps:" -ForegroundColor Cyan
Write-Host "   1. Run the application and open an existing workspace" -ForegroundColor White
Write-Host "   2. Check if you see the red/green RAG status dot in the workspace header" -ForegroundColor White
Write-Host "   3. If not visible, the issue might be:" -ForegroundColor White
Write-Host "      - CurrentAnythingLLMWorkspaceSlug not set for existing workspaces" -ForegroundColor Gray
Write-Host "      - RAG initialization not being triggered" -ForegroundColor Gray
Write-Host "      - WorkspaceHeaderViewModel not receiving mediator events" -ForegroundColor Gray

Write-Host "`n🔧 To test manually:" -ForegroundColor Cyan
Write-Host "   dotnet run" -ForegroundColor White
Write-Host "   Then: File -> Open -> [select a .tcex.json file]" -ForegroundColor White
Write-Host "   Look for: Red/green dot near Knowledge Base text in header" -ForegroundColor White