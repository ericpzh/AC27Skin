$ErrorActionPreference = "Stop"

Write-Host "=== Building AC27Skin ==="
dotnet build -c Release
if ($LASTEXITCODE -ne 0) { throw "Build failed" }

$outDir = "release\AC27Skin"
Remove-Item -Recurse -Force $outDir -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $outDir | Out-Null

Copy-Item AC27Skin.dll $outDir\
Copy-Item -Recurse overrides $outDir\
Copy-Item README.md $outDir\

# Generate PDF from README
$pdfOk = $false
if (Get-Command pandoc -ErrorAction SilentlyContinue) {
    Write-Host "Generating README.pdf via pandoc..."
    pandoc README.md -o "$outDir\README.pdf" --pdf-engine=wkhtmltopdf -V margin-top=15 -V margin-bottom=15 -V margin-left=15 -V margin-right=15
    if ($LASTEXITCODE -eq 0) { $pdfOk = $true }
}
if (-not $pdfOk) {
    Write-Host "WARNING: pandoc not available, skipping README.pdf"
}

$zipName = "AC27Skin.zip"
Remove-Item $zipName -ErrorAction SilentlyContinue
Compress-Archive -Path "release\*" -DestinationPath $zipName
Remove-Item -Recurse -Force release

Write-Host "=== Done: $zipName ==="
$size = (Get-Item $zipName).Length
Write-Host "Size: $([math]::Round($size/1KB)) KB"
