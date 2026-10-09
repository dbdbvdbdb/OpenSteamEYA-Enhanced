param([Parameter(Mandatory)][string]$PublishDirectory)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Reflection.Metadata
$exePath = Join-Path $PublishDirectory 'SteamEyaWinUI.exe'
if (-not (Test-Path -LiteralPath $exePath)) { throw 'Native executable missing.' }
$stream = [System.IO.File]::OpenRead($exePath)
$reader = [System.Reflection.PortableExecutable.PEReader]::new($stream)
try {
    if ($reader.PEHeaders.CorHeader) { throw 'Executable contains a managed CLI header.' }
    if ($reader.PEHeaders.CoffHeader.Machine -ne [System.Reflection.PortableExecutable.Machine]::Amd64) { throw 'Executable is not Windows x64.' }
} finally { $reader.Dispose(); $stream.Dispose() }
if (-not [System.Text.Encoding]::ASCII.GetString([System.IO.File]::ReadAllBytes($exePath)).Contains('DotNetRuntimeDebugHeader')) {
    throw 'Native AOT runtime debug header marker is missing.'
}
foreach ($managedFile in @('SteamEyaWinUI.dll','AccountManagement.dll','coreclr.dll','clrjit.dll','hostfxr.dll','hostpolicy.dll','System.Private.CoreLib.dll','SteamEyaWinUI.runtimeconfig.json','SteamEyaWinUI.deps.json')) {
    if (Test-Path -LiteralPath (Join-Path $PublishDirectory $managedFile)) { throw "Managed publish contamination: $managedFile" }
}
foreach ($requiredFile in @('SteamEyaWinUI.pri','Microsoft.ui.xaml.dll','steam_api64.dll','Assets/AppIcon.ico','Languages/zh-Hans.json','Languages/en.json','Languages/zh-Hant.json')) {
    if (-not (Test-Path -LiteralPath (Join-Path $PublishDirectory $requiredFile))) { throw "Required runtime/resource missing: $requiredFile" }
}
Write-Output 'PASS Native AOT: x64 native PE, no managed app/runtime host, required WinUI/Steam resources present.'
