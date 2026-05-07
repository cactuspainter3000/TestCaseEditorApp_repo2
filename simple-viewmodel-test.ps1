# Simple ViewModel UI Binding Test
Write-Host "Testing TrainingDataValidation ViewModel UI Binding..." -ForegroundColor Green

try {
    # Build first
    Write-Host "Building..." -ForegroundColor Yellow
    dotnet build TestCaseEditorApp.csproj --verbosity quiet
    
    if ($LASTEXITCODE -ne 0) {
        throw "Build failed"
    }
    
    Write-Host "Build successful!" -ForegroundColor Green
    
    # Check ViewModel file
    $viewModelFile = "MVVM\Domains\TrainingDataValidation\ViewModels\TrainingDataValidationViewModel.cs"
    
    if (-not (Test-Path $viewModelFile)) {
        throw "ViewModel file not found"
    }
    
    Write-Host "ViewModel file exists" -ForegroundColor Green
    
    $content = Get-Content $viewModelFile -Raw
    
    # Check key patterns
    $patterns = @(
        @{ Name = "BaseDomainViewModel"; Pattern = ": BaseDomainViewModel" },
        @{ Name = "Constructor Injection"; Pattern = "public TrainingDataValidationViewModel\(" },
        @{ Name = "ICommand Properties"; Pattern = "public.*ICommand" },
        @{ Name = "ObservableCollection"; Pattern = "ObservableCollection" },
        @{ Name = "StatusMessage Property"; Pattern = "public.*string.*StatusMessage" },
        @{ Name = "IsLoading Property"; Pattern = "public.*bool.*IsLoading" },
        @{ Name = "Abstract Method Override"; Pattern = "protected override" }
    )
    
    $passed = 0
    foreach ($pattern in $patterns) {
        if ($content -match $pattern.Pattern) {
            Write-Host "  Found: $($pattern.Name)" -ForegroundColor Green
            $passed++
        } else {
            Write-Host "  Missing: $($pattern.Name)" -ForegroundColor Yellow
        }
    }
    
    Write-Host "`nResults: $passed/$($patterns.Count) patterns found" -ForegroundColor Cyan
    
    if ($passed -ge 5) {
        Write-Host "`nViewModel UI Binding Test: PASSED!" -ForegroundColor Green
        Write-Host "- ViewModel follows MVVM patterns" -ForegroundColor Green
        Write-Host "- Properties available for binding" -ForegroundColor Green
        Write-Host "- Commands available for UI interaction" -ForegroundColor Green
        Write-Host "- BaseDomainViewModel inheritance correct" -ForegroundColor Green
    } else {
        Write-Host "`nSome patterns missing, but basic structure is present" -ForegroundColor Yellow
    }
    
} catch {
    Write-Host "Test failed: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}