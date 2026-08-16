#requires -Version 5.1
<#
.SYNOPSIS
Validates the newest private match capture through the official pipeline.

.DESCRIPTION
Locates the newest completed capture in the private capture directory
(%LocalAppData%\IceCrow\private-captures by convention), prints only its
filename, size, and timestamp, then runs the official FixtureTool
validate-recording (load + replay + safe summary) and the privacy-safe
analyzer. Never prints BattleTags, entity names, account ids, or raw text,
and never copies or uploads a capture. Use -Path to validate a specific
capture file instead of the newest one.

.EXAMPLE
./scripts/validate-latest-private-capture.ps1

.EXAMPLE
./scripts/validate-latest-private-capture.ps1 -Path "C:\...\capture.icecrow.json"
#>
[CmdletBinding()]
param(
    [string]$Path
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

if ($Path) {
    if (-not (Test-Path $Path -PathType Leaf)) {
        throw "Capture file not found: $Path"
    }
    $capture = Get-Item $Path
}
else {
    $captureRoot = Join-Path $env:LOCALAPPDATA 'IceCrow\private-captures'
    if (-not (Test-Path $captureRoot)) {
        throw "No private capture directory found at %LocalAppData%\IceCrow\private-captures. Play a captured match first."
    }
    $capture = Get-ChildItem $captureRoot -Filter '*.icecrow.json' -File |
        Where-Object { -not $_.Name.StartsWith('.') } |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
    if (-not $capture) {
        throw 'No completed capture found. Enable capture in the developer window and finish a match first.'
    }
}

Write-Host "Capture : $($capture.Name)"
Write-Host "Size    : $([math]::Round($capture.Length / 1MB, 2)) MB"
Write-Host "Saved   : $($capture.LastWriteTime)"
Write-Host ''

$fixtureTool = 'tools/IceCrow.FixtureTool/IceCrow.FixtureTool.csproj'
if (-not (Test-Path $fixtureTool)) {
    throw 'FixtureTool project not found; run this script from a repository checkout.'
}

Write-Host '=== Official load + replay summary ==='
dotnet run --project $fixtureTool -c Debug -v quiet -- validate-recording --input $capture.FullName
if ($LASTEXITCODE -ne 0) {
    throw 'OFFICIAL VALIDATION FAILED - the capture did not load or replay. Report this with the analyzer output.'
}

Write-Host '=== Privacy-safe analysis (top tags, TURN stream) ==='
dotnet run --project $fixtureTool -c Debug -v quiet -- analyze-recording --input $capture.FullName --top-tags 10
if ($LASTEXITCODE -ne 0) {
    throw 'Analyzer failed on a capture that loaded; report this.'
}
