param(
    [string]$SchemaPath = "jama-openapi.json",
    [string]$EndpointFilter = "picklist|lookup"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $SchemaPath)) {
    Write-Error "Schema file not found: $SchemaPath"
}

$raw = Get-Content -LiteralPath $SchemaPath -Raw
$spec = $raw | ConvertFrom-Json

if (-not $spec.paths) {
    Write-Error "No 'paths' node found. This does not look like a valid OpenAPI/Swagger document."
}

$httpMethods = @("get", "post", "put", "patch", "delete", "head", "options")
$pathNames = $spec.paths.PSObject.Properties.Name | Sort-Object

$matches = @()

foreach ($pathName in $pathNames) {
    $pathItem = $spec.paths.$pathName
    if (-not $pathItem) {
        continue
    }

    foreach ($method in $httpMethods) {
        $operation = $pathItem.$method
        if (-not $operation) {
            continue
        }

        $opText = (
            @(
                $pathName
                $operation.operationId
                $operation.summary
                $operation.description
                (($operation.tags | ForEach-Object { $_ }) -join " ")
            ) -join " "
        )

        if ($opText -notmatch $EndpointFilter) {
            continue
        }

        $params = @()

        if ($pathItem.parameters) {
            foreach ($p in $pathItem.parameters) {
                $params += $p
            }
        }

        if ($operation.parameters) {
            foreach ($p in $operation.parameters) {
                $params += $p
            }
        }

        $queryParams = @()
        foreach ($p in $params) {
            if ($p.'in' -eq "query") {
                $required = if ($p.required) { "required" } else { "optional" }
                $type = ""

                if ($p.schema -and $p.schema.type) {
                    $type = $p.schema.type
                } elseif ($p.type) {
                    $type = $p.type
                }

                if ([string]::IsNullOrWhiteSpace($type)) {
                    $type = "unknown"
                }

                $queryParams += ("{0} ({1}, {2})" -f $p.name, $type, $required)
            }
        }

        $matches += [PSCustomObject]@{
            Method = $method.ToUpperInvariant()
            Path = $pathName
            OperationId = $operation.operationId
            Summary = $operation.summary
            QueryParams = if ($queryParams.Count -gt 0) { $queryParams -join ", " } else { "(none)" }
        }
    }
}

if ($matches.Count -eq 0) {
    Write-Host "No operations matched filter: $EndpointFilter"
    exit 0
}

$matches |
    Sort-Object Path, Method |
    Format-Table Method, Path, OperationId, QueryParams -AutoSize
