# Test Enhanced AnythingLLM Integration
Write-Host "Testing Enhanced AnythingLLM Integration" -ForegroundColor Green
Write-Host "=======================================" -ForegroundColor Green
Write-Host

# Test 1: Installation Detection
Write-Host "1. Testing AnythingLLM Installation Detection..." -ForegroundColor Yellow
$commonPaths = @(
    "$env:LOCALAPPDATA\Programs\AnythingLLM",
    "$env:ProgramFiles\AnythingLLM", 
    "${env:ProgramFiles(x86)}\AnythingLLM"
)

$found = $false
foreach($path in $commonPaths) {
    if (Test-Path $path) {
        $exeFiles = Get-ChildItem $path -Recurse -Filter "AnythingLLM.exe" -ErrorAction SilentlyContinue
        if ($exeFiles) {
            Write-Host "   Found AnythingLLM installation: $($exeFiles[0].FullName)" -ForegroundColor Green
            $found = $true
            break
        }
    }
}

if (-not $found) {
    Write-Host "   AnythingLLM installation not found in common locations" -ForegroundColor Red
}

# Test 2: Port Test
Write-Host "`n2. Testing AnythingLLM Service..." -ForegroundColor Yellow
$portTest = Test-NetConnection localhost -Port 3001 -InformationLevel Quiet -WarningAction SilentlyContinue
if ($portTest) {
    Write-Host "   AnythingLLM service is running on port 3001" -ForegroundColor Green
} else {
    Write-Host "   AnythingLLM service not running on port 3001" -ForegroundColor Red
}

# Test 3: API Key Environment Variable
Write-Host "`n3. Testing API Key Configuration..." -ForegroundColor Yellow
$envApiKey = $env:ANYTHINGLM_API_KEY
if ($envApiKey) {
    Write-Host "   Environment API key found" -ForegroundColor Green
} else {
    Write-Host "   No environment API key found" -ForegroundColor Red
}

Write-Host "`nEnhanced Features Implemented:" -ForegroundColor Magenta
Write-Host "- Dynamic installation detection (no hardcoded paths)" -ForegroundColor White
Write-Host "- Multiple search methods: Program Files, Registry, Start Menu" -ForegroundColor White
Write-Host "- Flexible API key configuration (Registry + Environment)" -ForegroundColor White
Write-Host "- API key setup dialog for first-time users" -ForegroundColor White
Write-Host "- Smart service detection and auto-start" -ForegroundColor White
Write-Host "- Installation guidance for missing AnythingLLM" -ForegroundColor White

Write-Host "`nTo set an API key:" -ForegroundColor Cyan
Write-Host "Environment Variable: Set ANYTHINGLM_API_KEY" -ForegroundColor White
Write-Host "Or use the GUI dialog when app starts" -ForegroundColor White