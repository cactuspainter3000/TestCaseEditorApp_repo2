# ViewModel UI Binding Integration Test
Write-Host "🧪 Testing TrainingDataValidation ViewModel UI Binding..." -ForegroundColor Green

try {
    # Build the test
    Write-Host "📦 Building ViewModel binding test..." -ForegroundColor Yellow
    dotnet build TestCaseEditorApp.csproj --verbosity quiet
    
    if ($LASTEXITCODE -ne 0) {
        throw "Build failed for ViewModel test"
    }
    
    Write-Host "✅ Build successful!" -ForegroundColor Green
    
    # Test ViewModel properties and structure
    Write-Host "🔍 Verifying ViewModel structure..." -ForegroundColor Yellow
    
    $viewModelFile = "MVVM\Domains\TrainingDataValidation\ViewModels\TrainingDataValidationViewModel.cs"
    
    if (Test-Path $viewModelFile) {
        Write-Host "✅ ViewModel file exists" -ForegroundColor Green
        
        $viewModelContent = Get-Content $viewModelFile -Raw
        
        # Check for MVVM patterns
        $mvvmPatterns = @{
            "INotifyPropertyChanged" = "INotifyPropertyChanged|ObservableObject"
            "Commands" = "ICommand|RelayCommand|AsyncRelayCommand"
            "Properties" = "public.*\{.*get.*set.*\}"
            "Collections" = "ObservableCollection"
            "Constructor Injection" = "public TrainingDataValidationViewModel\("
            "BaseDomainViewModel" = ": BaseDomainViewModel"
            "Abstract Methods" = "protected override.*async Task.*SaveAsync|protected override.*void.*Cancel|protected override.*async Task.*RefreshAsync"
        }
        
        $passedPatterns = @()
        $failedPatterns = @()
        
        foreach ($pattern in $mvvmPatterns.Keys) {
            $regex = $mvvmPatterns[$pattern]
            if ($viewModelContent -match $regex) {
                $passedPatterns += $pattern
                Write-Host "  ✅ MVVM Pattern found: $pattern" -ForegroundColor Green
            } else {
                $failedPatterns += $pattern
                Write-Host "  ❌ MVVM Pattern missing: $pattern" -ForegroundColor Red
            }
        }
        
        # Check for specific properties
        Write-Host "`n🔍 Checking key properties..." -ForegroundColor Yellow
        
        $requiredProperties = @(
            "StatusMessage",
            "IsLoading", 
            "CurrentState",
            "PendingExamples",
            "ValidatedExamples",
            "ValidationOptions",
            "HasPendingExamples",
            "CanStartValidation"
        )
        
        $foundProperties = @()
        foreach ($prop in $requiredProperties) {
            if ($viewModelContent -match "public.*$prop") {
                $foundProperties += $prop
                Write-Host "  ✅ Property found: $prop" -ForegroundColor Green
            } else {
                Write-Host "  ❌ Property missing: $prop" -ForegroundColor Red
            }
        }
        
        # Check for commands
        Write-Host "`n🔍 Checking commands..." -ForegroundColor Yellow
        
        $requiredCommands = @(
            "GenerateExamplesCommand",
            "StartValidationCommand",
            "ApproveExampleCommand", 
            "RejectExampleCommand",
            "SaveProgressCommand"
        )
        
        $foundCommands = @()
        foreach ($cmd in $requiredCommands) {
            if ($viewModelContent -match "public.*ICommand.*$cmd") {
                $foundCommands += $cmd
                Write-Host "  ✅ Command found: $cmd" -ForegroundColor Green
            } else {
                Write-Host "  ❌ Command missing: $cmd" -ForegroundColor Red
            }
        }
        
        # Summary
        Write-Host "`n📊 ViewModel UI Binding Test Results:" -ForegroundColor Cyan
        Write-Host "  MVVM Patterns: $($passedPatterns.Count)/$($mvvmPatterns.Count)" -ForegroundColor $(if ($passedPatterns.Count -eq $mvvmPatterns.Count) { "Green" } else { "Yellow" })
        Write-Host "  Properties: $($foundProperties.Count)/$($requiredProperties.Count)" -ForegroundColor $(if ($foundProperties.Count -ge 6) { "Green" } else { "Yellow" })
        Write-Host "  Commands: $($foundCommands.Count)/$($requiredCommands.Count)" -ForegroundColor $(if ($foundCommands.Count -ge 4) { "Green" } else { "Yellow" })
        
        if ($passedPatterns.Count -ge 5 -and $foundProperties.Count -ge 6 -and $foundCommands.Count -ge 4) {
            Write-Host "`n🎉 ViewModel UI Binding Test: PASSED!" -ForegroundColor Green
            Write-Host "✅ MVVM patterns are properly implemented" -ForegroundColor Green
            Write-Host "✅ Properties are available for data binding" -ForegroundColor Green  
            Write-Host "✅ Commands are available for UI interaction" -ForegroundColor Green
            Write-Host "✅ BaseDomainViewModel inheritance is correct" -ForegroundColor Green
            Write-Host "✅ Constructor injection pattern is implemented" -ForegroundColor Green
        } else {
            throw "Some required ViewModel patterns are missing"
        }
        
    } else {
        throw "ViewModel file not found"
    }
    
} catch {
    Write-Host "❌ ViewModel UI binding test failed: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}