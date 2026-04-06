#!/usr/bin/env pwsh
# Cleanup script for old code after architecture modernization

Write-Host "🧹 Cleaning up old code patterns..." -ForegroundColor Cyan

# Safe immediate deletions (backup files, temp files)
$safeDeletes = @(
    "MainViewModel.cs.backup",
    "MainViewModel.cs.backup2", 
    "TestCaseEditorApp.csproj.user.Backup.tmp",
    "TestCaseEditorApp_2elth1rl_wpftmp.csproj",
    "TestCaseEditorApp_3dol5law_wpftmp.csproj", 
    "TestCaseEditorApp_3ma5kdj0_wpftmp.csproj",
    "TestCaseEditorApp_4w45st2l_wpftmp.csproj"
)

$deletedCount = 0

foreach ($file in $safeDeletes) {
    if (Test-Path $file) {
        Write-Host "🗑️  Deleting: $file" -ForegroundColor Yellow
        Remove-Item $file -Force
        $deletedCount++
    }
}

Write-Host "`n✅ Deleted $deletedCount temporary/backup files" -ForegroundColor Green

# Check for duplicate ViewModels (require manual review)
Write-Host "`n🔍 Checking for legacy ViewModels that may be duplicates..." -ForegroundColor Cyan

$legacyViewModels = @(
    "MVVM\ViewModels\TestCaseGeneratorViewModel.cs"
)

foreach ($vm in $legacyViewModels) {
    if (Test-Path $vm) {
        $domainEquivalent = $vm -replace "MVVM\\ViewModels", "MVVM\\Domains\\TestCaseGeneration\\ViewModels"
        if (Test-Path $domainEquivalent) {
            Write-Host "⚠️  DUPLICATE FOUND:" -ForegroundColor Red
            Write-Host "   Legacy: $vm" -ForegroundColor White
            Write-Host "   Modern: $domainEquivalent" -ForegroundColor Green
            Write-Host "   👆 Consider deleting the legacy version after verification" -ForegroundColor Yellow
        }
    }
}

# Check for fragmented partial ViewModels
Write-Host "`n🔍 Checking for fragmented ViewModels (from consolidation plan)..." -ForegroundColor Cyan

$fragmentedVMs = @(
    @{
        Main = "MVVM\ViewModels\TestCaseCreatorHeaderViewModel.cs"
        Partials = @(
            "MVVM\ViewModels\TestCaseCreatorHeaderViewModel.Bindings.cs",
            "MVVM\ViewModels\TestCaseCreatorHeaderViewModel.Additions.cs", 
            "MVVM\ViewModels\TestCaseCreatorHeaderViewModel.LlmConnection.cs",
            "MVVM\ViewModels\TestCaseCreatorHeaderViewModel.Ollama.cs"
        )
    },
    @{
        Main = "MVVM\ViewModels\ClarifyingQuestionsViewModel.cs"
        Partials = @("MVVM\ViewModels\ClarifyingQuestionsViewModel.Commands.cs")
    },
    @{
        Main = "MVVM\Domains\TestCaseGeneration\ViewModels\RequirementsViewModel.cs"
        Partials = @("MVVM\ViewModels\RequirementsViewModel.Bindings.cs")
    }
)

foreach ($vm in $fragmentedVMs) {
    $mainExists = Test-Path $vm.Main
    $partialCount = ($vm.Partials | Where-Object { Test-Path $_ }).Count
    
    if ($mainExists -and $partialCount -gt 0) {
        Write-Host "🧩 FRAGMENTED: $($vm.Main.Split('\')[-1])" -ForegroundColor Yellow
        Write-Host "   Main file: $($vm.Main)" -ForegroundColor White
        foreach ($partial in $vm.Partials) {
            if (Test-Path $partial) {
                Write-Host "   Partial: $partial" -ForegroundColor Gray
            }
        }
        Write-Host "   👆 Consider consolidating (see VIEWMODEL_CONSOLIDATION_PLAN.md)" -ForegroundColor Yellow
    }
}

# Check for old static mediator patterns
Write-Host "`n🔍 Checking for legacy mediator patterns..." -ForegroundColor Cyan

if (Test-Path "MVVM\Utils\ProjectStatusMediator.cs") {
    Write-Host "⚠️  LEGACY PATTERN: ProjectStatusMediator.cs" -ForegroundColor Yellow
    Write-Host "   👆 Static mediator pattern - may be replaced by domain mediators" -ForegroundColor Gray
}

Write-Host "`n📋 Summary of cleanup recommendations:" -ForegroundColor Cyan
Write-Host "✅ Backup/temp files: Deleted automatically" -ForegroundColor Green  
Write-Host "🔴 Duplicate ViewModels: Require manual verification" -ForegroundColor Red
Write-Host "🟡 Fragmented ViewModels: Consolidation recommended" -ForegroundColor Yellow
Write-Host "🟠 Legacy mediators: Architecture review needed" -ForegroundColor DarkYellow

Write-Host "`nRefer to VIEWMODEL_CONSOLIDATION_PLAN.md for detailed consolidation steps" -ForegroundColor White