param([string]$OutputDirectory = 'artifacts/installer')
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = Split-Path $PSScriptRoot -Parent
$output = Join-Path $root $OutputDirectory
New-Item -ItemType Directory -Path $output -Force | Out-Null
$icoBytes = [System.IO.File]::ReadAllBytes((Join-Path $root 'src/PotPlayerNext/Assets/app.ico'))
$pngIndex = -1
for ($i = 0; $i -lt $icoBytes.Length - 4; $i++) {
    if ($icoBytes[$i] -eq 0x89 -and $icoBytes[$i+1] -eq 0x50 -and $icoBytes[$i+2] -eq 0x4E -and $icoBytes[$i+3] -eq 0x47) {
        $pngIndex = $i
    }
}
$ms = [System.IO.MemoryStream]::new($icoBytes, $pngIndex, $icoBytes.Length - $pngIndex)
$iconBmp = [System.Drawing.Bitmap]::FromStream($ms)
try {
    foreach ($kind in @('dialog','banner')) {
        $height = if ($kind -eq 'dialog') { 312 } else { 58 }
        $bmp = [Drawing.Bitmap]::new(493,$height,[Drawing.Imaging.PixelFormat]::Format24bppRgb)
        $g = [Drawing.Graphics]::FromImage($bmp)
        $g.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $g.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.TextRenderingHint = [Drawing.Text.TextRenderingHint]::AntiAliasGridFit
        $g.Clear([Drawing.Color]::White)
        try {
            if ($kind -eq 'dialog') {
                $brush = [Drawing.Drawing2D.LinearGradientBrush]::new([Drawing.Rectangle]::new(0,0,164,312),[Drawing.ColorTranslator]::FromHtml('#102b44'),[Drawing.ColorTranslator]::FromHtml('#087f88'),[single]70)
                $g.FillRectangle($brush,0,0,164,312); $brush.Dispose()
                $pen = [Drawing.Pen]::new([Drawing.Color]::FromArgb(25,255,255,255),1)
                foreach ($r in @(100,160,220)) { $g.DrawEllipse($pen,82-$r/2,100-$r/2,$r,$r) }; $pen.Dispose()
                $g.DrawImage($iconBmp,[Drawing.Rectangle]::new(42,58,80,80))
                $font = [Drawing.Font]::new('Segoe UI',12,[Drawing.FontStyle]::Bold,[Drawing.GraphicsUnit]::Pixel)
                $g.DrawString('PotPlayerNext',$font,[Drawing.Brushes]::White,24,159); $font.Dispose()
                $font = [Drawing.Font]::new('Segoe UI',10,[Drawing.FontStyle]::Regular,[Drawing.GraphicsUnit]::Pixel)
                $g.DrawString("IMAGES + VIDEO`nNATIVE WINUI 3",$font,[Drawing.Brushes]::White,25,190)
                $g.DrawString('OPEN SOURCE / GPL-3.0',$font,[Drawing.Brushes]::White,17,280); $font.Dispose()
            } else {
                $g.DrawImage($iconBmp,[Drawing.Rectangle]::new(442,9,40,40))
                $pen = [Drawing.Pen]::new([Drawing.ColorTranslator]::FromHtml('#dce9ed'),1)
                $g.DrawLine($pen,0,57,493,57); $pen.Dispose()
            }
            $bmp.Save((Join-Path $output "$kind.bmp"),[Drawing.Imaging.ImageFormat]::Bmp)
        } finally { $g.Dispose(); $bmp.Dispose() }
    }
} finally { $iconBmp.Dispose(); $ms.Dispose() }
Write-Host 'Generated project-owned installer branding (493x312 / 493x58).'
