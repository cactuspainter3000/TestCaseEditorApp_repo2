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
        [string]$Method = "GET"
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

    try {
        Write-ProbeStep "Probing $Method $Url"
        $resp = Invoke-WebRequest -Uri $Url -Method $Method -Headers $Headers -TimeoutSec 30 -ErrorAction Stop
        $record.Status = [int]$resp.StatusCode
        $record.Success = $true

        try {
            $json = $resp.Content | ConvertFrom-Json
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
    } catch {
        $record.Success = $false
        $record.Status = "ERR"

        if ($_.Exception.Response) {
            try {
                $record.Status = [string]([int]$_.Exception.Response.StatusCode)
            } catch {
                $record.Status = "ERR"
            }
        }

        $record.Notes = $_.Exception.Message
    }

    $record.DurationMs = [int]((Get-Date) - $startedAt).TotalMilliseconds
    if ($record.Success) {
        Write-ProbeStep "Completed $Method $Url in $($record.DurationMs) ms"
    } else {
        Write-ProbeStep "Failed $Method $Url after $($record.DurationMs) ms"
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
            $resp = Invoke-WebRequest -Uri $url -Method Get -Headers $Headers -TimeoutSec 30 -ErrorAction Stop
            $json = $resp.Content | ConvertFrom-Json
            if ($json.paths) {
                $pathNames = $json.paths.PSObject.Properties.Name
                return [PSCustomObject]@{
                    Url = $url
                    Paths = $pathNames
                }
            }
        } catch {
            continue
        }
    }

    return $null
}

Write-Host "=== Jama Relationship Capability Probe ===" -ForegroundColor Cyan
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

$baseEndpoints = @(
    "$BaseUrl/rest/v1/relationshiptypes?project=$ProjectId&maxResults=20",
    "$BaseUrl/rest/v1/projects/$ProjectId/relationshiptypes",
    "$BaseUrl/rest/v1/relationships?project=$ProjectId&maxResults=20",
    "$BaseUrl/rest/v1/attachments?project=$ProjectId&maxResults=20"
)

foreach ($endpoint in $baseEndpoints) {
    $results.Add((Invoke-Probe -Url $endpoint -Headers $apiHeaders))
}

$attachmentId = $null
if ($SeedItemId -and $SeedItemId -gt 0) {
    Write-ProbeStep "Probing seed-item relationship and attachment endpoints for item $SeedItemId"
    $results.Add((Invoke-Probe -Url "$BaseUrl/rest/v1/items/$SeedItemId/attachments?maxResults=20" -Headers $apiHeaders))
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
[void]$report.AppendLine("- Timestamp (UTC): $(Get-Date -AsUTC -Format \"yyyy-MM-dd HH:mm:ss\")")
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
[void]$report.AppendLine("- If item relationship endpoints succeed while attachment-relationship candidate endpoints return 404/400, your tenant likely supports item-to-item relationships only.")
[void]$report.AppendLine("- In that model, attachments are associated to items and traceability should point to the source item, with attachment metadata retained as provenance.")

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
