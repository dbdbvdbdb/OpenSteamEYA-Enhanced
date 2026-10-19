param([switch]$NativeAot)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $projectRoot 'tools/Cs2ConfigVerifier/Cs2ConfigVerifier.csproj'
$output = Join-Path $projectRoot 'artifacts/cs2-feasibility/ci'
if ($NativeAot) {
    $binary = Join-Path $output 'bin'
    & dotnet publish $project -c Release -r win-x64 -p:PublishAot=true -p:SelfContained=true -p:RestoreLockedMode=true -o $binary
    if ($LASTEXITCODE -ne 0) { throw 'CS2 verifier Native AOT build failed.' }
    & (Join-Path $binary 'Cs2ConfigVerifier.exe') self-test --out (Join-Path $output 'isolation')
} else {
    & dotnet run --project $project --configuration Release -- self-test --out (Join-Path $output 'isolation')
}
if ($LASTEXITCODE -ne 0) { throw 'CS2 isolated checks failed. No real game test is implied.' }
