param(
    [string]$Executable = (Join-Path $PSScriptRoot 'LivpViewer.exe')
)

$ErrorActionPreference = 'Stop'
$resolvedExecutable = (Resolve-Path $Executable).Path
$classes = 'HKCU:\Software\Classes'
$appKey = Join-Path $classes 'LivpViewer.livp'
$extensionKey = Join-Path $classes '.livp'

New-Item $appKey -Force | Out-Null
Set-Item $appKey 'LIVP Live Photo'
New-Item (Join-Path $appKey 'shell\open\command') -Force | Out-Null
Set-Item (Join-Path $appKey 'shell\open\command') ('"' + $resolvedExecutable + '" "%1"')
New-Item $extensionKey -Force | Out-Null
Set-Item $extensionKey 'LivpViewer.livp'
Write-Host 'LIVP association registered for the current user.'
Write-Host 'If Windows retains an existing default app, use Open with > Choose another app > Always.'
