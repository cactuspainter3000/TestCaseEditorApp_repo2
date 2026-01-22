param(
    [string]$DocumentPath = "c:\Users\e10653214\Downloads\Decagon_Boundary Scan.docx",
    [string]$OutputPath = ".\decagon_word_content.txt"
)

try {
    Write-Host "Opening Word document: $DocumentPath"
    
    # Create Word COM object
    $word = New-Object -ComObject Word.Application
    $word.Visible = $false
    
    # Open the document
    $doc = $word.Documents.Open($DocumentPath)
    
    Write-Host "Document opened successfully"
    Write-Host "Pages: $($doc.ComputeStatistics(2))"  # wdStatisticPages = 2
    Write-Host "Words: $($doc.ComputeStatistics(0))"  # wdStatisticWords = 0
    
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
    
    # Close document and Word
    $doc.Close()
    $word.Quit()
    
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
    
    # Show first 1000 characters as preview
    Write-Host ""
    Write-Host "=== PREVIEW (First 1000 chars) ==="
    Write-Host $content.Substring(0, [Math]::Min(1000, $content.Length))
    
    return $OutputPath
}
catch {
    Write-Host "Error extracting Word content: $($_.Exception.Message)"
    if ($word) {
        try { $word.Quit() } catch {}
    }
}
finally {
    # Clean up COM objects
    if ($doc) { [System.Runtime.Interopservices.Marshal]::ReleaseComObject($doc) | Out-Null }
    if ($word) { [System.Runtime.Interopservices.Marshal]::ReleaseComObject($word) | Out-Null }
}