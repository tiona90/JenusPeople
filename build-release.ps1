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
$wwwroot   = Join-Path $root 'API\wwwroot'
$publishTo = Join-Path $root 'publish\jpeople'

Write-Host '==> Building React SPA (Vite)...' -ForegroundColor Cyan
Push-Location $client
$env:NODE_OPTIONS = '--max-old-space-size=4096'   # the bundle needs a larger heap
npm run build
Pop-Location

Write-Host '==> Staging SPA into API\wwwroot...' -ForegroundColor Cyan
if (Test-Path $wwwroot) { Remove-Item $wwwroot -Recurse -Force }
New-Item -ItemType Directory -Path $wwwroot | Out-Null
Copy-Item (Join-Path $client 'dist\*') $wwwroot -Recurse

Write-Host '==> Publishing API (Release)...' -ForegroundColor Cyan
if (Test-Path $publishTo) { Remove-Item $publishTo -Recurse -Force }
dotnet publish (Join-Path $root 'API\API.csproj') -c Release -o $publishTo

Write-Host "==> Done. Release is in $publishTo" -ForegroundColor Green
Write-Host '    Remember: appsettings.Production.json carries the prod DB password;' -ForegroundColor Yellow
Write-Host '    it is git-ignored on purpose. Deploy it with the release.'        -ForegroundColor Yellow
