Write-Host "=== Basic Jama OAuth Scope Error Demo ===" -ForegroundColor Cyan
Write-Host ""
Write-Host "Step 1: OAuth Authentication" -ForegroundColor Yellow
Write-Host "  Request: POST /rest/oauth/token" -ForegroundColor Gray
Write-Host "  Headers: Authorization Basic [client_id:client_secret]" -ForegroundColor Gray
Write-Host "  Body: grant_type=client_credentials" -ForegroundColor Gray
Write-Host "  Result: SUCCESS - Got access token" -ForegroundColor Green
Write-Host ""
Write-Host "Step 2: API Call with Token" -ForegroundColor Yellow
Write-Host "  Request: GET /rest/v1/projects" -ForegroundColor Gray
Write-Host "  Headers: Authorization Bearer [access_token]" -ForegroundColor Gray
Write-Host "  Result: FAILED" -ForegroundColor Red
Write-Host ""
Write-Host "ERROR RESPONSE:" -ForegroundColor Red -BackgroundColor Black
Write-Host "  Status: 500 Internal Server Error" -ForegroundColor White
Write-Host "  Body: IndexOutOfBounds exception" -ForegroundColor White
Write-Host ""
Write-Host "PROBLEM:" -ForegroundColor Yellow
Write-Host "  OAuth client scope = 'Token Information'" -ForegroundColor Red
Write-Host ""  
Write-Host "SOLUTION:" -ForegroundColor Green
Write-Host "  Change OAuth client scope to 'read'" -ForegroundColor White
Write-Host "  Location: Jama Admin > OAuth Clients" -ForegroundColor White
Write-Host ""
Write-Host "Press any key to continue..."
Read-Host