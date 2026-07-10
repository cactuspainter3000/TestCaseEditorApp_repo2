param(
    [Parameter(Mandatory = $false)]
    [string]$BaseUrl = $env:JAMA_BASE_URL,

    [Parameter(Mandatory = $false)]
    [string]$ClientId = $env:JAMA_CLIENT_ID,

    [Parameter(Mandatory = $false)]
    [string]$ClientSecret = $env:JAMA_CLIENT_SECRET,

    [Parameter(Mandatory = $false)]
    [int]$ProjectId = $(if ($env:JAMA_PROJECT_ID) { [int]$env:JAMA_PROJECT_ID } else { 0 }),

    [Parameter(Mandatory = $false)]
    [int]$SeedItemId,

    [Parameter(Mandatory = $false)]
    [string]$ManualRequirementDocumentKey = $env:JAMA_MANUAL_REQ_KEY,

    [Parameter(Mandatory = $false)]
    [string]$ManualSourceDocumentKey = $env:JAMA_MANUAL_SOURCE_KEY,

    [Parameter(Mandatory = $false)]
    [string]$ReportPath = "jama-relationship-capability-report.md"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$ProbeScriptVersion = "seed-item-fast-path-v1"

Add-Type -AssemblyName System.Net.Http

function Write-ProbeStep {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Message
    )

    Write-Host "[$(Get-Date -Format 'HH:mm:ss')] $Message" -ForegroundColor Cyan
}

if ([string]::IsNullOrWhiteSpace($BaseUrl)) {
    throw "BaseUrl is required. Pass -BaseUrl or set JAMA_BASE_URL."
}

if ([string]::IsNullOrWhiteSpace($ClientId)) {
    throw "ClientId is required. Pass -ClientId or set JAMA_CLIENT_ID."
}

if ([string]::IsNullOrWhiteSpace($ClientSecret)) {
    throw "ClientSecret is required. Pass -ClientSecret or set JAMA_CLIENT_SECRET."
}

if ($ProjectId -le 0) {
    throw "ProjectId is required. Pass -ProjectId or set JAMA_PROJECT_ID to a positive integer."
}

function Get-OAuthToken {
    param(
        [string]$BaseUrl,
        [string]$ClientId,
        [string]$ClientSecret
    )

    $tokenUrl = "$BaseUrl/rest/oauth/token"
    $credentials = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes("$ClientId`:$ClientSecret"))

    $headers = @{
        Authorization = "Basic $credentials"
        "Content-Type" = "application/x-www-form-urlencoded"
    }

    $body = @{
        grant_type = "client_credentials"
        scope = "token_information"
    }

    $resp = Invoke-RestMethod -Uri $tokenUrl -Method Post -Headers $headers -Body $body -TimeoutSec 30
    if ([string]::IsNullOrWhiteSpace($resp.access_token)) {
        throw "Token request succeeded but no access_token was returned."
    }

    return $resp.access_token
}

function Invoke-Probe {
    param(
        [string]$Url,
        [hashtable]$Headers,
        [string]$Method = "GET",
        [int]$TimeoutSec = 30
    )

    $startedAt = Get-Date
    $record = [ordered]@{
        Method = $Method
        Url = $Url
        Status = ""
        Success = $false
        ResultCount = "n/a"
        Notes = ""
        DurationMs = 0
    }

    $content = ""

    try {
        Write-ProbeStep "Probing $Method $Url"
        $handler = New-Object System.Net.Http.HttpClientHandler
        $client = New-Object System.Net.Http.HttpClient($handler)
        $client.Timeout = [TimeSpan]::FromSeconds([Math]::Max(1, $TimeoutSec))

        try {
            foreach ($headerName in $Headers.Keys) {
                $null = $client.DefaultRequestHeaders.TryAddWithoutValidation($headerName, [string]$Headers[$headerName])
            }

            $request = New-Object System.Net.Http.HttpRequestMessage([System.Net.Http.HttpMethod]::$Method, $Url)
            $resp = $client.SendAsync($request).GetAwaiter().GetResult()
            $content = $resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        }
        finally {
            if ($null -ne $request) {
                $request.Dispose()
            }

            $client.Dispose()
            $handler.Dispose()
        }

        $record.Status = [int]$resp.StatusCode
        $record.Success = $resp.IsSuccessStatusCode

        if (-not $resp.IsSuccessStatusCode) {
            $errorSnippet = ""
            if (-not [string]::IsNullOrWhiteSpace($content)) {
                $normalizedContent = ($content -replace "`r", " " -replace "`n", " ").Trim()
                if ($normalizedContent.Length -gt 180) {
                    $normalizedContent = $normalizedContent.Substring(0, 180) + "..."
                }

                if (-not [string]::IsNullOrWhiteSpace($normalizedContent)) {
                    $errorSnippet = ": $normalizedContent"
                }
            }

            $record.Notes = "HTTP $([int]$resp.StatusCode) $($resp.ReasonPhrase)$errorSnippet"
        }

        if ($resp.IsSuccessStatusCode) {
            try {
                $json = $content | ConvertFrom-Json
                if ($null -ne $json.meta -and $null -ne $json.meta.pageInfo -and $null -ne $json.meta.pageInfo.resultCount) {
                    $record.ResultCount = [string]$json.meta.pageInfo.resultCount
                } elseif ($null -ne $json.data) {
                    if ($json.data -is [System.Array]) {
                        $record.ResultCount = [string]$json.data.Count
                    } else {
                        $record.ResultCount = "1"
                    }
                }
            } catch {
                $record.Notes = "Non-JSON or unexpected response body"
            }
        }
    } catch {
        $record.Success = $false
        $record.Status = "ERR"

        if ($_.Exception.PSObject.Properties.Name -contains 'StatusCode' -and $_.Exception.StatusCode) {
            $record.Status = [string]([int]$_.Exception.StatusCode)
        }
        else {
            try {
                $responseProperty = $_.Exception.PSObject.Properties['Response']
                if ($null -ne $responseProperty -and $null -ne $responseProperty.Value) {
                    $record.Status = [string]([int]$responseProperty.Value.StatusCode)
                }
            }
            catch {
                $record.Status = "ERR"
            }
        }

        if ($_.Exception.Message -match "task was canceled") {
            $record.Notes = "Request timed out after $TimeoutSec seconds"
        }
        else {
            $record.Notes = $_.Exception.Message
        }
    }

    $record.DurationMs = [int]((Get-Date) - $startedAt).TotalMilliseconds
    if ($record.Success) {
        Write-ProbeStep "Completed $Method $Url in $($record.DurationMs) ms"
    } else {
        Write-ProbeStep "Failed $Method $Url after $($record.DurationMs) ms"
        if (-not [string]::IsNullOrWhiteSpace($record.Notes)) {
            Write-ProbeStep "Failure details: $($record.Notes)"
        }
    }

    return [PSCustomObject]$record
}

