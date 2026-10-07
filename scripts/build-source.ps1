param([string]$Version = '')
$ErrorActionPreference = 'Stop'
if (!$Version) { $Version = & "$PSScriptRoot/get-version.ps1" }
$root = Split-Path $PSScriptRoot -Parent
$output = Join-Path $root 'artifacts/installer'
New-Item -ItemType Directory -Path $output -Force | Out-Null
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archivePath = Join-Path $output "PotPlayerNext-$Version-source.zip"
$stream = [IO.File]::Open($archivePath, [IO.FileMode]::Create, [IO.FileAccess]::Write)
$archive = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($source in @('native','src','formats','scripts','installer','tests','docs','licenses','.github','.gitignore','.gitattributes','global.json','PotPlayerNext.sln','Cargo.toml','Cargo.lock','LICENSE','README.md','THIRD-PARTY-NOTICES.md')) {
        $path = Join-Path $root $source
        $files = if (Test-Path $path -PathType Container) { Get-ChildItem $path -File -Recurse } else { Get-Item $path }
        foreach ($file in $files) {
            $relative = $file.FullName.Substring($root.Length).TrimStart('\', '/')
            if ($relative -match '(^|[\\/])(bin|obj)([\\/]|$)') { continue }
            [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $relative.Replace('\','/'), [IO.Compression.CompressionLevel]::Optimal)
        }
    }
} finally { $archive.Dispose() }
$hash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash
[IO.File]::WriteAllText("$archivePath.sha256", "$hash  $([IO.Path]::GetFileName($archivePath))`n")
Write-Host "Source archive: $archivePath, SHA256 $hash"
