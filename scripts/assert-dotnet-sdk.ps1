$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$expected = (Get-Content -LiteralPath (Join-Path $projectRoot 'global.json') -Raw | ConvertFrom-Json).sdk.version
Push-Location $projectRoot
try {
    $actual = & dotnet --version
    if ($LASTEXITCODE -ne 0) { throw 'The pinned .NET SDK is unavailable; install the exact version in global.json.' }
    $actual = ($actual | Out-String).Trim()
    if ($actual -ne $expected) { throw "SDK mismatch: expected $expected; selected $actual. Do not regenerate lock files to bypass this check." }
    Write-Output "PASS .NET SDK $actual matches global.json; locked Native AOT restore can proceed."
}
finally { Pop-Location }
