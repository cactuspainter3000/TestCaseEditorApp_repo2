Write-Host "Testing AnythingLLM API access..." -ForegroundColor Cyan

$baseUrl = "http://localhost:3001"

# Test without API key first
try {
    Write-Host "1. Testing without API key..." -ForegroundColor Yellow
    $response = Invoke-RestMethod -Uri "$baseUrl/api/v1/workspaces" -Method GET -TimeoutSec 10
    Write-Host "SUCCESS: No API key needed" -ForegroundColor Green
} catch {
    Write-Host "FAILED: $($_.Exception.Message)" -ForegroundColor Red
    
    # Try with API key
    Write-Host "2. Testing with API key..." -ForegroundColor Yellow
    $apiKey = "JVJGN9Q-HG4M4Q4-GW3CVWS-AC8XG1G"
    $headers = @{
        'Authorization' = "Bearer $apiKey"
    }
    
    try {
        $response = Invoke-RestMethod -Uri "$baseUrl/api/v1/workspaces" -Method GET -Headers $headers -TimeoutSec 10
        Write-Host "SUCCESS: API key works" -ForegroundColor Green
        
        # Now test the specific workspace
        Write-Host "3. Testing workspace access..." -ForegroundColor Yellow
        $workspaceResponse = Invoke-RestMethod -Uri "$baseUrl/api/v1/workspace/maverick-nicole" -Method GET -Headers $headers -TimeoutSec 10
        
        if ($workspaceResponse.workspace) {
            $workspace = $workspaceResponse.workspace
            Write-Host "WORKSPACE FOUND:" -ForegroundColor Green
            Write-Host "  Name: $($workspace.name)" -ForegroundColor White
            Write-Host "  Vectors: $($workspace.totalVectors)" -ForegroundColor White
            Write-Host "  Documents: $($workspace.documents.Count)" -ForegroundColor White
            Write-Host "  LLM Provider: $($workspace.chatProvider)" -ForegroundColor White
            Write-Host "  LLM Model: $($workspace.chatModel)" -ForegroundColor White
            
            if ($workspace.totalVectors -eq 0) {
                Write-Host "PROBLEM: No vectors - documents not processed!" -ForegroundColor Red
            }
            if ($workspace.documents.Count -eq 0) {
                Write-Host "PROBLEM: No documents uploaded!" -ForegroundColor Red
            }
        }
        
    } catch {
        Write-Host "FAILED: $($_.Exception.Message)" -ForegroundColor Red
    }
}