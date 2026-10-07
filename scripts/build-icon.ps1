$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = Split-Path $PSScriptRoot -Parent
$path = Join-Path $root 'src/PotPlayerNext/Assets/app.ico'
$images = @()
foreach ($size in @(16,32,48,64,128,256)) {
    $bitmap = [Drawing.Bitmap]::new($size,$size)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.ScaleTransform($size / 256.0, $size / 256.0)
    $sky = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#42B8F4'))
    $graphics.FillEllipse($sky,4,4,248,248)
    $brush = [Drawing.SolidBrush]::new([Drawing.Color]::White)
    $play = [Drawing.Drawing2D.GraphicsPath]::new()
    # Cubic equivalents of the SVG/XAML quadratic corner curves.
    $play.AddBezier(75,61,75,56.33333,77.33333,55.33333,82,58)
    $play.AddLine(82,58,192,122)
    $play.AddBezier(192,122,198,126,198,130,192,134)
    $play.AddLine(192,134,82,198)
    $play.AddBezier(82,198,77.33333,200.66667,75,199.66667,75,195)
    $play.CloseFigure(); $graphics.FillPath($brush,$play)
    $points = [Drawing.PointF[]]@(
        [Drawing.PointF]::new(167,166),[Drawing.PointF]::new(173,166),[Drawing.PointF]::new(184,182),
        [Drawing.PointF]::new(184,166),[Drawing.PointF]::new(190,166),[Drawing.PointF]::new(190,191),
        [Drawing.PointF]::new(184,191),[Drawing.PointF]::new(173,175),[Drawing.PointF]::new(173,191),[Drawing.PointF]::new(167,191))
    $graphics.FillPolygon($brush,$points)
    $stream = [IO.MemoryStream]::new(); $bitmap.Save($stream,[Drawing.Imaging.ImageFormat]::Png)
    $images += @{Size=$size; Bytes=$stream.ToArray()}
    $stream.Dispose(); $brush.Dispose(); $sky.Dispose(); $play.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
}
$stream = [IO.File]::Open($path,[IO.FileMode]::Create)
$writer = [IO.BinaryWriter]::new($stream)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$images.Count)
    $offset = 6 + 16 * $images.Count
    foreach ($image in $images) {
        $dimension = if ($image.Size -eq 256) { 0 } else { $image.Size }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension); $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$image.Bytes.Length); $writer.Write([uint32]$offset)
        $offset += $image.Bytes.Length
    }
    foreach ($image in $images) { $writer.Write([byte[]]$image.Bytes) }
} finally { $writer.Dispose() }
Write-Host "Generated multi-resolution icon: $path"
