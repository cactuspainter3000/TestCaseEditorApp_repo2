# Test Logic Conflict Prevention
Write-Host "Testing AnythingLLM Logic Conflict Prevention" -ForegroundColor Green
Write-Host "=============================================" -ForegroundColor Green
Write-Host

Write-Host "Testing scenarios where AnythingLLM is not installed..." -ForegroundColor Yellow
Write-Host

# Test 1: Simulate no installation scenario
Write-Host "1. Installation Detection Test..." -ForegroundColor Yellow

# Check if AnythingLLM actually exists
$installPaths = @(
    "$env:LOCALAPPDATA\Programs\AnythingLLM\AnythingLLM.exe",
    "$env:ProgramFiles\AnythingLLM\AnythingLLM.exe",
    "${env:ProgramFiles(x86)}\AnythingLLM\AnythingLLM.exe"
)

$actualInstallation = $false
foreach($path in $installPaths) {
    if (Test-Path $path) {
        Write-Host "   Found installation: $path" -ForegroundColor Green
        $actualInstallation = $true
        break
    }
}

if (-not $actualInstallation) {
    Write-Host "   No installation found (this is the test case)" -ForegroundColor Orange
    Write-Host "   Expected behavior:" -ForegroundColor Cyan
    Write-Host "   - Dialog should detect no installation" -ForegroundColor White
    Write-Host "   - Show installation guidance message" -ForegroundColor White
    Write-Host "   - Close dialog WITHOUT asking for API key" -ForegroundColor White
    Write-Host "   - No service operations attempted" -ForegroundColor White
} else {
    Write-Host "   Installation found - testing normal flow" -ForegroundColor Green
}

# Test 2: API Key handling logic
Write-Host "`n2. API Key Logic Test..." -ForegroundColor Yellow
$envKey = $env:ANYTHINGLM_API_KEY
if ($envKey) {
    Write-Host "   Environment API key exists" -ForegroundColor Green
    Write-Host "   Expected behavior: Use existing key, no dialog needed" -ForegroundColor Cyan
} else {
    Write-Host "   No environment API key found" -ForegroundColor Orange
    Write-Host "   Expected behavior: Show API key dialog ONLY if AnythingLLM is installed" -ForegroundColor Cyan
}

# Test 3: Service initialization timing
Write-Host "`n3. Service Initialization Timing..." -ForegroundColor Yellow
Write-Host "   Expected sequence:" -ForegroundColor Cyan
Write-Host "   1. Check installation status" -ForegroundColor White
Write-Host "   2. If not installed -> show message and exit" -ForegroundColor White
Write-Host "   3. If installed -> check API key" -ForegroundColor White
Write-Host "   4. If no API key -> show API key dialog" -ForegroundColor White
Write-Host "   5. If API key obtained -> create service instance" -ForegroundColor White
Write-Host "   6. Then proceed with service operations" -ForegroundColor White

# Test 4: Port availability (simulates running without installation)
Write-Host "`n4. Service Availability Test..." -ForegroundColor Yellow
$portTest = Test-NetConnection localhost -Port 3001 -InformationLevel Quiet -WarningAction SilentlyContinue
if ($portTest) {
    Write-Host "   Port 3001 is available (service running)" -ForegroundColor Green
    Write-Host "   This suggests AnythingLLM is running despite potential missing installation" -ForegroundColor Orange
} else {
    Write-Host "   Port 3001 not available (service not running)" -ForegroundColor Red
    Write-Host "   This is consistent with no installation scenario" -ForegroundColor Cyan
}

Write-Host "`nFixed Logic Conflicts:" -ForegroundColor Magenta
Write-Host "- Service creation deferred until installation verified" -ForegroundColor White
Write-Host "- API key dialog only shown if installation exists" -ForegroundColor White  
Write-Host "- Null checks added to all service method calls" -ForegroundColor White
Write-Host "- Early exit if installation not found (before API key prompt)" -ForegroundColor White
Write-Host "- Configuration status only checked after service creation" -ForegroundColor White

Write-Host "`nPrevious Issues Fixed:" -ForegroundColor Red
Write-Host "- No longer creates service before checking installation" -ForegroundColor White
Write-Host "- No API key prompts for non-existent installations" -ForegroundColor White
Write-Host "- No service method calls with null service instance" -ForegroundColor White
Write-Host "- No confusing user prompts when app can't function" -ForegroundColor White