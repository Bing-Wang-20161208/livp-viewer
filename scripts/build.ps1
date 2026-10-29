param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$projectFile = Join-Path $projectRoot 'src/LivpViewer/LivpViewer.csproj'
$releaseFolder = Join-Path $projectRoot "dist/LivpViewer-$Runtime"
$archiveFile = Join-Path $projectRoot "dist/LivpViewer-$Runtime.zip"

if (Test-Path $releaseFolder) { Remove-Item $releaseFolder -Recurse -Force }
if (Test-Path $archiveFile) { Remove-Item $archiveFile -Force }

dotnet publish $projectFile -c Release -r $Runtime --self-contained true -p:PublishSingleFile=false -p:PublishTrimmed=false -o $releaseFolder
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }

Copy-Item (Join-Path $projectRoot 'scripts/register-livp.ps1') $releaseFolder
Copy-Item (Join-Path $projectRoot 'scripts/register.cmd') $releaseFolder
Copy-Item (Join-Path $projectRoot 'THIRD-PARTY.md') $releaseFolder
Compress-Archive -Path (Join-Path $releaseFolder '*') -DestinationPath $archiveFile -CompressionLevel Optimal
Write-Host "Built $archiveFile"
