param([Parameter(Mandatory)][string]$Version, [Parameter(Mandatory)][string]$Repository, [string]$AssetDirectory = 'release-assets')
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+$' -or $Repository -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') { throw 'Invalid release identity.' }
if (!(Get-Command gh -ErrorAction SilentlyContinue)) { throw 'GitHub CLI is required.' }
$tag = "v$Version"
$refJson = & gh api "repos/$Repository/git/ref/tags/$tag"
if ($LASTEXITCODE) { throw 'Release tag must already exist on GitHub.' }
$target = ($refJson | ConvertFrom-Json).object
for ($depth = 0; $target.type -eq 'tag' -and $depth -lt 4; $depth++) {
    $tagJson = & gh api "repos/$Repository/git/tags/$($target.sha)"
    if ($LASTEXITCODE) { throw 'Cannot resolve annotated release tag.' }
    $target = ($tagJson | ConvertFrom-Json).object
}
if ($target.type -ne 'commit' -or $target.sha -ne $env:GITHUB_SHA) { throw 'Release tag does not point to the build commit.' }
$primary = @("PotPlayerNext-$Version-win-x64.msi", "PotPlayerNext-$Version-win-x64-portable.zip", "PotPlayerNext-$Version-source.zip")
$directory = [IO.Path]::GetFullPath($AssetDirectory)
$expectedLines = @()
foreach ($name in $primary) {
    $path = Join-Path $directory $name
    $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    $line = "$hash  $name"
    if ((Get-Content -LiteralPath "$path.sha256" -Raw).Trim() -cne $line) { throw "Checksum mismatch: $name" }
    $expectedLines += $line
}
if (@(Get-Content (Join-Path $directory 'SHA256SUMS.txt')).Count -ne 3 -or
    ((Get-Content (Join-Path $directory 'SHA256SUMS.txt')) -join "`n") -cne ($expectedLines -join "`n")) { throw 'Invalid release checksums.' }
$manifest = Get-Content (Join-Path $directory 'release-manifest.json') -Raw | ConvertFrom-Json
if ($manifest.version -ne $Version -or $manifest.commit -ne $env:GITHUB_SHA) { throw 'Release provenance mismatch.' }
$assets = @($primary | ForEach-Object { Join-Path $directory $_ }) + @($primary | ForEach-Object { Join-Path $directory "$_.sha256" }) + @((Join-Path $directory 'SHA256SUMS.txt'), (Join-Path $directory 'release-manifest.json'))
$existing = & gh release view $tag --repo $Repository --json isDraft 2>$null
if ($LASTEXITCODE -eq 0) {
    if (!(($existing | ConvertFrom-Json).isDraft)) { throw "Release $tag is already published; existing downloads will not be overwritten." }
    & gh release upload $tag @assets --repo $Repository --clobber
    if ($LASTEXITCODE) { throw 'Draft release upload failed.' }
} else {
    & gh release create $tag @assets --repo $Repository --verify-tag --draft --title "PotPlayerNext $tag · qireal" --generate-notes --notes "Rust + WinUI 3 图片查看器 / 视频播放器。MSI 为当前用户安装；portable.zip 解压后运行。视频格式依赖 Windows 解码器。包含 GPL 源码与 SHA256。"
    if ($LASTEXITCODE) { throw 'Draft release creation/upload failed.' }
}
# Publish only after all uploads succeeded; a partial failure leaves a draft.
& gh release edit $tag --repo $Repository --draft=false
if ($LASTEXITCODE) { throw 'Release publication failed.' }
Write-Output "Published https://github.com/$Repository/releases/tag/$tag"
