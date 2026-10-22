param([switch]$NativeAot = $true, [string]$Version = '0.3.0', [string]$ReleaseLabel = '')
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Version must be X.Y.Z' }
if (-not $ReleaseLabel) { $ReleaseLabel = $Version }
if ($ReleaseLabel -notmatch ('^' + [regex]::Escape($Version) + '(?:-[a-z0-9]+(?:[.-][a-z0-9]+)*)?$')) { throw 'ReleaseLabel must match Version with an optional prerelease suffix.' }
$projectRoot = Split-Path -Parent $PSScriptRoot
$artifactRoot = Join-Path $projectRoot 'artifacts'
$mode = if ($NativeAot) { 'aot' } else { 'managed' }
$releaseDirectory = Join-Path $artifactRoot 'release-win-x64'
if ($NativeAot) { $releaseDirectory += '-aot' }
# A fresh directory prevents a previous managed build or QA files contaminating Native AOT.
$stagingDirectory = Join-Path $artifactRoot ('.publish-' + $mode + '-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $stagingDirectory | Out-Null
$publishArguments = @('publish', (Join-Path $projectRoot 'SteamEyaWinUI/SteamEyaWinUI.csproj'), '-c', 'Release', '-r', 'win-x64', '-p:Platform=x64', '-p:EnableManagerUiChecks=false', '-o', $stagingDirectory,
    "-p:Version=$Version", "-p:FileVersion=$Version.0", "-p:AssemblyVersion=$Version.0", "-p:InformationalVersion=v$ReleaseLabel", "-p:PublishAot=$($NativeAot.IsPresent.ToString().ToLowerInvariant())")
if (-not $NativeAot) { $publishArguments += '-p:PublishTrimmed=false' }
else { $publishArguments += '-p:RestoreLockedMode=true' }
& dotnet @publishArguments
if ($LASTEXITCODE -ne 0) { throw "Publish failed; staging preserved for diagnosis: $stagingDirectory. No managed fallback." }
if ($NativeAot) { & (Join-Path $PSScriptRoot 'assert-native-aot.ps1') -PublishDirectory $stagingDirectory }

# Keep debugging symbols outside the runtime package, available for later crash analysis.
$symbolDirectory = Join-Path $artifactRoot ("symbols-$Version-win-x64-$mode")
New-Item -ItemType Directory -Force -Path $symbolDirectory | Out-Null
foreach ($symbol in (Get-ChildItem -LiteralPath $stagingDirectory -Recurse -File -Filter '*.pdb')) {
    $relative = [System.IO.Path]::GetRelativePath($stagingDirectory, $symbol.FullName)
    $destination = Join-Path $symbolDirectory $relative
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination) | Out-Null
    Move-Item -LiteralPath $symbol.FullName -Destination $destination -Force
}
# The application has no WebView/WebView2 control or API use. WinUI, SDK DLLs/PRI and all
# language/CS2 resources remain. Keep this audit guard if a future feature adds WebView2.
$webViewUsage = Get-ChildItem -LiteralPath (Join-Path $projectRoot 'SteamEyaWinUI') -Recurse -File |
    Where-Object { $_.Extension -in @('.cs','.xaml') -and $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } |
    Select-String -Pattern '\bWebView2?\b|Microsoft\.Web\.WebView2' -Quiet
if ($NativeAot -and -not $webViewUsage) {
    foreach ($pattern in @('Microsoft.Web.WebView2.Core*.dll','WebView2Loader.dll')) {
        foreach ($unused in (Get-ChildItem -LiteralPath $stagingDirectory -File -Filter $pattern)) { Remove-Item -LiteralPath $unused.FullName }
    }
}
Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination $stagingDirectory
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs/RUNNING.zh-CN.md') -Destination (Join-Path $stagingDirectory 'README-ENHANCED.zh-CN.md')
foreach ($notice in @('THIRD_PARTY_NOTICES.md','END_USER_TERMS.txt','SECURITY.md')) {
    Copy-Item -LiteralPath (Join-Path $projectRoot $notice) -Destination $stagingDirectory
}
$releaseDocs = Join-Path $stagingDirectory 'docs'
New-Item -ItemType Directory -Force -Path $releaseDocs | Out-Null
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs/FORMATS-AND-BACKUP.zh-CN.md') -Destination $releaseDocs
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs/CS2-LOCAL-SETTINGS.zh-CN.md') -Destination $releaseDocs
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs/CS2-UNIFIED-CONFIG.zh-CN.md') -Destination $releaseDocs
$licenses = Join-Path $stagingDirectory 'third-party-licenses'
New-Item -ItemType Directory -Force -Path $licenses | Out-Null
Copy-Item -LiteralPath (Join-Path $projectRoot 'third-party-licenses/counter-strike-icons-LICENSE.txt') -Destination $licenses
& (Join-Path $PSScriptRoot 'copy-package-licenses.ps1') -PublishDirectory $stagingDirectory
# Ship the updater beside the app so an installed copy can replace itself after exit.
$updaterOutput = Join-Path $artifactRoot ('.updater-' + $mode + '-' + [guid]::NewGuid().ToString('N'))
$updaterArguments = @('publish', (Join-Path $projectRoot 'OSEUpdater/OSEUpdater.csproj'), '-c', 'Release', '-r', 'win-x64', '-p:Platform=x64',
    "-p:Version=$Version", "-p:FileVersion=$Version.0", "-p:AssemblyVersion=$Version.0", "-p:PublishAot=$($NativeAot.IsPresent.ToString().ToLowerInvariant())",
    "-p:InformationalVersion=v$ReleaseLabel", '-o', $updaterOutput)