function Get-SeedItemId {
    param(
        [string]$BaseUrl,
        [int]$ProjectId,
        [hashtable]$Headers
    )

    $url = "$BaseUrl/rest/v1/items?project=$ProjectId&maxResults=1"
    try {
        $resp = Invoke-RestMethod -Uri $url -Method Get -Headers $Headers -TimeoutSec 30
        if ($resp.data -and $resp.data.Count -gt 0) {
            return [int]$resp.data[0].id
        }
    } catch {
        return $null
    }

    return $null
}

function Get-AttachmentIdForItem {
    param(
        [string]$BaseUrl,
        [int]$ItemId,
        [hashtable]$Headers
    )

    $url = "$BaseUrl/rest/v1/items/$ItemId/attachments?maxResults=20"
    try {
        $resp = Invoke-RestMethod -Uri $url -Method Get -Headers $Headers -TimeoutSec 30
        if ($resp.data -and $resp.data.Count -gt 0) {
            return [int]$resp.data[0].id
        }
    } catch {
        return $null
    }

    return $null
}

function Get-ItemByDocumentKey {
    param(
        [string]$BaseUrl,
        [int]$ProjectId,
        [string]$DocumentKey,
        [hashtable]$Headers
    )

    if ([string]::IsNullOrWhiteSpace($DocumentKey) -or $ProjectId -le 0) {
        return $null
    }

    $escapedDocKey = [Uri]::EscapeDataString($DocumentKey)
    $url = "$BaseUrl/rest/v1/abstractitems?project=$ProjectId&documentKey=$escapedDocKey&maxResults=20"

    try {
        $resp = Invoke-RestMethod -Uri $url -Method Get -Headers $Headers -TimeoutSec 30
        if (-not $resp.data) {
            return $null
        }

        foreach ($item in $resp.data) {
            if ($item.documentKey -eq $DocumentKey) {
                return $item
            }
        }

        return $resp.data[0]
    } catch {
        return $null
    }
}

function Get-ItemRelationships {
    param(
        [string]$BaseUrl,
        [int]$ItemId,
        [hashtable]$Headers
    )

    $all = New-Object System.Collections.Generic.List[object]
    $endpoints = @(
        "$BaseUrl/rest/v1/abstractitems/$ItemId/upstreamrelationships?maxResults=200",
        "$BaseUrl/rest/v1/abstractitems/$ItemId/downstreamrelationships?maxResults=200"
    )

    foreach ($url in $endpoints) {
        try {
            $resp = Invoke-RestMethod -Uri $url -Method Get -Headers $Headers -TimeoutSec 30
            if ($resp.data) {
                foreach ($r in $resp.data) {
                    $all.Add($r)
                }
            }
        } catch {
            continue
        }
    }

    return $all
}

function Get-InterpretationSummary {
    param(
        [System.Collections.Generic.List[object]]$Results,
        [bool]$HasSeedItem,
        [bool]$HasAttachmentId
    )

    $summary = New-Object System.Collections.Generic.List[string]
    $successfulResults = @($Results | Where-Object { $_.Success })
    $relationshipTypeResults = @($successfulResults | Where-Object { $_.Url -match '/relationshiptypes' })
    $itemAttachmentResult = @($successfulResults | Where-Object { $_.Url -match '/items/\d+/attachments' } | Select-Object -First 1)
    $abstractRelationshipResults = @($Results | Where-Object { $_.Url -match '/abstractitems/\d+/(upstreamrelationships|downstreamrelationships)' })
    $genericRelationshipResults = @($Results | Where-Object { $_.Url -match '/relationships\?' -and $_.Url -notmatch 'attachment=' })
    $attachmentRelationshipResults = @($Results | Where-Object { $_.Url -match '/attachments/\d+/.+relationship|/relationships\?.*attachment=' })

    if ($relationshipTypeResults.Count -gt 0) {
        $summary.Add("Relationship type discovery is supported via the v1 relationshiptypes endpoints. Use that path to resolve valid relationship type IDs for project-scoped writes.")
    } else {
        $summary.Add("Relationship type discovery did not succeed on any tested endpoint, so write operations should not assume a relationship type ID can be resolved yet.")
    }

    if ($HasSeedItem -and $itemAttachmentResult.Count -gt 0) {
        $summary.Add("Seed-item attachment enumeration is supported. The current seed item returned $($itemAttachmentResult[0].ResultCount) attachments, so attachment provenance can be discovered from the owning item.")
    }

    if ($abstractRelationshipResults.Count -gt 0 -and (@($abstractRelationshipResults | Where-Object { $_.Success }).Count -eq 0)) {
        $summary.Add("The abstract-item upstream/downstream relationship endpoints are not available on this tenant as tested. Manual trace validation should not depend on those routes.")
    }

    if ($genericRelationshipResults.Count -gt 0 -and (@($genericRelationshipResults | Where-Object { $_.Status -eq 400 }).Count -gt 0)) {
        $summary.Add("The generic relationships collection exists but rejected the tested query shape. It likely requires different filters than project-only discovery.")
    }

    if (-not $HasAttachmentId -and $attachmentRelationshipResults.Count -eq 0) {
        $summary.Add("No attachment-specific relationship endpoints were exercised because the seed item had no attachments. Attachment trace probing will need an item that actually owns at least one attachment.")
    } elseif ($attachmentRelationshipResults.Count -gt 0 -and (@($attachmentRelationshipResults | Where-Object { $_.Success }).Count -eq 0)) {
        $summary.Add("Attachment-specific relationship routes did not succeed on the tested paths. Treat attachments as provenance on items unless a supported attachment trace route is identified separately.")
    }

    if ($summary.Count -eq 0) {
        $summary.Add("The probe did not identify a stable relationship traversal route from the tested endpoints. Prefer item-centric traceability and validate any alternate route against tenant-specific API documentation.")
    }

    return $summary
}

