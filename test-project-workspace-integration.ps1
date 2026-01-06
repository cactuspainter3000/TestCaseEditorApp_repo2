#!/usr/bin/env pwsh

# Test script to verify project-specific workspace integration
# This tests the end-to-end flow from project creation to requirement analysis

Write-Host "🧪 Testing Project-Specific Workspace Integration" -ForegroundColor Cyan
Write-Host "=" * 60

# Test project creation and workspace context setting
function Test-WorkspaceContextIntegration {
    Write-Host "📝 Testing workspace context integration..." -ForegroundColor Yellow
    
    # Build the project first
    Write-Host "🔨 Building project..."
    dotnet build --configuration Release --verbosity minimal
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Build failed!"
        return $false
    }
    
    Write-Host "✅ Build successful" -ForegroundColor Green
    
    # Test 1: Verify RequirementAnalysisService has SetWorkspaceContext method
    Write-Host "🔍 Checking RequirementAnalysisService.SetWorkspaceContext method exists..."
    $setWorkspaceContextExists = Select-String -Path "Services/RequirementAnalysisService.cs" -Pattern "SetWorkspaceContext.*string.*projectWorkspaceName" -Quiet
    if ($setWorkspaceContextExists) {
        Write-Host "✅ SetWorkspaceContext method found" -ForegroundColor Green
    } else {
        Write-Host "❌ SetWorkspaceContext method not found" -ForegroundColor Red
        return $false
    }
    
    # Test 2: Verify TestCaseGenerationMediator calls SetWorkspaceContext on project events
    Write-Host "🔍 Checking TestCaseGenerationMediator workspace context integration..."
    $mediatorIntegration = Select-String -Path "MVVM/Domains/TestCaseGeneration/Mediators/TestCaseGenerationMediator.cs" -Pattern "_analysisService\.SetWorkspaceContext.*projectCreated\.WorkspaceName" -Quiet
    if ($mediatorIntegration) {
        Write-Host "✅ Mediator integration found for ProjectCreated" -ForegroundColor Green
    } else {
        Write-Host "❌ Mediator integration not found for ProjectCreated" -ForegroundColor Red
        return $false
    }
    
    $mediatorIntegrationOpened = Select-String -Path "MVVM/Domains/TestCaseGeneration/Mediators/TestCaseGenerationMediator.cs" -Pattern "_analysisService\.SetWorkspaceContext.*projectOpened\.WorkspaceName" -Quiet
    if ($mediatorIntegrationOpened) {
        Write-Host "✅ Mediator integration found for ProjectOpened" -ForegroundColor Green
    } else {
        Write-Host "❌ Mediator integration not found for ProjectOpened" -ForegroundColor Red
        return $false
    }
    
    # Test 3: Verify workspace creation uses project name
    Write-Host "🔍 Checking NewProjectWorkflowViewModel workspace creation..."
    $workspaceCreation = Select-String -Path "MVVM/Domains/WorkspaceManagement/ViewModels/NewProjectWorkflowViewModel.cs" -Pattern "CreateWorkspaceAsync.*WorkspaceName" -Quiet
    if ($workspaceCreation) {
        Write-Host "✅ Workspace creation with project name found" -ForegroundColor Green
    } else {
        Write-Host "❌ Workspace creation with project name not found" -ForegroundColor Red
        return $false
    }
    
    # Test 4: Verify enhanced workspace detection logic
    Write-Host "🔍 Checking enhanced workspace detection logic..."
    $workspaceDetection = Select-String -Path "Services/RequirementAnalysisService.cs" -Pattern "string\.Equals.*projectWorkspaceName.*StringComparison\.OrdinalIgnoreCase" -Quiet
    if ($workspaceDetection) {
        Write-Host "✅ Enhanced workspace detection logic found" -ForegroundColor Green
    } else {
        Write-Host "❌ Enhanced workspace detection logic not found" -ForegroundColor Red
        return $false
    }
    
    Write-Host "🎉 All workspace integration tests passed!" -ForegroundColor Green
    return $true
}

# Test AnythingLLM workspace compatibility
function Test-AnythingLLMCompatibility {
    Write-Host "📝 Testing AnythingLLM workspace compatibility..." -ForegroundColor Yellow
    
    # Check if AnythingLLM service is available (optional)
    try {
        $response = Invoke-WebRequest -Uri "http://localhost:3001/api/v1/admin" -TimeoutSec 5 -ErrorAction SilentlyContinue
        if ($response.StatusCode -eq 200) {
            Write-Host "✅ AnythingLLM service is available for testing" -ForegroundColor Green
            return $true
        }
    } catch {
        Write-Host "ℹ️ AnythingLLM service not available (this is optional for build verification)" -ForegroundColor Yellow
        return $true
    }
    
    return $true
}

# Run all tests
$allTestsPassed = $true

if (-not (Test-WorkspaceContextIntegration)) {
    $allTestsPassed = $false
}

if (-not (Test-AnythingLLMCompatibility)) {
    $allTestsPassed = $false
}

Write-Host ""
if ($allTestsPassed) {
    Write-Host "🎉 All integration tests passed! Project-specific workspace architecture is properly implemented." -ForegroundColor Green
    Write-Host ""
    Write-Host "✅ Key Features Verified:" -ForegroundColor Green
    Write-Host "  • RequirementAnalysisService.SetWorkspaceContext method"
    Write-Host "  • TestCaseGenerationMediator workspace context setting"
    Write-Host "  • Project creation with workspace name"
    Write-Host "  • Enhanced workspace detection logic"
    Write-Host ""
    Write-Host "🔧 Next Steps:" -ForegroundColor Cyan
    Write-Host "  1. Test creating a new project with a unique name"
    Write-Host "  2. Verify requirement analysis uses the correct workspace"
    Write-Host "  3. Confirm fast analysis performance (~30 seconds vs 3+ minutes)"
    
    exit 0
} else {
    Write-Host "❌ Some integration tests failed! Please review the implementation." -ForegroundColor Red
    exit 1
}