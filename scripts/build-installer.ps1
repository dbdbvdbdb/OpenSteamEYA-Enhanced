param([string]$Version = '0.2.4', [switch]$NativeAot = $true)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'publish-enhanced.ps1') -Version $Version -NativeAot:$NativeAot
$publishDir = Join-Path $projectRoot 'artifacts/release-win-x64'
if ($NativeAot) { $publishDir += '-aot' }
$iscc = (Get-Command iscc -ErrorAction SilentlyContinue).Source
if (-not $iscc) {
    foreach ($basePath in @($env:ProgramFiles, ${env:ProgramFiles(x86)})) {
        if (-not $basePath) { continue }
        foreach ($major in @(7, 6)) {
            $candidate = Join-Path $basePath "Inno Setup $major/ISCC.exe"
            if (Test-Path -LiteralPath $candidate) { $iscc = $candidate; break }
        }
        if ($iscc) { break }
    }
}
if (-not $iscc) { throw 'ISCC.exe was not found. The portable ZIP was produced; install Inno Setup to build an installer.' }
& $iscc "/DAppVersion=$Version" "/DPublishDir=$publishDir" "/DOutputDir=$(Join-Path $projectRoot 'artifacts')" (Join-Path $projectRoot 'build/installer/SteamEYA.iss')
if ($LASTEXITCODE -ne 0) { throw "ISCC failed with exit code $LASTEXITCODE." }
