param([Parameter(Mandatory=$true)][string]$PublishDirectory)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$assets = Get-Content -LiteralPath (Join-Path $projectRoot 'SteamEyaWinUI/obj/project.assets.json') -Raw | ConvertFrom-Json
$destination = Join-Path $PublishDirectory 'third-party-licenses'
New-Item -ItemType Directory -Force -Path $destination | Out-Null
$inventory = @()
foreach ($entry in $assets.libraries.PSObject.Properties) {
    if ($entry.Value.type -ne 'package') { continue }
    $packageDir = $null
    foreach ($folder in $assets.packageFolders.PSObject.Properties.Name) {
        $candidate = Join-Path $folder $entry.Value.path
        if (Test-Path -LiteralPath $candidate) { $packageDir = $candidate; break }
    }
    if (-not $packageDir) { throw "Missing restored package $($entry.Name)" }
    $nuspec = Get-ChildItem -LiteralPath $packageDir -File -Filter '*.nuspec' | Select-Object -First 1
    [xml]$metadata = Get-Content -LiteralPath $nuspec.FullName -Raw
    $license = $metadata.SelectSingleNode('//*[local-name()="metadata"]/*[local-name()="license"]')
    $url = $metadata.SelectSingleNode('//*[local-name()="metadata"]/*[local-name()="licenseUrl"]')
    $files = @($entry.Value.files | Where-Object { $_ -match '(?i)(^|/)(license|notice|third.party)' })
    if ($license -and $license.type -eq 'file') { $files += $license.InnerText }
    # Some NuGet asset inventories omit license files; include actual package-root notices too.
    $files += @(Get-ChildItem -LiteralPath $packageDir -File | Where-Object { $_.Name -match '^(?i:license|notice|third.party)' } | ForEach-Object Name)
    $copied = @()
    foreach ($relative in ($files | Sort-Object -Unique)) {
        $source = Join-Path $packageDir $relative
        if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
            if ($license -and $license.type -eq 'file' -and $relative -eq $license.InnerText) { throw "Declared license missing for $($entry.Name)" }
            continue
        }
        $body = Get-Content -LiteralPath $source -Raw
        if ($body -match 'MICROSOFT WINDOWS APP SDK ENGINEERING PREVIEW') { throw "Nonredistributable Engineering Preview license: $($entry.Name)" }
        $safe = ($entry.Name + '-' + $relative) -replace '[\\/:]', '-'
        Copy-Item -LiteralPath $source -Destination (Join-Path $destination $safe)
        $copied += [ordered]@{ File=$safe; Sha256=(Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant() }
    }
    $inventory += [ordered]@{ Package=$entry.Name; License=$(if ($license) { $license.InnerText } else { $null }); LicenseType=$(if ($license) { $license.type } else { $null }); LicenseUrl=$(if ($url) { $url.InnerText } else { $null }); Notices=$copied }
}
# Native AOT includes .NET runtime code. Match the restored compiler/runtime version,
# never select an unrelated "latest installed" runtime license.
$compiler = $assets.libraries.PSObject.Properties.Name | Where-Object { $_ -match '^Microsoft.DotNet.ILCompiler/' } | Select-Object -First 1
$runtimeVersion = ($compiler -split '/')[1]
foreach ($folder in $assets.packageFolders.PSObject.Properties.Name) {
    $runtimeDir = Join-Path $folder "microsoft.netcore.app.runtime.win-x64/$runtimeVersion"
    if (Test-Path -LiteralPath $runtimeDir) {
        foreach ($name in @('LICENSE.TXT','THIRD-PARTY-NOTICES.TXT')) {
            $source = Join-Path $runtimeDir $name
            if (-not (Test-Path -LiteralPath $source)) { throw "Missing .NET runtime notice: $name" }
            Copy-Item -LiteralPath $source -Destination (Join-Path $destination "dotnet-$runtimeVersion-$name")
        }
        break
    }
}
if (-not (Test-Path -LiteralPath (Join-Path $destination "dotnet-$runtimeVersion-LICENSE.TXT"))) { throw 'Matching .NET runtime license not found.' }
$inventory | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $destination 'nuget-license-inventory.json') -Encoding utf8