function Find-ApiDocsPaths {
    param(
        [string]$BaseUrl,
        [hashtable]$Headers
    )

    $candidates = @(
        "$BaseUrl/rest/latest/api-docs",
        "$BaseUrl/api-docs",
        "$BaseUrl/rest/v1/api-docs"
    )

    foreach ($url in $candidates) {
        try {
            Write-ProbeStep "Checking API docs endpoint $url"

            $handler = New-Object System.Net.Http.HttpClientHandler
            $client = New-Object System.Net.Http.HttpClient($handler)
            $client.Timeout = [TimeSpan]::FromSeconds(30)

            try {
                foreach ($headerName in $Headers.Keys) {
                    $null = $client.DefaultRequestHeaders.TryAddWithoutValidation($headerName, [string]$Headers[$headerName])
                }

                $request = New-Object System.Net.Http.HttpRequestMessage([System.Net.Http.HttpMethod]::Get, $url)
                $resp = $client.SendAsync($request).GetAwaiter().GetResult()
                if (-not $resp.IsSuccessStatusCode) {
                    Write-ProbeStep "API docs endpoint returned status $([int]$resp.StatusCode): $url"
                    continue
                }

                $content = $resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
            }
            finally {
                if ($null -ne $request) {
                    $request.Dispose()
                }

                $client.Dispose()
                $handler.Dispose()
            }

            $json = $content | ConvertFrom-Json
            if ($json.paths) {
                $pathNames = $json.paths.PSObject.Properties.Name
                Write-ProbeStep "API docs discovered at $url"
                return [PSCustomObject]@{
                    Url = $url
                    Paths = $pathNames
                }
            }
        } catch {
            Write-ProbeStep "API docs endpoint failed: $url"
            continue
        }
    }

    return $null
}

