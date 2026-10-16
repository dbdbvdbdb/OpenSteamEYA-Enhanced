param([switch]$NativeAot)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$testOutput = Join-Path $projectRoot 'artifacts/ui-check'
if ($NativeAot) { $testOutput = Join-Path $projectRoot 'artifacts/ui-check-aot' }
$arguments = @('publish',(Join-Path $projectRoot 'SteamEyaWinUI/SteamEyaWinUI.csproj'),'-c','Release','-r','win-x64','-p:Platform=x64','-p:EnableManagerUiChecks=true','-o',$testOutput,"-p:PublishAot=$($NativeAot.IsPresent.ToString().ToLowerInvariant())")
if (-not $NativeAot) { $arguments += '-p:PublishTrimmed=false' }
else { $arguments += '-p:RestoreLockedMode=true' }
& dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw 'QA build failed.' }
if ($NativeAot) {
    & (Join-Path $PSScriptRoot 'assert-native-aot.ps1') -PublishDirectory $testOutput
    # Match the production directory's removal of unused WebView2 files.
    $webViewUsage = Get-ChildItem -LiteralPath (Join-Path $projectRoot 'SteamEyaWinUI') -Recurse -File |
        Where-Object { $_.Extension -in @('.cs','.xaml') -and $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } |
        Select-String -Pattern '\bWebView2?\b|Microsoft\.Web\.WebView2' -Quiet
    if (-not $webViewUsage) {
        foreach ($pattern in @('Microsoft.Web.WebView2.Core*.dll','WebView2Loader.dll')) {
            foreach ($unused in (Get-ChildItem -LiteralPath $testOutput -File -Filter $pattern)) { Remove-Item -LiteralPath $unused.FullName }
        }
    }
}
$process = Start-Process -FilePath (Join-Path $testOutput 'SteamEyaWinUI.exe') -ArgumentList '--manager-ui-check' -WindowStyle Hidden -PassThru
if (-not $process.WaitForExit(120000)) { throw 'QA did not finish within 120 seconds. Inspect the test process before retrying.' }
$runDirectory = Get-Content -LiteralPath (Join-Path $testOutput 'ui-check-results/latest-run.txt')
$failureFile = Join-Path $runDirectory 'failure.txt'
if (Test-Path -LiteralPath $failureFile) {
    $partialResultsFile = Join-Path $runDirectory 'partial-results.txt'
    if (Test-Path -LiteralPath $partialResultsFile) { Get-Content -LiteralPath $partialResultsFile }
    Get-Content -LiteralPath $failureFile
    throw 'QA checks failed.'
}
$results = Get-Content -LiteralPath (Join-Path $runDirectory 'results.txt')
$results
Write-Output "Passed: $(($results | Where-Object { $_ -like 'PASS *' }).Count)"
Write-Output "Rendered screenshots: $runDirectory"
