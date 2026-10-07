param()
$root = Split-Path $PSScriptRoot -Parent
$manifest = Get-Content -LiteralPath (Join-Path $root 'formats/media-formats.json') -Raw | ConvertFrom-Json
return @{ image = @($manifest.images) + @($manifest.platformImages); video = @($manifest.videos) }
