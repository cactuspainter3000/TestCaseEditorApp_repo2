param(
    [string]$InputDir = ".",
    [string]$OutputDir = "./svg"
)

$ErrorActionPreference = "Stop"

if (-not (Get-Command mmdc -ErrorAction SilentlyContinue)) {
    Write-Host "Mermaid CLI (mmdc) is not installed." -ForegroundColor Yellow
    Write-Host "Install Node.js, then run: npm install -g @mermaid-js/mermaid-cli" -ForegroundColor Yellow
    exit 1
}

if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir | Out-Null
}

Get-ChildItem -Path $InputDir -Filter "*.mmd" | ForEach-Object {
    $outFile = Join-Path $OutputDir ($_.BaseName + ".svg")
    Write-Host "Rendering $($_.Name) -> $outFile"
    mmdc -i $_.FullName -o $outFile -b transparent
}

Write-Host "Done. SVG files are in $OutputDir"