function Get-RequirementFieldInventory {
    param(
        [string]$BaseUrl,
        [hashtable]$Headers,
        [int]$ProjectId,
        [int]$SeedItemId
    )

    $result = [ordered]@{
        Success = $false
        Notes = ""
        RequirementItemTypes = New-Object System.Collections.Generic.List[object]
        FieldRows = New-Object System.Collections.Generic.List[object]
        PicklistSummaries = New-Object System.Collections.Generic.List[object]
        RelationshipFieldSnapshots = New-Object System.Collections.Generic.List[object]
    }

    function Get-DataArray {
        param([object]$Response)

        if ($null -eq $Response) {
            return @()
        }

        if ($Response -is [System.Array]) {
            return @($Response)
        }

        if ($Response.PSObject.Properties.Name -contains "data") {
            $dataValue = $Response.data
            if ($dataValue -is [System.Array]) {
                return @($dataValue)
            }

            if ($null -ne $dataValue) {
                return @($dataValue)
            }
        }

        return @($Response)
    }

    function Get-FirstSuccessfulResponse {
        param(
            [string[]]$Urls,
            [string]$Label,
            [hashtable]$Headers,
            [int]$TimeoutSec = 8
        )

        $attempts = New-Object System.Collections.Generic.List[string]
        foreach ($url in $Urls) {
            Write-ProbeStep "Trying $Label endpoint: $url"
            try {
                $resp = Invoke-WebRequest -Uri $url -Method Get -Headers $Headers -TimeoutSec $TimeoutSec
                $attempts.Add("$url -> $([int]$resp.StatusCode)")
                Write-ProbeStep "$Label endpoint status $([int]$resp.StatusCode): $url"
                if ($resp.StatusCode -ge 200 -and $resp.StatusCode -lt 300) {
                    $parsed = $null
                    if (-not [string]::IsNullOrWhiteSpace($resp.Content)) {
                        try {
                            $parsed = $resp.Content | ConvertFrom-Json
                        } catch {
                            $parsed = $null
                        }
                    }

                    return [PSCustomObject]@{
                        Success = $true
                        Url = $url
                        Response = $parsed
                        Attempts = @($attempts)
                        Notes = ""
                    }
                }
            } catch {
                $statusText = "ERR"
                $statusCode = $null
                if ($_.Exception.Response -and $_.Exception.Response.StatusCode) {
                    $statusCode = [int]$_.Exception.Response.StatusCode
                    $statusText = [string]$statusCode
                }

                $attempts.Add("$url -> $statusText")
            }
        }

        return [PSCustomObject]@{
            Success = $false
            Url = ""
            Response = $null
            Attempts = @($attempts)
            Notes = "No successful endpoint."
        }
    }

    $candidateTypes = New-Object System.Collections.Generic.List[object]
    $seedItemTypeId = $null
    $seedItemData = $null

    function Get-ValueShape {
        param([object]$Value)

        if ($null -eq $Value) {
            return "null"
        }

        if ($Value -is [string]) {
            return "string"
        }

        if ($Value -is [bool]) {
            return "boolean"
        }

        if ($Value -is [int] -or $Value -is [long] -or $Value -is [double] -or $Value -is [decimal]) {
            return "number"
        }

        if ($Value -is [System.Array]) {
            return "array"
        }

        return "object"
    }

    function Add-InferredSeedItemFields {
        param(
            [object]$SeedItemData,
            [int]$ItemTypeId,
            [string]$ItemTypeName,
            [System.Collections.Generic.List[object]]$FieldRows
        )

        if ($null -eq $SeedItemData -or -not ($SeedItemData.PSObject.Properties.Name -contains "fields") -or $null -eq $SeedItemData.fields) {
            return $false
        }

        foreach ($fieldProp in $SeedItemData.fields.PSObject.Properties) {
            $FieldRows.Add([PSCustomObject]@{
                ItemTypeId = $ItemTypeId
                ItemTypeName = $ItemTypeName
                FieldKey = [string]$fieldProp.Name
                FieldLabel = [string]$fieldProp.Name
                FieldType = Get-ValueShape -Value $fieldProp.Value
                Required = ""
                Notes = "inferred from seed item payload"
            })
        }

        return $FieldRows.Count -gt 0
    }

    function Add-RelationshipFieldSnapshots {
        param(
            [object]$SeedItemData,
            [System.Collections.Generic.List[object]]$Snapshots
        )

        if ($null -eq $SeedItemData -or -not ($SeedItemData.PSObject.Properties.Name -contains "fields") -or $null -eq $SeedItemData.fields) {
            return
        }

        foreach ($fieldProp in $SeedItemData.fields.PSObject.Properties) {
            $fieldName = [string]$fieldProp.Name
            if ($fieldName -notmatch 'relationship|trace|link') {
                continue
            }

            $valueText = ""
            if ($null -eq $fieldProp.Value) {
                $valueText = "<null>"
            } elseif ($fieldProp.Value -is [System.Array]) {
                $valueText = ($fieldProp.Value | ForEach-Object { [string]$_ }) -join '; '
            } else {
                $valueText = [string]$fieldProp.Value
            }

            if ($valueText.Length -gt 240) {
                $valueText = $valueText.Substring(0, 240) + "..."
            }

            $Snapshots.Add([PSCustomObject]@{
                FieldName = $fieldName
                ValueShape = Get-ValueShape -Value $fieldProp.Value
                Sample = $valueText
            })
        }
    }

    if ($SeedItemId -gt 0) {
        try {
            Write-ProbeStep "Resolving seed item type from item $SeedItemId"
            $seedItem = Invoke-RestMethod -Uri "$BaseUrl/rest/v1/items/$SeedItemId" -Method Get -Headers $Headers -TimeoutSec 8
            if ($seedItem -and $seedItem.data -and $seedItem.data.itemType) {
                $seedItemData = $seedItem.data
                $seedItemTypeId = [int]$seedItem.data.itemType
                Add-RelationshipFieldSnapshots -SeedItemData $seedItemData -Snapshots $result.RelationshipFieldSnapshots
            }
        } catch {
            Write-ProbeStep "Could not resolve seed item type: $($_.Exception.Message)"
        }
    }

    if ($seedItemTypeId) {
        Write-ProbeStep "Using seed-derived item type id $seedItemTypeId as primary field schema target"
        $candidateTypes.Add([PSCustomObject]@{
            id = $seedItemTypeId
            name = ""
            typeKey = ""
        })
    }

    if ($candidateTypes.Count -eq 0) {
        Write-ProbeStep "Discovering item type catalog (fallback path)"
        $itemTypeCatalogCandidates = @(
            "$BaseUrl/rest/v1/itemtypes?project=$ProjectId&maxResults=50",
            "$BaseUrl/rest/v1/itemtypes?maxResults=50",
            "$BaseUrl/rest/v1/itemtypes",
            "$BaseUrl/rest/latest/itemtypes?project=$ProjectId&maxResults=50",
            "$BaseUrl/rest/latest/itemtypes"
        )

        $itemTypeCatalogResult = Get-FirstSuccessfulResponse -Urls $itemTypeCatalogCandidates -Label "item type catalog" -Headers $Headers -TimeoutSec 5
        if (-not $itemTypeCatalogResult.Success) {
            $result.Notes = "Failed to query item types. Attempts: $($itemTypeCatalogResult.Attempts -join ' | ')"
            return [PSCustomObject]$result
        }

        $itemTypes = Get-DataArray -Response $itemTypeCatalogResult.Response
        foreach ($t in $itemTypes) {
            $name = ""
            $typeKey = ""
            if ($t.PSObject.Properties.Name -contains "name") { $name = [string]$t.name }
            if ($t.PSObject.Properties.Name -contains "typeKey") { $typeKey = [string]$t.typeKey }

            if ($name -match "requirement" -or $typeKey -match "requirement") {
                $candidateTypes.Add($t)
            }

            if ($candidateTypes.Count -ge 2) {
                break
            }
        }
    }

    if ($candidateTypes.Count -eq 0) {
        if ([string]::IsNullOrWhiteSpace($result.Notes)) {
            $result.Notes = "No requirement-like item types were identified."
        }

        return [PSCustomObject]$result
    }

    $picklistIds = New-Object 'System.Collections.Generic.HashSet[int]'

    foreach ($candidate in $candidateTypes) {
        $typeId = [int]$candidate.id
        $typeName = ""
        $typeKey = ""
        if ($candidate.PSObject.Properties.Name -contains "name") { $typeName = [string]$candidate.name }
        if ($candidate.PSObject.Properties.Name -contains "typeKey") { $typeKey = [string]$candidate.typeKey }

        $result.RequirementItemTypes.Add([PSCustomObject]@{
            Id = $typeId
            Name = $typeName
            TypeKey = $typeKey
        })

        Write-ProbeStep "Discovering fields for item type $typeId ($typeName)"
        $fieldEndpointCandidates = @(
            "$BaseUrl/rest/v1/itemtypes/${typeId}/fields",
            "$BaseUrl/rest/latest/itemtypes/${typeId}/fields"
        )

        $typeResult = Get-FirstSuccessfulResponse -Urls $fieldEndpointCandidates -Label "item type fields" -Headers $Headers -TimeoutSec 5
        if (-not $typeResult.Success) {
            $inferred = $false
            if ($seedItemTypeId -eq $typeId -and $null -ne $seedItemData) {
                Write-ProbeStep "Item type field endpoints unavailable; inferring fields from seed item payload"
                $inferred = Add-InferredSeedItemFields -SeedItemData $seedItemData -ItemTypeId $typeId -ItemTypeName $typeName -FieldRows $result.FieldRows
            }

            if (-not $inferred) {
                $result.FieldRows.Add([PSCustomObject]@{
                    ItemTypeId = $typeId
                    ItemTypeName = $typeName
                    FieldKey = "<error>"
                    FieldLabel = "Failed to load field schema"
                    FieldType = ""
                    Required = ""
                    Notes = "Attempts: $($typeResult.Attempts -join ' | ')"
                })
            }
            continue
        }

        $fieldObjects = @()
        $typeResponse = $typeResult.Response
        if ($typeResponse) {
            if ($typeResponse.PSObject.Properties.Name -contains "data") {
                if ($typeResponse.data -is [System.Array]) {
                    $fieldObjects = @($typeResponse.data)
                } elseif ($typeResponse.data -and $typeResponse.data.PSObject.Properties.Name -contains "fields") {
                    $fieldContainer = $typeResponse.data.fields
                    if ($fieldContainer -is [System.Array]) {
                        $fieldObjects = @($fieldContainer)
                    } else {
                        foreach ($p in $fieldContainer.PSObject.Properties) {
                            if ($null -ne $p.Value) {
                                $fieldObjects += $p.Value
                            }
                        }
                    }
                }
            }

            if ($fieldObjects.Count -eq 0 -and ($typeResponse.PSObject.Properties.Name -contains "fields")) {
                $fieldContainer = $typeResponse.fields
                if ($fieldContainer -is [System.Array]) {
                    $fieldObjects = @($fieldContainer)
                } else {
                    foreach ($p in $fieldContainer.PSObject.Properties) {
                        if ($null -ne $p.Value) {
                            $fieldObjects += $p.Value
                        }
                    }
                }
            }
        }

        foreach ($f in $fieldObjects) {
            $fieldKey = ""
            $fieldLabel = ""
            $fieldType = ""
            $required = ""

            if ($f.PSObject.Properties.Name -contains "fieldName") { $fieldKey = [string]$f.fieldName }
            elseif ($f.PSObject.Properties.Name -contains "name") { $fieldKey = [string]$f.name }
            elseif ($f.PSObject.Properties.Name -contains "key") { $fieldKey = [string]$f.key }

            if ($f.PSObject.Properties.Name -contains "label") { $fieldLabel = [string]$f.label }
            elseif ($f.PSObject.Properties.Name -contains "display") { $fieldLabel = [string]$f.display }
            elseif ($f.PSObject.Properties.Name -contains "name") { $fieldLabel = [string]$f.name }

            if ($f.PSObject.Properties.Name -contains "fieldType") { $fieldType = [string]$f.fieldType }
            elseif ($f.PSObject.Properties.Name -contains "dataType") { $fieldType = [string]$f.dataType }
            elseif ($f.PSObject.Properties.Name -contains "type") { $fieldType = [string]$f.type }

            if ($f.PSObject.Properties.Name -contains "required") { $required = [string]$f.required }
            elseif ($f.PSObject.Properties.Name -contains "isRequired") { $required = [string]$f.isRequired }

            $notes = ""
            if ($fieldKey -match '^PL\$(\d+)$') {
                $picklistId = [int]$Matches[1]
                $null = $picklistIds.Add($picklistId)
                $notes = "picklist"
            }

            $result.FieldRows.Add([PSCustomObject]@{
                ItemTypeId = $typeId
                ItemTypeName = $typeName
                FieldKey = $fieldKey
                FieldLabel = $fieldLabel
                FieldType = $fieldType
                Required = $required
                Notes = $notes
            })
        }
    }

    foreach ($pid in $picklistIds) {
        $picklistCandidates = @(
            "$BaseUrl/rest/v1/picklists/$pid/options",
            "$BaseUrl/rest/latest/picklists/$pid/options",
            "$BaseUrl/rest/v1/picklistoptions/$pid?maxResults=500",
            "$BaseUrl/rest/latest/picklistoptions/$pid?maxResults=500"
        )

        $picklistResult = Get-FirstSuccessfulResponse -Urls $picklistCandidates -Label "picklist options" -Headers $Headers -TimeoutSec 6
        if ($picklistResult.Success) {
            $picklistResp = $picklistResult.Response
            $optionCount = 0
            if ($picklistResp -and $picklistResp.data) {
                if ($picklistResp.data -is [System.Array]) {
                    $optionCount = $picklistResp.data.Count
                } else {
                    $optionCount = 1
                }
            } elseif ($picklistResp -is [System.Array]) {
                $optionCount = $picklistResp.Count
            }

            $result.PicklistSummaries.Add([PSCustomObject]@{
                PicklistId = $pid
                OptionCount = $optionCount
                Notes = ""
            })
        } else {
            $result.PicklistSummaries.Add([PSCustomObject]@{
                PicklistId = $pid
                OptionCount = ""
                Notes = "Attempts: $($picklistResult.Attempts -join ' | ')"
            })
        }
    }

    $result.Success = $result.RequirementItemTypes.Count -gt 0
    if (-not $result.Success -and [string]::IsNullOrWhiteSpace($result.Notes)) {
        $result.Notes = "No requirement item types resolved."
    }

    return [PSCustomObject]$result
}

