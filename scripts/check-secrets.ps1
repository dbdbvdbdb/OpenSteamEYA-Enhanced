$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$auditRoot = Join-Path $projectRoot 'artifacts/secret-check'
New-Item -ItemType Directory -Force -Path $auditRoot | Out-Null
$tool = Join-Path $auditRoot 'tool/gitleaks.exe'
if (-not (Test-Path -LiteralPath $tool)) {
    $zip = Join-Path $auditRoot 'gitleaks_8.30.1_windows_x64.zip'
    Invoke-WebRequest 'https://github.com/gitleaks/gitleaks/releases/download/v8.30.1/gitleaks_8.30.1_windows_x64.zip' -OutFile $zip
    # Official release checksum, independently verified during release preparation.
    if ((Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash -ne 'd29144deff3a68aa93ced33dddf84b7fdc26070add4aa0f4513094c8332afc4e') { throw 'Gitleaks checksum mismatch.' }
    Expand-Archive -LiteralPath $zip -DestinationPath (Join-Path $auditRoot 'tool') -Force
}
# All refs, including removed files. Report/console redaction is mandatory.
& $tool git --log-opts='--all --full-history' --redact=100 --no-banner --report-format json --report-path (Join-Path $auditRoot 'history.json') $projectRoot
if ($LASTEXITCODE -ne 0) { throw 'Git history secret scan failed or found candidate credentials. Stop public release; review redacted report.' }
# Scan precisely tracked + untracked public candidates; not ignored QA/build data or tool examples.
$stage = Join-Path $auditRoot ('source-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage | Out-Null
$sourceFiles = & git -C $projectRoot -c core.quotepath=false ls-files --cached --others --exclude-standard
if ($LASTEXITCODE -ne 0) { throw 'Source enumeration failed.' }
foreach ($relative in ($sourceFiles | Sort-Object -Unique)) {
    $source = [System.IO.Path]::GetFullPath((Join-Path $projectRoot $relative))
    $target = [System.IO.Path]::GetFullPath((Join-Path $stage $relative))
    if (-not $source.StartsWith($projectRoot + [System.IO.Path]::DirectorySeparatorChar,[System.StringComparison]::OrdinalIgnoreCase) -or
        -not $target.StartsWith($stage + [System.IO.Path]::DirectorySeparatorChar,[System.StringComparison]::OrdinalIgnoreCase)) { throw 'Source path outside audit stage.' }
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { continue }
    if ((Get-Item -LiteralPath $source).Length -gt 20MB) { throw 'Source candidate exceeds scan size limit; manual audit required.' }
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
    Copy-Item -LiteralPath $source -Destination $target
}
& $tool dir --redact=100 --no-banner --report-format json --report-path (Join-Path $auditRoot 'source.json') --max-target-megabytes 20 $stage
if ($LASTEXITCODE -ne 0) { throw 'Source secret scan failed or found candidate credentials. Stop public release; review redacted report.' }
Write-Output 'Gitleaks: all-ref history and public source candidate scans passed; reports contain redacted values only.'
