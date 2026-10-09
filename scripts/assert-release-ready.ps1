$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$review = Get-Content -LiteralPath (Join-Path $projectRoot 'docs/release-readiness.json') -Raw | ConvertFrom-Json
$unresolved = @($review.items | Where-Object { $_.status -ne 'resolved' })
if ($review.approved -ne $true -or $unresolved.Count) {
    foreach ($item in $unresolved) { Write-Output ("BLOCKED: $($item.id) — $($item.reason)") }
    throw 'Public release is blocked. Resolve documented rights/testing requirements and review readiness before publishing.'
}
Write-Output 'Documented public-release readiness approved.'
