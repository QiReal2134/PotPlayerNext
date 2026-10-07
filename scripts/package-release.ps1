param([string]$Version = '', [string]$PublishDirectory = 'artifacts/app')
$ErrorActionPreference = 'Stop'
if (!$Version) { $Version = & "$PSScriptRoot/get-version.ps1" }
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid release version.' }
$root = Split-Path $PSScriptRoot -Parent
$publish = [IO.Path]::GetFullPath((Join-Path $root $PublishDirectory))
if (!$publish.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Publish directory must be within the workspace.' }
$output = Join-Path $root 'artifacts/installer'
$portable = Join-Path $output "PotPlayerNext-$Version-win-x64-portable.zip"
foreach ($name in @('PotPlayerNext.exe','PotPlayerNext.dll','PotPlayerNext.pri','SplashWindow.xbf','Controls/BrandLogo.xbf')) {
    if (!(Test-Path -LiteralPath (Join-Path $publish $name))) { throw "Missing portable payload: $name" }
}
if (Test-Path -LiteralPath $portable) { Remove-Item -LiteralPath $portable }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($publish, $portable, [IO.Compression.CompressionLevel]::Optimal, $false)
$assets = @("PotPlayerNext-$Version-win-x64.msi", "PotPlayerNext-$Version-win-x64-portable.zip", "PotPlayerNext-$Version-source.zip")
$lines = @(); $manifest = @()
foreach ($name in $assets) {
    $file = Join-Path $output $name
    $hash = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()
    $line = "$hash  $name"
    [IO.File]::WriteAllText("$file.sha256", "$line`n", [Text.UTF8Encoding]::new($false))
    $lines += $line
    $manifest += @{ name=$name; sha256=$hash; bytes=(Get-Item -LiteralPath $file).Length }
}
[IO.File]::WriteAllText((Join-Path $output 'SHA256SUMS.txt'), ($lines -join "`n") + "`n", [Text.UTF8Encoding]::new($false))
@{ version=$Version; commit=$env:GITHUB_SHA; assets=$manifest } | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $output 'release-manifest.json') -Encoding utf8
Write-Output "PASS: MSI / portable / GPL source release bundle v$Version."
