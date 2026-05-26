$ErrorActionPreference = "Stop"

Write-Host "=== Building AC27Skin ==="
dotnet build -c Release
if ($LASTEXITCODE -ne 0) { throw "Build failed" }

$outDir = "release\tmp"
Remove-Item -Recurse -Force $outDir -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $outDir | Out-Null

Copy-Item AC27Skin.dll $outDir\
Copy-Item -Recurse overrides $outDir\
Copy-Item README.md $outDir\README.txt

$zipName = "AC27Skin.zip"
Remove-Item $zipName -ErrorAction SilentlyContinue
Compress-Archive -Path "$outDir\*" -DestinationPath $zipName
Remove-Item -Recurse -Force release

Write-Host "=== Done: $zipName ==="
$size = (Get-Item $zipName).Length
Write-Host "Size: $([math]::Round($size/1KB)) KB"
