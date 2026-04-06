param(
    [string]$ApiKey = $env:ANYTHINGLM_API_KEY,
    [string]$BaseUrl = "http://localhost:3001",
    [string]$Workspace = "maverick-nicole"
)

Write-Host "Testing AnythingLLM Chat with Enhanced Error Logging" -ForegroundColor Cyan

if (-not $ApiKey) {
    Write-Host "No API key found. Set ANYTHINGLM_API_KEY environment variable." -ForegroundColor Red
    exit 1
}

Write-Host "API Key: $($ApiKey.Substring(0,8))..." -ForegroundColor Green
Write-Host "Base URL: $BaseUrl" -ForegroundColor Green
Write-Host "Workspace: $Workspace" -ForegroundColor Green

$headers = @{
    "Authorization" = "Bearer $ApiKey"
    "Content-Type" = "application/json"
}

$testMessage = "What are the main requirements categories?"

$body = @{
    message = $testMessage
    mode = "chat"
} | ConvertTo-Json

Write-Host ""
Write-Host "Testing chat endpoint..." -ForegroundColor Cyan
Write-Host "Message: $testMessage" -ForegroundColor Cyan

try {
    $response = Invoke-RestMethod -Uri "$BaseUrl/api/workspace/$Workspace/chat" -Method Post -Headers $headers -Body $body -ErrorAction Stop
    
    Write-Host "Chat request successful!" -ForegroundColor Green
    Write-Host "Response type: $($response.type)" -ForegroundColor Green
    
    $sourcesCount = if ($response.sources) { $response.sources.Count } else { 0 }
    Write-Host "Sources found: $sourcesCount" -ForegroundColor Green
    
    $textLength = if ($response.textResponse) { $response.textResponse.Length } else { 0 }
    Write-Host "Text response length: $textLength" -ForegroundColor Green
    
    if ($response.sources -and $response.sources.Count -gt 0) {
        Write-Host ""
        Write-Host "Sources:" -ForegroundColor Cyan
        foreach ($source in $response.sources) {
            Write-Host "  - $($source.title)" -ForegroundColor Cyan
        }
    } else {
        Write-Host "No RAG sources found in response" -ForegroundColor Yellow
    }
    
    if ($response.textResponse) {
        Write-Host ""
        Write-Host "LLM Response:" -ForegroundColor Cyan
        Write-Host $response.textResponse -ForegroundColor White
    }
    
} catch {
    Write-Host "Chat request failed!" -ForegroundColor Red
    Write-Host "Status: $($_.Exception.Response.StatusCode)" -ForegroundColor Red
    Write-Host "Error: $($_.Exception.Message)" -ForegroundColor Red
    
    if ($_.Exception.Response) {
        try {
            $errorDetails = $_.Exception.Response.GetResponseStream()
            $reader = New-Object System.IO.StreamReader($errorDetails)
            $errorContent = $reader.ReadToEnd()
            Write-Host "Details: $errorContent" -ForegroundColor Red
        } catch {
            Write-Host "Could not read error details" -ForegroundColor Red
        }
    }
}

Write-Host ""
Write-Host "Now run the application and check logs for detailed AnythingLLMService output..." -ForegroundColor Cyan