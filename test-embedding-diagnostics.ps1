# Test script to run AnythingLLM embedding diagnostics
# This helps isolate the embedding configuration issues

Write-Host "🔍 AnythingLLM Embedding Diagnostics" -ForegroundColor Cyan
Write-Host "=====================================" -ForegroundColor Cyan

# Test 1: Check if Ollama is running and responding
Write-Host "`n1. Testing Ollama Service..." -ForegroundColor Yellow
try {
    $ollamaResponse = Invoke-RestMethod -Uri "http://localhost:11434/api/tags" -TimeoutSec 5
    Write-Host "   ✅ Ollama is running" -ForegroundColor Green
    Write-Host "   📋 Available models:" -ForegroundColor Gray
    foreach ($model in $ollamaResponse.models) {
        $modelName = $model.name
        $size = [math]::Round($model.size / 1MB, 2)
        Write-Host "      - $modelName ($size MB)" -ForegroundColor Gray
        
        if ($modelName -like "*embed*") {
            Write-Host "        🎯 Embedding model found!" -ForegroundColor Green
        }
    }
}
catch {
    Write-Host "   ❌ Ollama not responding: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "   💡 Try: ollama serve" -ForegroundColor Yellow
}

# Test 2: Test Ollama embedding generation
Write-Host "`n2. Testing Ollama Embedding Generation..." -ForegroundColor Yellow
try {
    $embeddingPayload = @{
        model = "mxbai-embed-large:335m-v1-fp16"
        prompt = "This is a test requirement for embedding."
    } | ConvertTo-Json
    
    $embeddingResponse = Invoke-RestMethod -Uri "http://localhost:11434/api/embeddings" -Method Post -Body $embeddingPayload -ContentType "application/json" -TimeoutSec 30
    
    if ($embeddingResponse.embedding -and $embeddingResponse.embedding.Count -gt 0) {
        Write-Host "   ✅ Ollama embedding generation works!" -ForegroundColor Green
        Write-Host "   📊 Embedding dimensions: $($embeddingResponse.embedding.Count)" -ForegroundColor Gray
    } else {
        Write-Host "   ❌ Ollama embedding failed - no embedding returned" -ForegroundColor Red
    }
}
catch {
    Write-Host "   ❌ Ollama embedding failed: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "   💡 This is likely the root cause of AnythingLLM embedding issues!" -ForegroundColor Yellow
}

# Test 3: Check AnythingLLM connectivity  
Write-Host "`n3. Testing AnythingLLM API..." -ForegroundColor Yellow
try {
    # Test basic connectivity first
    $response = Invoke-WebRequest -Uri "http://localhost:3001" -TimeoutSec 5 -UseBasicParsing
    Write-Host "   ✅ AnythingLLM web interface accessible" -ForegroundColor Green
}
catch {
    Write-Host "   ❌ AnythingLLM not accessible: $($_.Exception.Message)" -ForegroundColor Red
}

# Test 4: Check system resources
Write-Host "`n4. System Resource Check..." -ForegroundColor Yellow
$memory = Get-WmiObject -Class Win32_OperatingSystem
$totalRAM = [math]::Round($memory.TotalVisibleMemorySize / 1MB, 2)
$freeRAM = [math]::Round($memory.FreePhysicalMemory / 1MB, 2)
$usedRAM = $totalRAM - $freeRAM

Write-Host "   💾 Total RAM: $totalRAM GB" -ForegroundColor Gray  
Write-Host "   💾 Used RAM: $usedRAM GB" -ForegroundColor Gray
Write-Host "   💾 Free RAM: $freeRAM GB" -ForegroundColor Gray

if ($freeRAM -gt 4) {
    Write-Host "   ✅ Sufficient memory for embedding operations" -ForegroundColor Green
} else {
    Write-Host "   ⚠️ Low memory - may affect embedding performance" -ForegroundColor Yellow
}

Write-Host "`n🎯 Diagnosis Summary:" -ForegroundColor Cyan
Write-Host "===================" -ForegroundColor Cyan
Write-Host "If Ollama embedding test fails, that is the root cause."
Write-Host "Fix: Restart Ollama service or try a different embedding model."
Write-Host "If resources are low, close other applications."
Write-Host "If AnythingLLM API fails, check the application logs."