Write-Host "=== Jama Relationship Capability Probe ===" -ForegroundColor Cyan
Write-Host "Probe Script Version: $ProbeScriptVersion" -ForegroundColor Gray
Write-Host "Base URL: $BaseUrl" -ForegroundColor Gray
Write-Host "Project: $ProjectId" -ForegroundColor Gray

$probeStartedAt = Get-Date

Write-ProbeStep "Requesting OAuth token"
$token = Get-OAuthToken -BaseUrl $BaseUrl -ClientId $ClientId -ClientSecret $ClientSecret
$apiHeaders = @{ Authorization = "Bearer $token" }
Write-ProbeStep "OAuth token acquired"

if (-not $PSBoundParameters.ContainsKey("SeedItemId") -or $SeedItemId -le 0) {
    Write-ProbeStep "Looking up a seed item"
    $autoSeed = Get-SeedItemId -BaseUrl $BaseUrl -ProjectId $ProjectId -Headers $apiHeaders
    if ($autoSeed) {
        $SeedItemId = $autoSeed
    }
    Write-ProbeStep "Seed item resolved: $(if ($SeedItemId) { $SeedItemId } else { 'none found' })"
}

$results = New-Object System.Collections.Generic.List[object]
$relationshipProbeLastId = 0
$relationshipProbeMaxResults = 5
$relationshipProbeTimeoutSec = 5
$relationshipReadTimedOut = $false

