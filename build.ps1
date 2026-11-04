param([string]$OutputDirectory = (Join-Path $PSScriptRoot 'dist'))

$ErrorActionPreference = 'Stop'
$projectPath = Join-Path $PSScriptRoot 'src\AzimuthFH3SaveEditor.csproj'
dotnet publish $projectPath -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o $OutputDirectory
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.txt'),(Join-Path $PSScriptRoot 'THIRD-PARTY-NOTICES.txt'),(Join-Path $PSScriptRoot 'LICENSE') -Destination $OutputDirectory
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'licenses') -Destination $OutputDirectory -Recurse -Force
Write-Host "Built: $OutputDirectory"
