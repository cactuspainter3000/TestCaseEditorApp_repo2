# Test Auto-Start AnythingLLM Functionality
Write-Host "Testing AnythingLLM Auto-Start Functionality" -ForegroundColor Green
Write-Host "=============================================" -ForegroundColor Green
Write-Host

# Test 1: Check if AnythingLLM is currently running
Write-Host "1. Testing port connectivity..." -ForegroundColor Yellow
$portTest = Test-NetConnection localhost -Port 3001 -InformationLevel Quiet
if ($portTest) {
    Write-Host "   ✅ AnythingLLM is running on port 3001" -ForegroundColor Green
} else {
    Write-Host "   ❌ AnythingLLM is not running on port 3001" -ForegroundColor Red
}

# Test 2: Check API connectivity  
Write-Host "`n2. Testing API connectivity..." -ForegroundColor Yellow
try {
    $apiKey = "222C5V1-KK3MFY3-G1FFH7D-HN69H6G"
    $response = Invoke-RestMethod -Uri "http://localhost:3001/api/v1/workspaces" -Headers @{"Authorization"="Bearer $apiKey"} -Method GET -ErrorAction Stop
    Write-Host "   ✅ API is responding correctly" -ForegroundColor Green
    Write-Host "   📊 Found $($response.workspaces.Count) workspace(s)" -ForegroundColor Cyan
    foreach($ws in $response.workspaces) {
        Write-Host "      • $($ws.name) (slug: $($ws.slug))" -ForegroundColor White
    }
} catch {
    Write-Host "   ❌ API connection failed: $($_.Exception.Message)" -ForegroundColor Red
}

# Test 3: Check shortcut file existence
Write-Host "`n3. Checking AnythingLLM shortcut..." -ForegroundColor Yellow
$shortcutPath = "C:\Users\e10653214\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\AnythingLLM.lnk"
if (Test-Path $shortcutPath) {
    Write-Host "   ✅ AnythingLLM shortcut found" -ForegroundColor Green
} else {
    Write-Host "   ⚠️  AnythingLLM shortcut not found" -ForegroundColor Orange
}

# Test 4: Check for AnythingLLM process
Write-Host "`n4. Checking for AnythingLLM process..." -ForegroundColor Yellow
$processes = Get-Process -Name "AnythingLLM" -ErrorAction SilentlyContinue
if ($processes) {
    Write-Host "   ✅ Found AnythingLLM process(es)" -ForegroundColor Green
} else {
    Write-Host "   ❌ No AnythingLLM processes found" -ForegroundColor Red
}

Write-Host "`nAuto-Start Features Implemented:" -ForegroundColor Magenta
Write-Host "✨ Automatic service detection" -ForegroundColor White
Write-Host "🚀 Auto-launch using Windows shortcut" -ForegroundColor White  
Write-Host "🔄 Real-time status updates" -ForegroundColor White
Write-Host "🪟 Window minimization after startup" -ForegroundColor White
Write-Host "⚡ Graceful fallback if startup fails" -ForegroundColor White