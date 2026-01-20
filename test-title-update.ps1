cd "c:\Users\e10653214\Test Case Editor App Version 2_repo\TestCaseEditorApp"

# Start the app with logging focused on title updates
Write-Host "🚀 Testing Title Update System"
Write-Host "Expected: Title should change to 'Test Case Generator - [project name]' when project is opened"

# Run the app (built successfully above)
Start-Process -FilePath "bin\Debug\net8.0-windows\TestCaseEditorApp.exe" -WorkingDirectory (Get-Location)

Write-Host "✅ App launched. Please:"
Write-Host "1. Open a project using File menu"
Write-Host "2. Check if the title bar updates to show the project name"
Write-Host "3. Check the debug output for title-related messages"

# Give some time for the app to start
Start-Sleep -Seconds 3
Write-Host "📊 Check the app's title bar - it should show 'Test Case Generator - [ProjectName]' after opening a project"