param([string]$Version = '', [string]$PublishDirectory = 'artifacts/release', [switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
if (!$Version) { $Version = & "$PSScriptRoot/get-version.ps1" }
$root = Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $root
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Version must have three numeric fields.' }
if (!$SkipBuild) { & "$PSScriptRoot/build.ps1" -Publish -OutputDirectory $PublishDirectory -Version $Version }
$publish = [IO.Path]::GetFullPath((Join-Path $root $PublishDirectory))
foreach ($name in @('PotPlayerNext.exe','PotPlayerNext.pri','App.xbf','MainWindow.xbf','PreviewWindow.xbf','SplashWindow.xbf','Controls/BrandLogo.xbf','potplayer_next_core.dll')) {
    if (!(Test-Path -LiteralPath (Join-Path $publish $name))) { throw "Missing publish resource: $name" }
}
$dotnet = Join-Path $root '.tools/dotnet/dotnet.exe'
if (!(Test-Path $dotnet)) { $dotnet = (Get-Command dotnet -ErrorAction Stop).Source }
$env:DOTNET_ROOT = Split-Path $dotnet -Parent
$wix = Join-Path $root '.tools/wix/wix.exe'
if (!(Test-Path $wix)) {
    & $dotnet tool install wix --tool-path (Join-Path $root '.tools/wix') --version 6.0.2
    if ($LASTEXITCODE) { throw 'WiX install failed.' }
}
& $wix extension add WixToolset.UI.wixext/6.0.2
if ($LASTEXITCODE) { throw 'WiX UI extension restore failed.' }
$output = Join-Path $root 'artifacts/installer'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$ns = 'http://wixtoolset.org/schemas/v4/wxs'
$doc = [xml]"<Wix xmlns='$ns'><Fragment><DirectoryRef Id='INSTALLFOLDER'/><ComponentGroup Id='AppFiles'/><ComponentGroup Id='Associations'/></Fragment></Wix>"
function Element($parent, $name, $attributes) {
    $element = $doc.CreateElement($name, $ns)
    foreach ($entry in $attributes.GetEnumerator()) { $element.SetAttribute($entry.Key, [string]$entry.Value) }
    [void]$parent.AppendChild($element)
    return $element
}
function StableId([string]$value) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    $bytes = $sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($value.ToLowerInvariant()))
    return 'F' + [System.BitConverter]::ToString($bytes).Replace('-', '').Substring(0, 32)
}
function StableGuid([string]$value) { return ([guid]::ParseExact((StableId $value).Substring(1), 'N')).ToString('D') }
$directoryRef = $doc.Wix.Fragment.DirectoryRef
$filesGroup = $doc.Wix.Fragment.ComponentGroup | Where-Object Id -eq 'AppFiles'
$associationsGroup = $doc.Wix.Fragment.ComponentGroup | Where-Object Id -eq 'Associations'
$directories = @{ '' = $directoryRef }
function GetDirectory([string]$relative) {
    if ($directories.ContainsKey($relative)) { return $directories[$relative] }
    $parentPath = [IO.Path]::GetDirectoryName($relative)
    if ($null -eq $parentPath) { $parentPath = '' }
    $parent = GetDirectory $parentPath
    $id = StableId "directory/$relative"
    $directory = Element $parent 'Directory' @{ Id=$id; Name=[IO.Path]::GetFileName($relative) }
    $directories[$relative] = $directory
    return $directory
}
$payload = @(Get-ChildItem -LiteralPath $publish -File -Recurse | Where-Object { $_.Extension -notin @('.pdb','.manifest') } | Sort-Object FullName)
foreach ($file in $payload) {
    $relative = $file.FullName.Substring($publish.Length).TrimStart('\', '/')
    $parent = GetDirectory ([IO.Path]::GetDirectoryName($relative))
    $id = StableId $relative
    $component = Element $parent 'Component' @{ Id=$id; Guid=(StableGuid $relative) }
    $fileId = if ($relative -eq 'PotPlayerNext.exe') { 'MainExe' } else { "${id}File" }
    [void](Element $component 'File' @{Id=$fileId; Source=$file.FullName; KeyPath='no'})
    [void](Element $component 'RegistryValue' @{ Root='HKCU'; Key='Software\PotPlayerNext\Installer\Files'; Name=$id; Type='string'; Value=$Version; KeyPath='yes' })
    [void](Element $filesGroup 'ComponentRef' @{Id=$id})
}
foreach ($entry in $directories.GetEnumerator()) {
    $id = StableId "cleanup/$($entry.Key)"
    $component = Element $entry.Value 'Component' @{Id=$id; Guid='*'}
    [void](Element $component 'RemoveFolder' @{Id="${id}Dir"; On='uninstall'})
    [void](Element $component 'RegistryValue' @{Root='HKCU'; Key='Software\PotPlayerNext\Installer\Folders'; Name=$id; Type='string'; Value=$Version; KeyPath='yes'})
    [void](Element $filesGroup 'ComponentRef' @{Id=$id})
}
$associationComponent = Element $directoryRef 'Component' @{Id='FileAssociations'; Guid='*'}
[void](Element $associationComponent 'RegistryValue' @{Root='HKCU'; Key='Software\PotPlayerNext\Installer'; Name='Associations'; Type='integer'; Value='1'; KeyPath='yes'})
[void](Element $associationsGroup 'ComponentRef' @{Id='FileAssociations'})
function RegistryValue([string]$key, [string]$name, [string]$value) {
    $attributes = @{Root='HKCU'; Key=$key; Type='string'; Value=$value}
    if ($name) { $attributes.Name = $name }
    [void](Element $associationComponent 'RegistryValue' $attributes)
}
$command = '"[#MainExe]" -- "%1"'
$capabilities = 'Software\PotPlayerNext\Capabilities'
RegistryValue 'Software\RegisteredApplications' 'PotPlayerNext' $capabilities
RegistryValue $capabilities 'ApplicationName' 'PotPlayerNext'
RegistryValue $capabilities 'ApplicationDescription' 'WinUI 3 图片查看器与视频播放器；空格预览、缩略图与临时倍速。'
RegistryValue $capabilities 'ApplicationIcon' '"[#MainExe]",0'
RegistryValue 'Software\Classes\Applications\PotPlayerNext.exe' 'FriendlyAppName' 'PotPlayerNext'
RegistryValue 'Software\Classes\Applications\PotPlayerNext.exe\shell\open\command' '' $command
$types = & "$PSScriptRoot/get-media-types.ps1"
foreach ($kind in @('image','video')) {
    $progId = if ($kind -eq 'image') { 'PotPlayerNext.Image' } else { 'PotPlayerNext.Video' }
    RegistryValue "Software\Classes\$progId" '' "PotPlayerNext $kind"
    RegistryValue "Software\Classes\$progId\DefaultIcon" '' '"[#MainExe]",0'
    RegistryValue "Software\Classes\$progId\shell\open\command" '' $command
    foreach ($extension in $types.$kind) {
        RegistryValue "$capabilities\FileAssociations" $extension $progId
        RegistryValue "Software\Classes\$extension\OpenWithProgids" $progId ''
        RegistryValue 'Software\Classes\Applications\PotPlayerNext.exe\SupportedTypes' $extension ''
    }
    [void](Element $associationComponent 'RemoveRegistryKey' @{Root='HKCU'; Key="Software\Classes\$progId"; Action='removeOnUninstall'})
}
foreach ($key in @($capabilities, 'Software\Classes\Applications\PotPlayerNext.exe')) {
    [void](Element $associationComponent 'RemoveRegistryKey' @{Root='HKCU'; Key=$key; Action='removeOnUninstall'})
}
$generated = Join-Path $output 'Payload.wxs'; $doc.Save($generated)
$licenseText = (Get-Content (Join-Path $root 'LICENSE') -Raw)
$bs = [string][char]92; $lb = [string][char]123; $rb = [string][char]125
$licenseText = $licenseText.Replace($bs, $bs + $bs).Replace($lb, $bs + $lb).Replace($rb, $bs + $rb).Replace("`r", "").Replace("`n", $bs + "par ")
$licenseRtf = Join-Path $output 'License.rtf'
$rtfHeader = $lb + $bs + "rtf1" + $bs + "ansi" + $bs + "deff0" + $lb + $bs + "fonttbl" + $lb + $bs + "f0 Segoe UI;" + $rb + $rb + $bs + "f0" + $bs + "fs18 "
[IO.File]::WriteAllText($licenseRtf, $rtfHeader + $licenseText + $rb, [Text.Encoding]::ASCII)
$msi = Join-Path $output "PotPlayerNext-$Version-win-x64.msi"
$dialogBmp = Join-Path $output 'dialog.bmp'
$bannerBmp = Join-Path $output 'banner.bmp'
$packageWxs = Join-Path $root 'installer/Package.wxs'
& "$PSScriptRoot/build-installer-art.ps1"
& $wix build $packageWxs $generated -arch x64 -ext WixToolset.UI.wixext -culture zh-CN -d "Version=$Version" -d "LicenseRtf=$licenseRtf" -d "DialogBmp=$dialogBmp" -d "BannerBmp=$bannerBmp" -o $msi
if ($LASTEXITCODE) { throw 'MSI build failed.' }
$hash = (Get-FileHash -LiteralPath $msi -Algorithm SHA256).Hash
[IO.File]::WriteAllText("$msi.sha256", "$hash  $([IO.Path]::GetFileName($msi))`n")
Write-Host "PASS: MSI $msi ($($payload.Count) payload files), SHA256 $hash"
& "$PSScriptRoot/build-source.ps1" -Version $Version
