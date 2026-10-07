param([string]$Tag = '')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$project = [xml](Get-Content (Join-Path $root 'src/PotPlayerNext/PotPlayerNext.csproj') -Raw)
$version = [string]($project.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1)
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Project Version must be three numeric fields.' }
if ($Tag -and $Tag -cne "v$version") { throw "Release tag '$Tag' does not match project version v$version. Update the csproj first." }
Write-Output $version
