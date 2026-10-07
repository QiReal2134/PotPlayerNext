$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$assetsPath = Join-Path $root 'src/PotPlayerNext/obj/project.assets.json'
if (!(Test-Path $assetsPath)) { throw 'Restore the WinUI project before collecting package notices.' }
$assets = Get-Content $assetsPath -Raw | ConvertFrom-Json
$destination = Join-Path $root 'licenses'
New-Item -ItemType Directory -Path $destination -Force | Out-Null
$collected = 0
foreach ($library in $assets.libraries.PSObject.Properties) {
    if ($library.Value.type -ne 'package') { continue }
    foreach ($folder in $assets.packageFolders.PSObject.Properties.Name) {
        $path = Join-Path $folder $library.Name.ToLowerInvariant()
        if (!(Test-Path $path)) { continue }
        foreach ($file in Get-ChildItem -LiteralPath $path -File | Where-Object Name -Match '^(LICENSE|NOTICE|THIRD-PARTY-NOTICES)(\.|$)') {
            $name = $library.Name.Replace('/','-') + '-' + $file.Name
            Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $destination $name) -Force
            ++$collected
        }
    }
}
$cargo = (Get-Command cargo -ErrorAction SilentlyContinue).Source
if (!$cargo) { $cargo = Join-Path $env:USERPROFILE '.cargo/bin/cargo.exe' }
$metadata = & $cargo metadata --manifest-path (Join-Path $root 'Cargo.toml') --locked --format-version 1
if ($LASTEXITCODE) { throw 'Cargo metadata failed.' }
foreach ($package in ($metadata | ConvertFrom-Json).packages) {
    if (!$package.source) { continue }
    $path = Split-Path $package.manifest_path -Parent
    foreach ($file in Get-ChildItem -LiteralPath $path -File | Where-Object Name -Match '^(LICENSE|COPYING|NOTICE)([-.]|$)') {
        Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $destination "$($package.name)-$($package.version)-$($file.Name)") -Force
        ++$collected
    }
}
Write-Host "Collected $collected package license/notice files from restored, locked dependencies."
