$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $root
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if ($dotnet) { $dotnetPath = $dotnet.Source } else { $dotnetPath = Join-Path $root '.tools\dotnet\dotnet.exe' }
if (!(Test-Path $dotnetPath)) { throw 'Install .NET 8 SDK.' }
& $dotnetPath run --project tests/MediaSmoke/MediaSmoke.csproj -c Release
if ($LASTEXITCODE) { throw 'OS media smoke tests failed. See artifacts/media-smoke/results.json.' }
