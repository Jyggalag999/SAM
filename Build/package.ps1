# Builds a clean Release copy of SAM and zips it for sharing.
#   powershell -ExecutionPolicy Bypass -File package.ps1
# Output: dist\SAM-AutoIdle.zip (next to this script). Personal files such as
# idle-list.txt and debug symbols are never included.

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$stage = Join-Path $root 'dist\SAM-AutoIdle'
$zip = Join-Path $root 'dist\SAM-AutoIdle.zip'

if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }

# Build into the staging folder rather than upload\, which may be locked by running idlers.
dotnet build (Join-Path $root 'SAM.sln') -c Release -p:Platform=x86 "-p:OutputPath=$stage/"
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

Get-ChildItem -LiteralPath $stage -Recurse -File |
    Where-Object { $_.Extension -eq '.pdb' -or $_.Name -eq 'idle-list.txt' } |
    Remove-Item -Force
Copy-Item -LiteralPath (Join-Path $root 'LICENSE.txt') -Destination $stage
Copy-Item -LiteralPath (Join-Path $root 'HOW TO USE.txt') -Destination $stage

Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip
Write-Host "Created $zip" -ForegroundColor Green
