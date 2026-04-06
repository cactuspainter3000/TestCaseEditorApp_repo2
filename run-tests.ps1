<#
Runs `dotnet test` for each test project under `Tests/`.
Usage:
  .\run-tests.ps1            # runs all test projects found under Tests/
  .\run-tests.ps1 -StopOnFailure  # stop on first failing project
#>
param(
    [switch]$StopOnFailure,
    [string]$TestsPath = ".\Tests"
)

$projects = Get-ChildItem -Path $TestsPath -Recurse -Filter *.csproj -ErrorAction SilentlyContinue
if (-not $projects) {
    Write-Host "No test projects found under '$TestsPath'."
    exit 0
}

$overallExit = 0
foreach ($p in $projects) {
    Write-Host "`n=== Running tests for: $($p.FullName) ===`n"
    dotnet test $p.FullName
    $rc = $LASTEXITCODE
    if ($rc -ne 0) {
        Write-Host "Tests failed for $($p.FullName) with exit code $rc" -ForegroundColor Red
        $overallExit = $rc
        if ($StopOnFailure) { exit $rc }
    }
    else {
        Write-Host "Tests succeeded for $($p.FullName)" -ForegroundColor Green
    }
}

exit $overallExit
