# Test RAG chat with enhanced error logging
param(
    [string]$ApiKey = $env:ANYTHINGLM_API_KEY,
    [string]$BaseUrl = "http://localhost:3001",
    [string]$Workspace = "maverick-nicole"
)

# Colors for output
$ErrorColor = "Red"
$SuccessColor = "Green" 
$InfoColor = "Cyan"
$WarningColor = "Yellow"

Write-Host "Testing AnythingLLM Chat with Enhanced Error Logging" -ForegroundColor $InfoColor
Write-Host "=================================================" -ForegroundColor $InfoColor

if (-not $ApiKey) {
    Write-Host "❌ No API key found. Set ANYTHINGLM_API_KEY environment variable." -ForegroundColor $ErrorColor
    exit 1
}

Write-Host "✓ API Key: $($ApiKey.Substring(0,8))..." -ForegroundColor $SuccessColor
Write-Host "✓ Base URL: $BaseUrl" -ForegroundColor $SuccessColor
Write-Host "✓ Workspace: $Workspace" -ForegroundColor $SuccessColor

$headers = @{
    "Authorization" = "Bearer $ApiKey"
    "Content-Type" = "application/json"
}

# Test message
$testMessage = "What are the main requirements categories?"

$body = @{
    message = $testMessage
    mode = "chat"
} | ConvertTo-Json

Write-Host "`n🧪 Testing chat endpoint..." -ForegroundColor $InfoColor
Write-Host "Message: $testMessage" -ForegroundColor $InfoColor

try {
    $response = Invoke-RestMethod -Uri "$BaseUrl/api/workspace/$Workspace/chat" -Method Post -Headers $headers -Body $body -ErrorAction Stop
    
    Write-Host "✅ Chat request successful!" -ForegroundColor $SuccessColor
    Write-Host "Response type: $($response.type)" -ForegroundColor $SuccessColor
    $sourcesCount = if ($response.sources) { $response.sources.Count } else { 0 }
    $textLength = if ($response.textResponse) { $response.textResponse.Length } else { 0 }
    
    Write-Host "Sources found: $sourcesCount" -ForegroundColor $SuccessColor
    Write-Host "Text response length: $textLength" -ForegroundColor $SuccessColor
    
    if ($response.sources) {
        Write-Host "`n📚 Sources:" -ForegroundColor $InfoColor
        $response.sources | ForEach-Object { Write-Host "  - $($_.title)" -ForegroundColor $InfoColor }
    } else {
        Write-Host "⚠️ No RAG sources found in response" -ForegroundColor $WarningColor
    }
    
    if ($response.textResponse) {
        Write-Host "`n💬 LLM Response:" -ForegroundColor $InfoColor
        Write-Host $response.textResponse -ForegroundColor White
    }
    
} catch {
    Write-Host "❌ Chat request failed!" -ForegroundColor $ErrorColor
    Write-Host "Status: $($_.Exception.Response.StatusCode)" -ForegroundColor $ErrorColor
    Write-Host "Error: $($_.Exception.Message)" -ForegroundColor $ErrorColor
    
    if ($_.Exception.Response) {
        try {
            $errorDetails = $_.Exception.Response.GetResponseStream()
            $reader = New-Object System.IO.StreamReader($errorDetails)
            $errorContent = $reader.ReadToEnd()
            Write-Host "Details: $errorContent" -ForegroundColor $ErrorColor
        } catch {
            Write-Host "Could not read error details" -ForegroundColor $ErrorColor
        }
    }
}

Write-Host "`n🔍 Now run the application and check logs for detailed AnythingLLMService output..." -ForegroundColor $InfoColor