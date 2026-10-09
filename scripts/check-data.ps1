$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$output = Join-Path $projectRoot ('artifacts/data-check-' + [guid]::NewGuid().ToString('N'))
& dotnet publish (Join-Path $projectRoot 'tests/AccountManagement.Tests/AccountManagement.Tests.csproj') -c Release -r win-x64 -p:PublishAot=true -p:RestoreLockedMode=true -p:SelfContained=true -o $output
if ($LASTEXITCODE -ne 0) { throw 'Native AOT data checks failed to build.' }
& (Join-Path $output 'AccountManagement.Tests.exe')
if ($LASTEXITCODE -ne 0) { throw 'Native AOT data checks failed.' }
