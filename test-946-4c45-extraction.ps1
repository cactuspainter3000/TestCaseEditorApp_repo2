# Quick extraction test for 946-4C45-001A.docx
$docPath = 'exports/document-artifacts/20260722-122509/946-4C45-001A.docx'

if (Test-Path $docPath) {
    Write-Host "Document found: $docPath"
    Write-Host "Size: $((Get-Item $docPath).Length) bytes"
    Write-Host ""
    
    # Try to read the document.xml from the DOCX (which is a ZIP)
    Add-Type -Assembly System.IO.Compression
    $zip = [System.IO.Compression.ZipFile]::OpenRead($docPath)
    
    try {
        $entry = $zip.Entries | Where-Object { $_.Name -eq 'document.xml' }
        if ($entry) {
            Write-Host "Found document.xml in DOCX"
            $stream = $entry.Open()
            $reader = New-Object System.IO.StreamReader $stream
            $xml = $reader.ReadToEnd()
            $reader.Dispose()
            $stream.Dispose()
            
            # Display first 2000 chars of XML to see structure
            Write-Host "First 2000 characters of document.xml:"
            Write-Host $xml.Substring(0, [Math]::Min(2000, $xml.Length))
            Write-Host ""
            
            # Count paragraphs
            $paraCount = ([regex]::Matches($xml, '<w:p>')).Count
            Write-Host "Paragraph count: $paraCount"
            
            # Count text runs
            $textCount = ([regex]::Matches($xml, '<w:t>')).Count
            Write-Host "Text run count: $textCount"
            
            # Look for common requirement patterns
            if ($xml -match 'shall|MUST|WILL|must|will|should') {
                Write-Host "Found requirement keywords: YES"
            } else {
                Write-Host "Found requirement keywords: NO"
            }
        }
    }
    finally {
        $zip.Dispose()
    }
} else {
    Write-Host "Document not found: $docPath"
}