$baseEndpoints = @(
    "$BaseUrl/rest/v1/projects/$ProjectId/relationshiptypes",
    "$BaseUrl/rest/v1/relationshiptypes?project=$ProjectId&maxResults=20",
    "$BaseUrl/rest/v1/relationshiptypes",
    "$BaseUrl/rest/v1/relationships?project=$ProjectId&maxResults=20",
    "$BaseUrl/rest/v1/attachments?project=$ProjectId&maxResults=20"
)

foreach ($endpoint in $baseEndpoints) {
    $results.Add((Invoke-Probe -Url $endpoint -Headers $apiHeaders))
}

$projectRelationshipProbe = Invoke-Probe -Url "$BaseUrl/rest/v1/relationships?project=$ProjectId&lastId=$relationshipProbeLastId&maxResults=$relationshipProbeMaxResults" -Headers $apiHeaders -TimeoutSec $relationshipProbeTimeoutSec
$results.Add($projectRelationshipProbe)
if (-not [string]::IsNullOrWhiteSpace($projectRelationshipProbe.Notes) -and $projectRelationshipProbe.Notes -like "Request timed out*") {
    $relationshipReadTimedOut = $true
}

$attachmentId = $null
if ($SeedItemId -and $SeedItemId -gt 0) {
    Write-ProbeStep "Probing seed-item relationship and attachment endpoints for item $SeedItemId"
    $results.Add((Invoke-Probe -Url "$BaseUrl/rest/v1/items/$SeedItemId/attachments?maxResults=20" -Headers $apiHeaders))
    $results.Add((Invoke-Probe -Url "$BaseUrl/rest/v1/relationships?fromItem=$SeedItemId&maxResults=20" -Headers $apiHeaders))
    $results.Add((Invoke-Probe -Url "$BaseUrl/rest/v1/relationships?toItem=$SeedItemId&maxResults=20" -Headers $apiHeaders))
    $results.Add((Invoke-Probe -Url "$BaseUrl/rest/v1/relationships?item=$SeedItemId&maxResults=20" -Headers $apiHeaders))
    $results.Add((Invoke-Probe -Url "$BaseUrl/rest/v1/relationships?project=$ProjectId&fromItem=$SeedItemId&lastId=$relationshipProbeLastId&maxResults=$relationshipProbeMaxResults" -Headers $apiHeaders -TimeoutSec $relationshipProbeTimeoutSec))
    $results.Add((Invoke-Probe -Url "$BaseUrl/rest/v1/relationships?project=$ProjectId&toItem=$SeedItemId&lastId=$relationshipProbeLastId&maxResults=$relationshipProbeMaxResults" -Headers $apiHeaders -TimeoutSec $relationshipProbeTimeoutSec))
    $results.Add((Invoke-Probe -Url "$BaseUrl/rest/v1/relationships?project=$ProjectId&item=$SeedItemId&lastId=$relationshipProbeLastId&maxResults=$relationshipProbeMaxResults" -Headers $apiHeaders -TimeoutSec $relationshipProbeTimeoutSec))
    if ($relationshipReadTimedOut) {
        Write-ProbeStep "Baseline project+lastId relationship query timed out, but item-filtered project queries were still attempted with lastId=$relationshipProbeLastId."
    }
    $results.Add((Invoke-Probe -Url "$BaseUrl/rest/v1/items/$SeedItemId/upstreamrelationships?maxResults=20" -Headers $apiHeaders))
    $results.Add((Invoke-Probe -Url "$BaseUrl/rest/v1/items/$SeedItemId/downstreamrelationships?maxResults=20" -Headers $apiHeaders))
    $results.Add((Invoke-Probe -Url "$BaseUrl/rest/v1/items/$SeedItemId/upstreamrelated?maxResults=20" -Headers $apiHeaders))
    $results.Add((Invoke-Probe -Url "$BaseUrl/rest/v1/items/$SeedItemId/downstreamrelated?maxResults=20" -Headers $apiHeaders))
    $results.Add((Invoke-Probe -Url "$BaseUrl/rest/v1/abstractitems/$SeedItemId/upstreamrelationships?maxResults=20" -Headers $apiHeaders))
    $results.Add((Invoke-Probe -Url "$BaseUrl/rest/v1/abstractitems/$SeedItemId/downstreamrelationships?maxResults=20" -Headers $apiHeaders))

    $attachmentId = Get-AttachmentIdForItem -BaseUrl $BaseUrl -ItemId $SeedItemId -Headers $apiHeaders
    Write-ProbeStep "Attachment lookup result for seed item: $(if ($attachmentId) { $attachmentId } else { 'none found' })"
}

if ($attachmentId) {
    Write-ProbeStep "Probing attachment relationship endpoints for attachment $attachmentId"
    $attachmentRelationshipCandidates = @(
        "$BaseUrl/rest/v1/attachments/$attachmentId",
        "$BaseUrl/rest/v1/attachments/$attachmentId/comments",
        "$BaseUrl/rest/v1/attachments/$attachmentId/relationships",
        "$BaseUrl/rest/v1/attachments/$attachmentId/upstreamrelationships",
        "$BaseUrl/rest/v1/attachments/$attachmentId/downstreamrelationships",
        "$BaseUrl/rest/v1/relationships?attachment=$attachmentId&maxResults=20",
        "$BaseUrl/rest/v1/relationships?fromAttachment=$attachmentId&maxResults=20",
        "$BaseUrl/rest/v1/relationships?toAttachment=$attachmentId&maxResults=20"
    )

    foreach ($endpoint in $attachmentRelationshipCandidates) {
        $results.Add((Invoke-Probe -Url $endpoint -Headers $apiHeaders))
    }
}

