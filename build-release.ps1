<#
  Builds a same-origin production release of Jenus People into .\publish\jpeople

  Output layout:
    publish\jpeople\        <- copy this whole folder to the IIS site root
      API.dll, *.dll, web.config, appsettings*.json
      wwwroot\              <- the React SPA (served by the API at /)

  Usage (from the solution root):
    powershell -ExecutionPolicy Bypass -File .\build-release.ps1
#>
$ErrorActionPreference = 'Stop'
$root      = $PSScriptRoot
$client    = Join-Path $root 'client'
$dist      = Join-Path $client 'dist'
$wwwroot   = Join-Path $root 'API\wwwroot'
$publishTo = Join-Path $root 'publish\jpeople'

# $ErrorActionPreference does NOT apply to native commands: npm and dotnet report
# failure through an exit code, which PowerShell will happily ignore. Vite in
# particular can die on a V8 allocation fault part-way through -- leaving the
# PREVIOUS dist\ in place, which then gets staged and published as if it were
# this build's output. That shipped a stale SPA against a fresh API once; check
# every native exit code so it cannot happen silently again.
function Assert-NativeSuccess([string]$what) {
    if ($LASTEXITCODE -ne 0) {
        throw "$what failed with exit code $LASTEXITCODE. Release aborted; publish\jpeople was NOT updated."
    }
}

Write-Host '==> Building React SPA (Vite)...' -ForegroundColor Cyan
# Recorded before the build so the freshness check below cannot be satisfied by
# leftovers from an earlier run.
$buildStarted = Get-Date
Push-Location $client
try {
    $env:NODE_OPTIONS = '--max-old-space-size=4096'   # the bundle needs a larger heap
    npm run build
    Assert-NativeSuccess 'npm run build'
}
finally {
    Pop-Location
}

# A zero exit code is necessary but not sufficient: assert the bundle on disk is
# the one we just built.
$indexHtml = Join-Path $dist 'index.html'
if (-not (Test-Path $indexHtml)) {
    throw "Vite reported success but $indexHtml does not exist. Release aborted."
}
$indexWritten = (Get-Item $indexHtml).LastWriteTime
if ($indexWritten -lt $buildStarted) {
    throw "$indexHtml was last written $indexWritten, before this build started $buildStarted -- dist\ is stale. Release aborted."
}

Write-Host '==> Staging SPA into API\wwwroot...' -ForegroundColor Cyan
if (Test-Path $wwwroot) { Remove-Item $wwwroot -Recurse -Force }
New-Item -ItemType Directory -Path $wwwroot | Out-Null
Copy-Item (Join-Path $dist '*') $wwwroot -Recurse

Write-Host '==> Publishing API (Release)...' -ForegroundColor Cyan
if (Test-Path $publishTo) { Remove-Item $publishTo -Recurse -Force }
dotnet publish (Join-Path $root 'API\API.csproj') -c Release -o $publishTo
Assert-NativeSuccess 'dotnet publish'

# The SPA is the whole point of the same-origin layout; publishing without it
# yields a site that 404s at /.
$publishedIndex = Join-Path $publishTo 'wwwroot\index.html'
if (-not (Test-Path $publishedIndex)) {
    throw "Publish completed but $publishedIndex is missing. Release aborted."
}

Write-Host "==> Done. Release is in $publishTo" -ForegroundColor Green
Write-Host '    Remember: appsettings.Production.json carries the prod DB password;' -ForegroundColor Yellow
Write-Host '    it is git-ignored on purpose. Deploy it with the release.'        -ForegroundColor Yellow
