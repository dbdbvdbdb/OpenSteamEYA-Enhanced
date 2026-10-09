param([string]$Version = '0.2.4', [string]$Suffix = '')
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Version must be X.Y.Z' }
if ($Suffix -notmatch '^[a-z0-9-]*$') { throw 'Suffix must contain lowercase letters, digits or hyphens.' }
$projectRoot = Split-Path -Parent $PSScriptRoot
$artifactDir = Join-Path $projectRoot 'artifacts'
New-Item -ItemType Directory -Force -Path $artifactDir | Out-Null
$sourceName = "OpenSteamEYA-Enhanced-$Version-source"
if ($Suffix) { $sourceName += "-$Suffix" }
$zipPath = Join-Path $artifactDir "$sourceName.zip"
$sourceFiles = & git -C $projectRoot -c core.quotepath=false ls-files --cached --others --exclude-standard
if ($LASTEXITCODE -ne 0) { throw 'Unable to enumerate project source.' }
Add-Type -AssemblyName System.IO.Compression
$zipStream = [System.IO.File]::Open($zipPath, [System.IO.FileMode]::Create)
$archive = [System.IO.Compression.ZipArchive]::new($zipStream, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($relativePath in ($sourceFiles | Sort-Object -Unique)) {
        if ($relativePath -match '(?i)(^|[\\/])(bin|obj|artifacts|synthetic-data|ui-check-results|userdata|backups|exports)([\\/]|$)|\.(pdb|db|sqlite3?|pfx|p12|pem|key|zip|7z)$|(^|[\\/])(manager\.json|history\.json|loginusers\.vdf|\.env(?:\..*)?)$') { throw "Unsafe source package candidate: $relativePath" }
        $sourcePath = [System.IO.Path]::GetFullPath((Join-Path $projectRoot $relativePath))
        if (-not $sourcePath.StartsWith($projectRoot + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) { throw 'Source outside project root.' }
        if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) { continue }
        $entryName = 'OpenSteamEYA-Enhanced/' + $relativePath.Replace('\', '/')
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $sourcePath, $entryName, [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally { $archive.Dispose(); $zipStream.Dispose() }
Get-FileHash -LiteralPath $zipPath -Algorithm SHA256 | Format-List Path,Hash
