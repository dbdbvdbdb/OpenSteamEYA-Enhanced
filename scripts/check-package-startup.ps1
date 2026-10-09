param([Parameter(Mandatory=$true)][string]$ZipPath)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$unpack = Join-Path $projectRoot ('artifacts/startup-check-' + [guid]::NewGuid().ToString('N'))
Expand-Archive -LiteralPath $ZipPath -DestinationPath $unpack
& (Join-Path $PSScriptRoot 'assert-native-aot.ps1') -PublishDirectory $unpack
& (Join-Path $PSScriptRoot 'assert-portable-package.ps1') -PublishDirectory $unpack
# Smoke test only: no input, screenshot, credentials, login or CS2 network action.
# The production application uses the current Windows user's usual data paths.
$application = Start-Process -FilePath (Join-Path $unpack 'SteamEyaWinUI.exe') -WorkingDirectory $unpack -WindowStyle Hidden -PassThru
$deadline = [DateTime]::UtcNow.AddSeconds(20)
do {
    Start-Sleep -Milliseconds 250
    $application.Refresh()
    if ($application.HasExited) { throw 'Application exited before its native window appeared.' }
} while ($application.MainWindowHandle -eq [IntPtr]::Zero -and [DateTime]::UtcNow -lt $deadline)
if ($application.MainWindowHandle -eq [IntPtr]::Zero -or -not $application.Responding) { throw 'Native window did not start/respond.' }
Write-Output 'PASS extracted production package starts with a responsive native window.'
if (-not $application.CloseMainWindow() -or -not $application.WaitForExit(15000)) { throw 'Own startup test application did not close gracefully.' }
Write-Output 'PASS own test application closed gracefully; no login/query action invoked.'