if (-not $NativeAot) { $updaterArguments += '-p:PublishTrimmed=false' }
& dotnet @updaterArguments
if ($LASTEXITCODE -ne 0) { throw 'Updater publish failed.' }
Copy-Item -LiteralPath (Join-Path $updaterOutput 'OSEUpdater.exe') -Destination $stagingDirectory
Remove-Item -LiteralPath $updaterOutput -Recurse -Force
& (Join-Path $PSScriptRoot 'assert-portable-package.ps1') -PublishDirectory $stagingDirectory
if (Test-Path -LiteralPath $releaseDirectory) {
    # Archive instead of recursive deletion. Both resolved paths must stay in artifacts.
    $resolvedRelease = [System.IO.Path]::GetFullPath($releaseDirectory)
    $resolvedArtifact = [System.IO.Path]::GetFullPath($artifactRoot) + [System.IO.Path]::DirectorySeparatorChar
    if (-not $resolvedRelease.StartsWith($resolvedArtifact,[System.StringComparison]::OrdinalIgnoreCase)) { throw 'Release outside artifacts.' }
    $previous = Join-Path $artifactRoot ('.previous-release-' + $mode + '-' + [guid]::NewGuid().ToString('N'))
    if (-not [System.IO.Path]::GetFullPath($previous).StartsWith($resolvedArtifact,[System.StringComparison]::OrdinalIgnoreCase)) { throw 'Archive outside artifacts.' }
    Move-Item -LiteralPath $releaseDirectory -Destination $previous
}
Move-Item -LiteralPath $stagingDirectory -Destination $releaseDirectory
$packageName = "OpenSteamEYA-Enhanced-$ReleaseLabel-win-x64"
if ($NativeAot) { $packageName += '-aot' }
$zipPath = Join-Path $artifactRoot "$packageName.zip"
$sevenZip = (Get-Command 7z -ErrorAction SilentlyContinue).Source
if (-not $sevenZip) {
    $candidate = Join-Path $env:ProgramFiles '7-Zip/7z.exe'
    if (Test-Path -LiteralPath $candidate) { $sevenZip = $candidate }
}
if ($sevenZip) {
    foreach ($extension in @('zip','7z')) {
        $packagePath = Join-Path $artifactRoot "$packageName.$extension"
        if (Test-Path -LiteralPath $packagePath) { Remove-Item -LiteralPath $packagePath }
        Push-Location $releaseDirectory
        try {
            if ($extension -eq 'zip') { & $sevenZip a -tzip -mm=Deflate -mx=9 -mfb=258 -mpass=15 $packagePath '.\*' | Out-Host }
            else { & $sevenZip a -t7z -mx=9 -m0=LZMA2 -md=128m -mfb=273 -ms=on -mmt=2 $packagePath '.\*' | Out-Host }
            if ($LASTEXITCODE -ne 0) { throw "7-Zip $extension compression failed." }
        } finally { Pop-Location }
        & $sevenZip t $packagePath | Out-Host
        if ($LASTEXITCODE -ne 0) { throw 'Archive integrity test failed.' }
    }
} else { Compress-Archive -Path (Join-Path $releaseDirectory '*') -DestinationPath $zipPath -CompressionLevel Optimal -Force }
$releaseFiles = @(Get-ChildItem -LiteralPath $releaseDirectory -Recurse -File)
$metrics = [ordered]@{ Mode=$mode; Version=$Version; ReleaseLabel=$ReleaseLabel; FileCount=$releaseFiles.Count; UncompressedBytes=($releaseFiles | Measure-Object Length -Sum).Sum; ZipBytes=(Get-Item -LiteralPath $zipPath).Length; ZipSha256=(Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash }
$sevenZipPath = Join-Path $artifactRoot "$packageName.7z"
if ($sevenZip -and (Test-Path -LiteralPath $sevenZipPath)) { $metrics.SevenZipBytes=(Get-Item -LiteralPath $sevenZipPath).Length; $metrics.SevenZipSha256=(Get-FileHash -LiteralPath $sevenZipPath -Algorithm SHA256).Hash }
$checksumLines = @((Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + (Split-Path -Leaf $zipPath))
if ($sevenZip -and (Test-Path -LiteralPath $sevenZipPath)) { $checksumLines += (Get-FileHash -LiteralPath $sevenZipPath -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + (Split-Path -Leaf $sevenZipPath) }
$checksumLines | Set-Content -LiteralPath (Join-Path $artifactRoot 'SHA256SUMS.txt') -Encoding ascii
# Update manifest consumed by the in-app updater (see docs/UPDATE-SYSTEM.md).
$zipHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
$zipSize = (Get-Item -LiteralPath $zipPath).Length
$zipName = Split-Path -Leaf $zipPath
$releaseTag = 'v' + $ReleaseLabel
$manifest = [ordered]@{
    schema = 1
    version = $ReleaseLabel
    tag = $releaseTag
    minVersion = '0.4.0'
    publishedAt = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    assetName = $zipName
    size = $zipSize
    sha256 = $zipHash
    url = 'https://github.com/dbdbvdbdb/OpenSteamEYA-Enhanced/releases/download/' + $releaseTag + '/' + $zipName
    notesUrl = 'https://github.com/dbdbvdbdb/OpenSteamEYA-Enhanced/releases/tag/' + $releaseTag
    mandatory = $false
}
$manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $artifactRoot 'update.json') -Encoding utf8
$manifest | ConvertTo-Json -Depth 4 | Write-Output
& (Join-Path $PSScriptRoot 'assert-portable-package.ps1') -PublishDirectory $releaseDirectory -ZipPath $zipPath
$metrics | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $artifactRoot "size-$mode.json")
$metrics | ConvertTo-Json | Write-Output
Write-Output "Executable: $(Join-Path $releaseDirectory 'SteamEyaWinUI.exe')"
