Write-Host "RAG Workspace Diagnostic" -ForegroundColor Cyan

$workspaceSlug = "maverick-nicole"
$baseUrl = "http://localhost:3001"
$workspaceUri = "$baseUrl/api/v1/workspace/$workspaceSlug"

try {
    Write-Host "Checking workspace..." -ForegroundColor Yellow
    Write-Host "URL: $workspaceUri" -ForegroundColor Gray
    
    $response = Invoke-RestMethod -Uri $workspaceUri -Method GET -TimeoutSec 10
    
    if ($response.workspace) {
        $workspace = $response.workspace
        Write-Host "Workspace found: $($workspace.name)" -ForegroundColor Green
        Write-Host "Vectors: $($workspace.totalVectors)" -ForegroundColor White
        Write-Host "Documents: $($workspace.documents.Count)" -ForegroundColor White
        Write-Host "LLM Provider: $($workspace.chatProvider)" -ForegroundColor White
        Write-Host "LLM Model: $($workspace.chatModel)" -ForegroundColor White
        
        if ($workspace.totalVectors -eq 0) {
            Write-Host "ISSUE: No vectors in workspace!" -ForegroundColor Red
        } else {
            Write-Host "OK: Workspace has $($workspace.totalVectors) vectors" -ForegroundColor Green
        }
        
        if ($workspace.documents.Count -eq 0) {
            Write-Host "ISSUE: No documents in workspace!" -ForegroundColor Red
        } else {
            Write-Host "OK: Documents found:" -ForegroundColor Green
            $workspace.documents | ForEach-Object { Write-Host "  - $($_.name)" -ForegroundColor Gray }
        }
    }

} catch {
    Write-Host "Error: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "Is AnythingLLM running on localhost:3001?" -ForegroundColor Yellow
}