Write-ProbeStep "Discovering API docs"
$swaggerDiscovery = Find-ApiDocsPaths -BaseUrl $BaseUrl -Headers $apiHeaders
$swaggerFindings = @()
if ($swaggerDiscovery) {
    foreach ($path in $swaggerDiscovery.Paths) {
        if ($path -match "attachment" -or $path -match "relationship") {
            $swaggerFindings += $path
        }
    }
}

$attachmentRelationshipPaths = @()
foreach ($p in $swaggerFindings) {
    if ($p -match "attachment" -and $p -match "relationship") {
        $attachmentRelationshipPaths += $p
    }
}

Write-ProbeStep "Discovering requirement field schema"
$requirementFieldInventory = Get-RequirementFieldInventory -BaseUrl $BaseUrl -Headers $apiHeaders -ProjectId $ProjectId -SeedItemId $(if ($SeedItemId) { $SeedItemId } else { 0 })

$manualTraceCheck = [ordered]@{
    Enabled = $false
    RequirementDocumentKey = $ManualRequirementDocumentKey
    SourceDocumentKey = $ManualSourceDocumentKey
    RequirementItemId = $null
    SourceItemId = $null
    RelationshipFound = $false
    RelationshipCountOnRequirement = 0
    Notes = ""
}

if (-not [string]::IsNullOrWhiteSpace($ManualRequirementDocumentKey) -and -not [string]::IsNullOrWhiteSpace($ManualSourceDocumentKey)) {
    $manualTraceCheck.Enabled = $true
    Write-ProbeStep "Running manual trace validation"

    $reqItem = Get-ItemByDocumentKey -BaseUrl $BaseUrl -ProjectId $ProjectId -DocumentKey $ManualRequirementDocumentKey -Headers $apiHeaders
    $srcItem = Get-ItemByDocumentKey -BaseUrl $BaseUrl -ProjectId $ProjectId -DocumentKey $ManualSourceDocumentKey -Headers $apiHeaders

    if ($reqItem) {
        $manualTraceCheck.RequirementItemId = $reqItem.id
    }

    if ($srcItem) {
        $manualTraceCheck.SourceItemId = $srcItem.id
    }

    if (-not $reqItem) {
        $manualTraceCheck.Notes = "Requirement document key '$ManualRequirementDocumentKey' was not resolved in project $ProjectId."
    } elseif (-not $srcItem) {
        $manualTraceCheck.Notes = "Source document key '$ManualSourceDocumentKey' was not resolved in project $ProjectId."
    } else {
        $reqRelationships = Get-ItemRelationships -BaseUrl $BaseUrl -ItemId ([int]$reqItem.id) -Headers $apiHeaders
        $manualTraceCheck.RelationshipCountOnRequirement = $reqRelationships.Count

        foreach ($rel in $reqRelationships) {
            $fromId = $null
            $toId = $null

            if ($rel.PSObject.Properties.Name -contains "fromItem") {
                $fromId = $rel.fromItem
            }

            if ($rel.PSObject.Properties.Name -contains "toItem") {
                $toId = $rel.toItem
            }

            if (($fromId -eq $reqItem.id -and $toId -eq $srcItem.id) -or ($fromId -eq $srcItem.id -and $toId -eq $reqItem.id)) {
                $manualTraceCheck.RelationshipFound = $true
                break
            }
        }

        if (-not $manualTraceCheck.RelationshipFound) {
            $manualTraceCheck.Notes = "Both items were resolved, but no direct relationship between them was found via upstream/downstream relationship endpoints for requirement item."
        }
    }
}

$report = New-Object System.Text.StringBuilder
[void]$report.AppendLine("# Jama Relationship Capability Probe Report")
[void]$report.AppendLine("")
[void]$report.AppendLine("- Timestamp (UTC): $((Get-Date).ToUniversalTime().ToString('yyyy-MM-dd HH:mm:ss'))")
[void]$report.AppendLine("- Base URL: $BaseUrl")
[void]$report.AppendLine("- Project ID: $ProjectId")
[void]$report.AppendLine("- Seed Item ID: $(if ($SeedItemId) { $SeedItemId } else { 'n/a' })")
[void]$report.AppendLine("- Attachment ID Used: $(if ($attachmentId) { $attachmentId } else { 'n/a' })")
[void]$report.AppendLine("")
[void]$report.AppendLine("## Endpoint Probe Results")
[void]$report.AppendLine("")
[void]$report.AppendLine("| Method | Status | Success | ResultCount | URL | Notes |")
[void]$report.AppendLine("|---|---:|:---:|---:|---|---|")
foreach ($r in $results) {
    $notes = if ([string]::IsNullOrWhiteSpace($r.Notes)) { "" } else { $r.Notes.Replace("|", "/") }
    [void]$report.AppendLine("| $($r.Method) | $($r.Status) | $($r.Success) | $($r.ResultCount) | $($r.Url) | $notes |")
}

[void]$report.AppendLine("")
[void]$report.AppendLine("## API Docs Discovery")
if ($swaggerDiscovery) {
    [void]$report.AppendLine("")
    [void]$report.AppendLine("- API docs endpoint discovered: $($swaggerDiscovery.Url)")
    [void]$report.AppendLine("- Paths containing 'attachment' or 'relationship': $($swaggerFindings.Count)")

    if ($attachmentRelationshipPaths.Count -gt 0) {
        [void]$report.AppendLine("- Attachment+relationship combined paths discovered:")
        foreach ($p in $attachmentRelationshipPaths) {
            [void]$report.AppendLine("  - $p")
        }
    } else {
        [void]$report.AppendLine("- No API path name contained both 'attachment' and 'relationship'.")
    }
} else {
    [void]$report.AppendLine("")
    [void]$report.AppendLine("- Could not retrieve API docs JSON from standard endpoints.")
}

[void]$report.AppendLine("")
[void]$report.AppendLine("## Preliminary Interpretation")
[void]$report.AppendLine("")
$interpretationSummary = Get-InterpretationSummary -Results $results -HasSeedItem ([bool]($SeedItemId -and $SeedItemId -gt 0)) -HasAttachmentId ([bool]$attachmentId)
foreach ($line in $interpretationSummary) {
    [void]$report.AppendLine("- $line")
}

