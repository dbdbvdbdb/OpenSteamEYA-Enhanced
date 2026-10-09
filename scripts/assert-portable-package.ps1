param([Parameter(Mandatory=$true)][string]$PublishDirectory, [string]$ZipPath)
$ErrorActionPreference = 'Stop'
$root = [System.IO.Path]::GetFullPath($PublishDirectory).TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
$files = @(Get-ChildItem -LiteralPath $PublishDirectory -Recurse -File)
foreach ($file in $files) {
    $relative = [System.IO.Path]::GetRelativePath($root, $file.FullName).Replace('\','/')
    if ($relative -match '(?i)(^|/)(tests|synthetic-data|ui-check-results|bin|obj|userdata|backups|exports)(/|$)|\.(pdb|obj|tmp|bak|log|db|sqlite3?|pfx|p12|pem|key|dmp)$|(^|/)(manager\.json|history\.json|loginusers\.vdf|ssfn\d+)$') { throw "Forbidden runtime package file: $relative" }
    # WinUI's NoiseAsset is an SDK runtime texture, not a development screenshot.
    if ($file.Extension -in @('.png','.jpg','.jpeg') -and $relative -notmatch '^(Assets/|Microsoft.UI.Xaml/Assets/)') { throw "Unexpected runtime screenshot: $relative" }
}
foreach ($required in @('LICENSE','THIRD_PARTY_NOTICES.md','END_USER_TERMS.txt','third-party-licenses/nuget-license-inventory.json')) {
    if (-not (Test-Path -LiteralPath (Join-Path $root $required))) { throw "Missing required license notice: $required" }
}
$exe = Join-Path $root 'SteamEyaWinUI.exe'
if (Test-Path -LiteralPath $exe) {
    $bytes = [System.IO.File]::ReadAllBytes($exe)
    $utf8 = [System.Text.Encoding]::UTF8.GetString($bytes)
    $utf16 = [System.Text.Encoding]::Unicode.GetString($bytes)
    foreach ($marker in @('--manager-ui-check','qa-account-','synthetic-data','cooldown-groups-dark')) {
        if ($utf8.Contains($marker) -or $utf16.Contains($marker)) { throw 'QA marker remains in production executable.' }
    }
}
if ($ZipPath) {
    Add-Type -AssemblyName System.IO.Compression
    $zip = [System.IO.Compression.ZipFile]::OpenRead($ZipPath)
    try {
        $entries = @($zip.Entries | Where-Object { $_.Name })
        if ($entries.Count -ne $files.Count) { throw 'ZIP file count differs from publish directory.' }
        $seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
        foreach ($entry in $entries) {
            if (-not $seen.Add($entry.FullName)) { throw 'Duplicate ZIP entry.' }
            $target = [System.IO.Path]::GetFullPath((Join-Path $root $entry.FullName))
            if (-not $target.StartsWith($root,[System.StringComparison]::OrdinalIgnoreCase) -or -not (Test-Path -LiteralPath $target -PathType Leaf)) { throw 'ZIP entry outside publish directory or unknown.' }
            $stream = $entry.Open()
            $sha = [System.Security.Cryptography.SHA256]::Create()
            try { $hash = [System.Convert]::ToHexString($sha.ComputeHash($stream)) }
            finally { $sha.Dispose(); $stream.Dispose() }
            if ($hash -ne (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash) { throw "ZIP content mismatch: $($entry.FullName)" }
        }
    } finally { $zip.Dispose() }
}
Write-Output "Portable package checked: $($files.Count) files; no test data/debug outputs; required notices present."
