#!/usr/bin/env pwsh
# Test Requirements navigation via event debugging

Write-Host "Testing Requirements navigation flow..." -ForegroundColor Green

# Check if ViewConfigurationEvents exists and is properly implemented
Write-Host "`nChecking ViewConfigurationEvents implementation..." -ForegroundColor Yellow
$viewConfigEventsFile = "MVVM\Utils\ViewConfigurationEvents.cs"
if (Test-Path $viewConfigEventsFile) {
    Write-Host "ViewConfigurationEvents file found" -ForegroundColor Green
    $content = Get-Content $viewConfigEventsFile -Raw
    if ($content -match "ApplyViewConfiguration") {
        Write-Host "ApplyViewConfiguration event found" -ForegroundColor Green
    } else {
        Write-Host "ApplyViewConfiguration event NOT found" -ForegroundColor Red
    }
} else {
    Write-Host "ViewConfigurationEvents file NOT found" -ForegroundColor Red
}

# Check ViewAreaCoordinator implementation
Write-Host "`nChecking ViewAreaCoordinator implementation..." -ForegroundColor Yellow
$coordinatorFile = "Services\ViewAreaCoordinator.cs"
if (Test-Path $coordinatorFile) {
    Write-Host "ViewAreaCoordinator file found" -ForegroundColor Green
    $content = Get-Content $coordinatorFile -Raw
    if ($content -match "OnSectionChangeRequested") {
        Write-Host "OnSectionChangeRequested method found" -ForegroundColor Green
        if ($content -match "ApplyViewConfiguration") {
            Write-Host "Publishes ViewConfiguration events" -ForegroundColor Green
        } else {
            Write-Host "Does NOT publish ViewConfiguration events" -ForegroundColor Red
        }
    } else {
        Write-Host "OnSectionChangeRequested method NOT found" -ForegroundColor Red
    }
} else {
    Write-Host "ViewAreaCoordinator file NOT found" -ForegroundColor Red
}

# Check ConfigurableAreaViewModels subscription
Write-Host "`nChecking ConfigurableAreaViewModels event subscription..." -ForegroundColor Yellow
$configurableFiles = @(
    "MVVM\ViewModels\ConfigurableContentAreaViewModel.cs",
    "MVVM\ViewModels\ConfigurableHeaderAreaViewModel.cs"
)

foreach ($file in $configurableFiles) {
    $fileName = Split-Path $file -Leaf
    if (Test-Path $file) {
        Write-Host "$fileName found" -ForegroundColor Green
        $content = Get-Content $file -Raw
        if ($content -match "Subscribe.*ApplyViewConfiguration") {
            Write-Host "$fileName subscribes to ApplyViewConfiguration" -ForegroundColor Green
        } else {
            Write-Host "$fileName does NOT subscribe to ApplyViewConfiguration" -ForegroundColor Red
        }
    } else {
        Write-Host "$fileName NOT found" -ForegroundColor Red
    }
}

# Check if SideMenuViewModel properly calls NavigateToSection
Write-Host "`nChecking SideMenuViewModel navigation..." -ForegroundColor Yellow
$sideMenuFile = "MVVM\ViewModels\SideMenuViewModel.cs"
if (Test-Path $sideMenuFile) {
    Write-Host "SideMenuViewModel file found" -ForegroundColor Green
    $content = Get-Content $sideMenuFile -Raw
    if ($content -match "NavigateToSection") {
        Write-Host "NavigateToSection method found" -ForegroundColor Green
        if ($content -match "Requirements") {
            Write-Host "Requirements navigation implemented" -ForegroundColor Green
        } else {
            Write-Host "Requirements navigation string not found" -ForegroundColor Yellow
        }
    } else {
        Write-Host "NavigateToSection method NOT found" -ForegroundColor Red
    }
} else {
    Write-Host "SideMenuViewModel file NOT found" -ForegroundColor Red
}

Write-Host "`n=== Navigation Flow Summary ===" -ForegroundColor Cyan
Write-Host "Expected Flow:" -ForegroundColor White
Write-Host "1. SideMenu button click"
Write-Host "2. NavigationMediator.NavigateToSection call"  
Write-Host "3. ViewAreaCoordinator.OnSectionChangeRequested handler"
Write-Host "4. ViewConfigurationService.ApplyConfiguration call"
Write-Host "5. NavigationMediator.Publish ViewConfiguration event"
Write-Host "6. ConfigurableAreaViewModels receive events and update content"

Write-Host "`nTo debug further, run the app and check logs for:" -ForegroundColor Yellow
Write-Host "- ViewAreaCoordinator Section change requested: Requirements"
Write-Host "- ViewAreaCoordinator Configuration applied and published"
Write-Host "- ConfigurableContentAreaViewModel OnViewConfigurationRequested"
Write-Host "- ConfigurableHeaderAreaViewModel Configuration change"

Write-Host "`nTest complete!" -ForegroundColor Green