[void]$report.AppendLine("")
[void]$report.AppendLine("## Requirement Field Schema Discovery")
[void]$report.AppendLine("")
if ($requirementFieldInventory.Success) {
    [void]$report.AppendLine("- Requirement item types discovered: $($requirementFieldInventory.RequirementItemTypes.Count)")
    foreach ($t in $requirementFieldInventory.RequirementItemTypes) {
        [void]$report.AppendLine("  - ItemTypeId=$($t.Id), Name='$($t.Name)', TypeKey='$($t.TypeKey)'")
    }

    [void]$report.AppendLine("")
    [void]$report.AppendLine("### Requirement Field Rows")
    [void]$report.AppendLine("")
    [void]$report.AppendLine("| ItemTypeId | ItemTypeName | FieldKey | FieldLabel | FieldType | Required | Notes |")
    [void]$report.AppendLine("|---:|---|---|---|---|---|---|")
    foreach ($row in $requirementFieldInventory.FieldRows) {
        $itemTypeName = if ($null -eq $row.ItemTypeName) { "" } else { ([string]$row.ItemTypeName).Replace("|", "/") }
        $fieldKey = if ($null -eq $row.FieldKey) { "" } else { ([string]$row.FieldKey).Replace("|", "/") }
        $fieldLabel = if ($null -eq $row.FieldLabel) { "" } else { ([string]$row.FieldLabel).Replace("|", "/") }
        $fieldType = if ($null -eq $row.FieldType) { "" } else { ([string]$row.FieldType).Replace("|", "/") }
        $required = if ($null -eq $row.Required) { "" } else { ([string]$row.Required).Replace("|", "/") }
        $notes = if ($null -eq $row.Notes) { "" } else { ([string]$row.Notes).Replace("|", "/") }
        [void]$report.AppendLine("| $($row.ItemTypeId) | $itemTypeName | $fieldKey | $fieldLabel | $fieldType | $required | $notes |")
    }

    [void]$report.AppendLine("")
    [void]$report.AppendLine("### Picklist Resolution")
    [void]$report.AppendLine("")
    if ($requirementFieldInventory.PicklistSummaries.Count -gt 0) {
        [void]$report.AppendLine("| PicklistId | OptionCount | Notes |")
        [void]$report.AppendLine("|---:|---:|---|")
        foreach ($p in $requirementFieldInventory.PicklistSummaries) {
            $notes = if ($null -eq $p.Notes) { "" } else { ([string]$p.Notes).Replace("|", "/") }
            [void]$report.AppendLine("| $($p.PicklistId) | $($p.OptionCount) | $notes |")
        }
    } else {
        [void]$report.AppendLine("- No picklist-backed requirement fields were detected during this probe.")
    }
} else {
    [void]$report.AppendLine("- Field schema discovery did not complete successfully.")
    if (-not [string]::IsNullOrWhiteSpace($requirementFieldInventory.Notes)) {
        [void]$report.AppendLine("- Notes: $($requirementFieldInventory.Notes)")
    }
}

[void]$report.AppendLine("")
[void]$report.AppendLine("## Seed Item Relationship Field Snapshot")
[void]$report.AppendLine("")
if ($requirementFieldInventory.RelationshipFieldSnapshots.Count -gt 0) {
    [void]$report.AppendLine("| FieldName | ValueShape | Sample |")
    [void]$report.AppendLine("|---|---|---|")
    foreach ($snapshot in $requirementFieldInventory.RelationshipFieldSnapshots) {
        $fieldName = ([string]$snapshot.FieldName).Replace("|", "/")
        $valueShape = ([string]$snapshot.ValueShape).Replace("|", "/")
        $sample = ([string]$snapshot.Sample).Replace("|", "/")
        [void]$report.AppendLine("| $fieldName | $valueShape | $sample |")
    }
} else {
    [void]$report.AppendLine("- No relationship-, trace-, or link-named fields were detected on the seed item payload.")
}

[void]$report.AppendLine("")
[void]$report.AppendLine("## Known Manual Trace Validation")
[void]$report.AppendLine("")
if ($manualTraceCheck.Enabled) {
    [void]$report.AppendLine("- Requirement Document Key: $($manualTraceCheck.RequirementDocumentKey)")
    [void]$report.AppendLine("- Source Document Key: $($manualTraceCheck.SourceDocumentKey)")
    [void]$report.AppendLine("- Requirement Item ID: $(if ($manualTraceCheck.RequirementItemId) { $manualTraceCheck.RequirementItemId } else { 'not found' })")
    [void]$report.AppendLine("- Source Item ID: $(if ($manualTraceCheck.SourceItemId) { $manualTraceCheck.SourceItemId } else { 'not found' })")
    [void]$report.AppendLine("- Relationship Found Between Items: $($manualTraceCheck.RelationshipFound)")
    [void]$report.AppendLine("- Requirement Relationship Count (upstream + downstream): $($manualTraceCheck.RelationshipCountOnRequirement)")
    if (-not [string]::IsNullOrWhiteSpace($manualTraceCheck.Notes)) {
        [void]$report.AppendLine("- Notes: $($manualTraceCheck.Notes)")
    }
} else {
    [void]$report.AppendLine("- Manual trace validation skipped. Provide both ManualRequirementDocumentKey and ManualSourceDocumentKey (or env vars JAMA_MANUAL_REQ_KEY and JAMA_MANUAL_SOURCE_KEY).")
}

$reportText = $report.ToString()
Set-Content -LiteralPath $ReportPath -Value $reportText -Encoding UTF8

Write-ProbeStep "Report written to $ReportPath"
Write-ProbeStep "Total probe duration: $([int]((Get-Date) - $probeStartedAt).TotalSeconds) seconds"
Write-Host "Report written to: $ReportPath" -ForegroundColor Green
Write-Host "=== Probe Complete ===" -ForegroundColor Cyan
