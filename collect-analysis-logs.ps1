#!/usr/bin/env pwsh
# Collect and redact TestCaseEditorApp analysis logs for sharing via GitHub.

param(
    [string]$OutputZipPath = "",
    [switch]$IncludeDesktopDebug = $true,
    [int]$MaxDesktopFiles = 200,
    [switch]$OpenOutputFolder = $false
)

$ErrorActionPreference = "Stop"

function Write-Step {
    param([string]$Message)
    Write-Host "[collect-analysis-logs] $Message" -ForegroundColor Cyan
}

function Redact-Secrets {
    param([string]$Text)

    if ([string]::IsNullOrEmpty($Text)) { return $Text }

    # Common key/value secrets
    $Text = [regex]::Replace($Text, "(?im)\b(api[_-]?key|token|authorization|bearer|client[_-]?secret|password|pwd)\b\s*[:=]\s*[^\s,;\r\n]+", '$1=<REDACTED>')

    # OpenAI-style keys
    $Text = [regex]::Replace($Text, "(?i)\bsk-[A-Za-z0-9]{20,}\b", "<REDACTED_OPENAI_KEY>")

    # JWT-like blobs
    $Text = [regex]::Replace($Text, "(?i)\beyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\b", "<REDACTED_JWT>")

    # Basic auth headers
    $Text = [regex]::Replace($Text, "(?im)^\s*Authorization\s*:\s*Basic\s+[A-Za-z0-9+/=]+\s*$", "Authorization: Basic <REDACTED>")

    return $Text
}

function Copy-IfExists {
    param(
        [string]$SourcePath,
        [string]$DestPath
    )

    if (Test-Path $SourcePath) {
        New-Item -ItemType Directory -Force -Path (Split-Path $DestPath -Parent) | Out-Null
        Copy-Item $SourcePath $DestPath -Force
        return $true
    }

    return $false
}

$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
$workspace = Join-Path $env:TEMP "tce_logs_$timestamp"
$rawDir = Join-Path $workspace "raw"
$redactedDir = Join-Path $workspace "redacted"

if ([string]::IsNullOrWhiteSpace($OutputZipPath)) {
    $OutputZipPath = Join-Path $env:USERPROFILE "Desktop\tce_logs_$timestamp.zip"
}

Write-Step "Creating temporary workspace in $workspace"
New-Item -ItemType Directory -Force -Path $rawDir, $redactedDir | Out-Null

$appLog = Join-Path $env:LOCALAPPDATA "TestCaseEditorApp\logs\app.log"
$desktopDebugDir = Join-Path $env:USERPROFILE "Desktop\LLM_Debug"

$foundAny = $false

Write-Step "Collecting app log"
$foundAny = (Copy-IfExists -SourcePath $appLog -DestPath (Join-Path $rawDir "app.log")) -or $foundAny

if ($IncludeDesktopDebug) {
    Write-Step "Collecting desktop LLM debug files"
    if (Test-Path $desktopDebugDir) {
        $files = Get-ChildItem $desktopDebugDir -File | Sort-Object LastWriteTime -Descending | Select-Object -First $MaxDesktopFiles
        foreach ($file in $files) {
            Copy-IfExists -SourcePath $file.FullName -DestPath (Join-Path $rawDir $file.Name) | Out-Null
        }
        if ($files.Count -gt 0) { $foundAny = $true }
    }
}

if (-not $foundAny) {
    Write-Host "No log files found. Checked:" -ForegroundColor Yellow
    Write-Host "  $appLog" -ForegroundColor Yellow
    if ($IncludeDesktopDebug) {
        Write-Host "  $desktopDebugDir" -ForegroundColor Yellow
    }
    exit 1
}

Write-Step "Redacting secrets"
$rawFiles = Get-ChildItem $rawDir -File -Recurse
foreach ($file in $rawFiles) {
    $relative = $file.FullName.Substring($rawDir.Length).TrimStart([char[]]@('\', '/'))
    $target = Join-Path $redactedDir $relative
    New-Item -ItemType Directory -Force -Path (Split-Path $target -Parent) | Out-Null

    try {
        $content = Get-Content $file.FullName -Raw
        $redacted = Redact-Secrets -Text $content
        Set-Content -Path $target -Value $redacted -Encoding UTF8
    }
    catch {
        # If we can't read as text, copy as-is
        Copy-Item $file.FullName $target -Force
    }
}

Write-Step "Creating zip: $OutputZipPath"
$outputDir = Split-Path $OutputZipPath -Parent
if (-not [string]::IsNullOrWhiteSpace($outputDir) -and -not (Test-Path $outputDir)) {
    New-Item -ItemType Directory -Force -Path $outputDir | Out-Null
}
if (Test-Path $OutputZipPath) {
    Remove-Item $OutputZipPath -Force
}
Compress-Archive -Path (Join-Path $redactedDir "*") -DestinationPath $OutputZipPath -CompressionLevel Optimal

Write-Host "Created redacted log bundle:" -ForegroundColor Green
Write-Host "  $OutputZipPath" -ForegroundColor Green

if ($OpenOutputFolder) {
    Start-Process explorer.exe "/select,`"$OutputZipPath`""
}
