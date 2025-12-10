# Test Improved AnythingLLM Auto-Start Functionality
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
            Write-Host "   ✅ Found AnythingLLM installation: $($exeFiles[0].FullName)" -ForegroundColor Green
            $found = $true
            break
        }
    }
}

if (-not $found) {
    Write-Host "   ❌ AnythingLLM installation not found in common locations" -ForegroundColor Red
}

# Test 2: Shortcut Detection  
Write-Host "`n2. Testing Start Menu Shortcut Detection..." -ForegroundColor Yellow
$startMenuPaths = @(
    "$env:APPDATA\Microsoft\Windows\Start Menu\Programs",
    "$env:ALLUSERSPROFILE\Microsoft\Windows\Start Menu\Programs"
)

$shortcutFound = $false
foreach($startPath in $startMenuPaths) {
    if (Test-Path $startPath) {
        $shortcuts = Get-ChildItem $startPath -Recurse -Filter "AnythingLLM.lnk" -ErrorAction SilentlyContinue
        if ($shortcuts) {
            Write-Host "   ✅ Found AnythingLLM shortcut: $($shortcuts[0].FullName)" -ForegroundColor Green
            $shortcutFound = $true
            break
        }
    }
}

if (-not $shortcutFound) {
    Write-Host "   ❌ AnythingLLM shortcut not found in Start Menu" -ForegroundColor Red
}

# Test 3: API Key Configuration
Write-Host "`n3. Testing API Key Configuration..." -ForegroundColor Yellow
$envApiKey = $env:ANYTHINGLM_API_KEY
if ($envApiKey) {
    Write-Host "   ✅ Environment API key found (ANYTHINGLM_API_KEY)" -ForegroundColor Green
} else {
    Write-Host "   ❌ No environment API key found" -ForegroundColor Red
}

# Check registry (Windows only)
if ($env:OS -eq "Windows_NT") {
    try {
        $regKey = Get-ItemProperty "HKCU:\SOFTWARE\TestCaseEditorApp\AnythingLLM" -ErrorAction SilentlyContinue
        if ($regKey -and $regKey.ApiKey) {
            Write-Host "   ✅ Registry API key found" -ForegroundColor Green
        } else {
            Write-Host "   ❌ No registry API key found" -ForegroundColor Red
        }
    } catch {
        Write-Host "   ❌ Could not check registry API key" -ForegroundColor Red
    }
}

# Test 4: Port Test
Write-Host "`n4. Testing AnythingLLM Service..." -ForegroundColor Yellow
$portTest = Test-NetConnection localhost -Port 3001 -InformationLevel Quiet -WarningAction SilentlyContinue
if ($portTest) {
    Write-Host "   ✅ AnythingLLM service is running on port 3001" -ForegroundColor Green
    
    # Test API if we have a key
    $testApiKey = $envApiKey
    if (-not $testApiKey) {
        $testApiKey = "test-key-placeholder"
    }
    
    try {
        $response = Invoke-RestMethod -Uri "http://localhost:3001/api/v1/workspaces" -Headers @{"Authorization"="Bearer $testApiKey"} -Method GET -ErrorAction Stop
        Write-Host "   ✅ API is responding" -ForegroundColor Green
    } catch {
        if ($_.Exception.Response.StatusCode -eq 401) {
            Write-Host "   ⚠️  API responding but needs valid API key" -ForegroundColor Orange
        } else {
            Write-Host "   ❌ API connection failed: $($_.Exception.Message)" -ForegroundColor Red
        }
    }
} else {
    Write-Host "   ❌ AnythingLLM service not running on port 3001" -ForegroundColor Red
}

Write-Host "`nEnhanced Features Implemented:" -ForegroundColor Magenta
Write-Host "=============================" -ForegroundColor Magenta
Write-Host "🔍 Dynamic installation detection (no hardcoded paths)" -ForegroundColor White
Write-Host "📍 Multiple search methods: Program Files, Registry, Start Menu" -ForegroundColor White
Write-Host "🔑 Flexible API key configuration (Registry + Environment)" -ForegroundColor White
Write-Host "🔧 API key setup dialog for first-time users" -ForegroundColor White
Write-Host "⚡ Smart service detection and auto-start" -ForegroundColor White
Write-Host "📦 Installation guidance for missing AnythingLLM" -ForegroundColor White
Write-Host "🛡️ Cross-platform registry detection with fallbacks" -ForegroundColor White

Write-Host "`nNext Steps for Testing:" -ForegroundColor Cyan
Write-Host "1. Set API key via environment variable" -ForegroundColor White
Write-Host "2. Or let the app prompt for API key configuration" -ForegroundColor White
Write-Host "3. Launch TestCaseEditorApp and try workspace operations" -ForegroundColor White
Write-Host "4. App will auto-detect installation and manage startup" -ForegroundColor White