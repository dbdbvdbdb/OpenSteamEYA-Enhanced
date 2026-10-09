param([switch]$InstallMissing)
$ErrorActionPreference = 'Stop'
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
function Get-CppPath {
    if (Test-Path -LiteralPath $vswhere) {
        & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    }
}
function Test-Sdk {
    $sdk = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10'
    (Test-Path -LiteralPath (Join-Path $sdk 'Include/10.0.26100.0/um/Windows.h')) -and
    (Test-Path -LiteralPath (Join-Path $sdk 'Lib/10.0.26100.0/um/x64/kernel32.lib'))
}
$cpp = Get-CppPath
if ((-not $cpp -or -not (Test-Sdk)) -and $InstallMissing) {
    # Official Microsoft installer only; verify its signature before executing.
    $installer = Join-Path $env:TEMP ('eya-buildtools-' + [guid]::NewGuid().ToString('N') + '.exe')
    Invoke-WebRequest 'https://aka.ms/vs/17/release/vs_BuildTools.exe' -OutFile $installer
    $signature = Get-AuthenticodeSignature -LiteralPath $installer
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'Microsoft Corporation') { throw 'Untrusted Build Tools installer.' }
    $arguments = @('--quiet','--wait','--norestart','--nocache','--add','Microsoft.VisualStudio.Workload.VCTools',
        '--add','Microsoft.VisualStudio.Component.VC.Tools.x86.x64','--add','Microsoft.VisualStudio.Component.Windows11SDK.26100','--includeRecommended')
    $process = Start-Process -FilePath $installer -ArgumentList $arguments -WindowStyle Hidden -PassThru -Wait
    if ($process.ExitCode -notin @(0,3010)) { throw "Build Tools installation failed: $($process.ExitCode)" }
    if ($process.ExitCode -eq 3010) { throw 'Build Tools requires a reboot; no automatic reboot or managed fallback.' }
    $cpp = Get-CppPath
}
if (-not $cpp) { throw 'MSVC x64 linker missing. Install Visual Studio C++ Build Tools or run with -InstallMissing.' }
if (-not (Test-Sdk)) { throw 'Windows SDK 10.0.26100.0 headers/libraries missing. No managed fallback.' }
$links = @(& $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -find 'VC/Tools/MSVC/**/bin/Hostx64/x64/link.exe')
if (-not $links.Count) { throw 'MSVC x64 link.exe not found.' }
Write-Output 'MSVC x64 linker and Windows SDK 10.0.26100.0 verified.'
