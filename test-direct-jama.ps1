# Direct Jama API Test - No Emojis
# Test the hanging issue directly

$env:JAMA_BASE_URL = "https://jama02.rockwellcollins.com/contour"
$env:JAMA_CLIENT_ID = "hycv5tyzpvyvhmi" 
$env:JAMA_CLIENT_SECRET = "Wy+qLYTczFkxwZIhJJ/I4Q=="

Write-Host "=== DIRECT JAMA API TEST ===" -ForegroundColor Cyan
Write-Host "Testing with correct credentials..." -ForegroundColor Yellow

# Test OAuth token request
$tokenUrl = "$env:JAMA_BASE_URL/rest/oauth/token"
$credentials = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes("$env:JAMA_CLIENT_ID`:$env:JAMA_CLIENT_SECRET"))

$headers = @{
    'Authorization' = "Basic $credentials"
    'Content-Type' = 'application/x-www-form-urlencoded'
}

$body = @{
    'grant_type' = 'client_credentials'
    'scope' = 'token_information'
}

Write-Host "Step 1: Getting OAuth token..." -ForegroundColor Yellow

try {
    $tokenResponse = Invoke-RestMethod -Uri $tokenUrl -Method Post -Headers $headers -Body $body
    Write-Host "SUCCESS: OAuth token obtained" -ForegroundColor Green
    
    # Test the specific API call that hangs
    $itemsUrl = "$env:JAMA_BASE_URL/rest/v1/items?project=636&maxResults=50"
    $apiHeaders = @{
        'Authorization' = "Bearer $($tokenResponse.access_token)"
    }
    
    Write-Host "Step 2: Testing items API (this is where hanging occurs)..." -ForegroundColor Yellow
    Write-Host "URL: $itemsUrl" -ForegroundColor Gray
    
    # Add timeout to the request
    $timeout = 30 # 30 seconds
    Write-Host "Using $timeout second timeout..." -ForegroundColor Gray
    
    $itemsResponse = Invoke-RestMethod -Uri $itemsUrl -Method Get -Headers $apiHeaders -TimeoutSec $timeout
    
    $itemCount = if ($itemsResponse.data) { $itemsResponse.data.Count } else { 0 }
    Write-Host "SUCCESS: Retrieved $itemCount items" -ForegroundColor Green
    
    # Show item types
    if ($itemsResponse.data) {
        $typeGroups = $itemsResponse.data | Group-Object itemType
        Write-Host "Item types found:" -ForegroundColor Cyan
        foreach ($group in $typeGroups) {
            Write-Host "  Type $($group.Name): $($group.Count) items" -ForegroundColor White
        }
    }
    
} catch {
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
    
    if ($_.Exception.GetType().Name -eq "WebException" -and $_.Exception.Status -eq "Timeout") {
        Write-Host "CONFIRMED: Request timed out after $timeout seconds" -ForegroundColor Yellow
        Write-Host "This confirms the hanging issue exists at the HTTP level" -ForegroundColor Yellow
    }
}

Write-Host "=== TEST COMPLETE ===" -ForegroundColor Cyan