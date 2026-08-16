#requires -Version 5.1
<#
.SYNOPSIS
Builds the Debug IceCrow test client for real Hearthstone validation.

.DESCRIPTION
Verifies the pinned .NET SDK, restores, and builds the Debug configuration.
With -Publish, additionally publishes a framework-dependent win-x64 layout to
the Git-ignored artifacts/test-win-x64 directory. Prints the exact executable
path. Never launches Hearthstone, never touches Git state, never enables
capture, and contains no secrets.

.EXAMPLE
./scripts/build-test-debug.ps1

.EXAMPLE
./scripts/build-test-debug.ps1 -Publish
#>
[CmdletBinding()]
param(
    [switch]$Publish
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

Write-Host 'Verifying the pinned .NET SDK (global.json)...'
$sdkVersion = dotnet --version
if ($LASTEXITCODE -ne 0) {
    throw 'The pinned .NET SDK from global.json is not available. Install it first.'
}
Write-Host "SDK: $sdkVersion"

dotnet restore IceCrow.sln
if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }

dotnet build IceCrow.sln -c Debug --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Debug build failed.' }

if ($Publish) {
    dotnet publish src/IceCrow.App/IceCrow.App.csproj `
        -c Debug `
        -r win-x64 `
        --self-contained false `
        -o artifacts/test-win-x64
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    $executable = Join-Path $repoRoot 'artifacts\test-win-x64\IceCrow.App.exe'
}
else {
    $executable = Join-Path $repoRoot 'src\IceCrow.App\bin\Debug\net10.0-windows\IceCrow.App.exe'
}

Write-Host ''
Write-Host 'Test client ready:'
Write-Host "  $executable"
Write-Host "Match capture starts OFF. Enable it explicitly in the developer window under 'Match capture'."
Write-Host 'Private captures are written to %LocalAppData%\IceCrow\private-captures\.'
