param(
    [string]$DocumentPath = ".\downloads\Decagon_Boundary_Scan.docx",
    [string]$OutputPath = ".\decagon_word_content.txt"
)

try {
    Write-Host "Opening Word document: $DocumentPath"
    
    # Try using .NET Word interop
    Add-Type -AssemblyName Microsoft.Office.Interop.Word
    
    $word = New-Object -ComObject Word.Application
    $word.Visible = $false
    $word.DisplayAlerts = 0  # wdAlertsNone
    
    Write-Host "Opening document..."
    $doc = $word.Documents.Open((Resolve-Path $DocumentPath).Path)
    
    Write-Host "Document opened successfully"
    Write-Host "Pages: $($doc.ComputeStatistics([Microsoft.Office.Interop.Word.WdStatistic]::wdStatisticPages))"
    Write-Host "Words: $($doc.ComputeStatistics([Microsoft.Office.Interop.Word.WdStatistic]::wdStatisticWords))"
    
    # Extract all text content
    $content = $doc.Content.Text
    
    # Extract tables if any
    $tableContent = ""
    if ($doc.Tables.Count -gt 0) {
        Write-Host "Found $($doc.Tables.Count) tables"
        for ($i = 1; $i -le $doc.Tables.Count; $i++) {
            $table = $doc.Tables.Item($i)
            $tableContent += "`n`n=== TABLE $i ===`n"
            
            # Extract table headers and data
            for ($row = 1; $row -le $table.Rows.Count; $row++) {
                $rowText = ""
                for ($col = 1; $col -le $table.Columns.Count; $col++) {
                    try {
                        $cell = $table.Cell($row, $col)
                        $cellText = $cell.Range.Text -replace "`r", " " -replace "`a", ""
                        $rowText += "$cellText | "
                    }
                    catch {
                        $rowText += "N/A | "
                    }
                }
                $tableContent += "$rowText`n"
            }
        }
    } else {
        Write-Host "No tables found in document"
    }
    
    # Save content to file
    $fullContent = @"
=== DECAGON WORD DOCUMENT CONTENT ===
Document: $DocumentPath
Extracted: $(Get-Date)

=== MAIN CONTENT ===
$content

$tableContent
"@
    
    $fullContent | Out-File -FilePath $OutputPath -Encoding UTF8
    
    Write-Host "Content extracted to: $OutputPath"
    Write-Host "Content length: $($content.Length) characters"
    
    # Show first 2000 characters as preview
    Write-Host ""
    Write-Host "=== PREVIEW (First 2000 chars) ==="
    $previewLength = [Math]::Min(2000, $content.Length)
    Write-Host $content.Substring(0, $previewLength)
    
    if ($content.Length -gt 2000) {
        Write-Host "... [content truncated, see full file: $OutputPath]"
    }
    
    return $OutputPath
}
catch {
    Write-Host "Error extracting Word content: $($_.Exception.Message)"
    Write-Host "Stack trace: $($_.ScriptStackTrace)"
}
finally {
    # Close document and Word
    if ($doc) { 
        try { $doc.Close() } catch {}
    }
    if ($word) { 
        try { $word.Quit() } catch {}
    }
    
    # Clean up COM objects
    if ($doc) { [System.Runtime.Interopservices.Marshal]::ReleaseComObject($doc) | Out-Null }
    if ($word) { [System.Runtime.Interopservices.Marshal]::ReleaseComObject($word) | Out-Null }
    
    [System.GC]::Collect()
    [System.GC]::WaitForPendingFinalizers()
}