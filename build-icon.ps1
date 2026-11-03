[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$sizes = @(16, 24, 32, 48, 64, 128, 256)
$images = New-Object 'System.Collections.Generic.List[byte[]]'
foreach ($size in $sizes) {
    $bitmap = New-Object Drawing.Bitmap $size, $size
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $stream = New-Object IO.MemoryStream
    try {
        $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.Clear([Drawing.Color]::Transparent)
        $graphics.ScaleTransform(($size / 256.0), ($size / 256.0))
        $tile = New-Object Drawing.Drawing2D.GraphicsPath
        $gradient = New-Object Drawing.Drawing2D.LinearGradientBrush ([Drawing.Rectangle]::new(12,12,232,232)), ([Drawing.Color]::FromArgb(43,64,96)), ([Drawing.Color]::FromArgb(15,23,39)), 90.0
        $blue = New-Object Drawing.Pen ([Drawing.Color]::FromArgb(89,179,255)), 17
        $mint = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(91,237,211))
        $white = New-Object Drawing.Pen ([Drawing.Color]::FromArgb(235,247,255)), 10
        try {
            $tile.AddArc(12,12,64,64,180,90)
            $tile.AddArc(180,12,64,64,270,90)
            $tile.AddArc(180,180,64,64,0,90)
            $tile.AddArc(12,180,64,64,90,90)
            $tile.CloseFigure()
            $graphics.FillPath($gradient,$tile)
            $blue.StartCap = $blue.EndCap = [Drawing.Drawing2D.LineCap]::Round
            foreach ($coords in @(@(91,62,62,62,62,91), @(165,62,194,62,194,91), @(62,165,62,194,91,194), @(165,194,194,194,194,165))) {
                $points = [Drawing.PointF[]]@(
                    [Drawing.PointF]::new($coords[0],$coords[1]),
                    [Drawing.PointF]::new($coords[2],$coords[3]),
                    [Drawing.PointF]::new($coords[4],$coords[5])
                )
                $graphics.DrawLines($blue,$points)
            }
            $spark = [Drawing.PointF[]]@(
                [Drawing.PointF]::new(137,82), [Drawing.PointF]::new(116,123),
                [Drawing.PointF]::new(137,123), [Drawing.PointF]::new(116,168),
                [Drawing.PointF]::new(154,117), [Drawing.PointF]::new(133,117)
            )
            $graphics.FillPolygon($mint,$spark)
            $graphics.DrawLine($white,167,156,167,178)
            $graphics.DrawLine($white,156,167,178,167)
        }
        finally { $tile.Dispose(); $gradient.Dispose(); $blue.Dispose(); $mint.Dispose(); $white.Dispose() }
        $bitmap.Save($stream,[Drawing.Imaging.ImageFormat]::Png)
        $images.Add($stream.ToArray())
        if ($size -eq 256) {
            $bitmap.Save((Join-Path $PSScriptRoot 'ScreenshotStudio.png'),[Drawing.Imaging.ImageFormat]::Png)
        }
    }
    finally { $graphics.Dispose(); $bitmap.Dispose(); $stream.Dispose() }
}
$file = [IO.File]::Create((Join-Path $PSScriptRoot 'ScreenshotStudio.ico'))
$writer = New-Object IO.BinaryWriter $file
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($index = 0; $index -lt $sizes.Count; $index++) {
        $dimension = if ($sizes[$index] -eq 256) { 0 } else { $sizes[$index] }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
        $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$images[$index].Length); $writer.Write([uint32]$offset)
        $offset += $images[$index].Length
    }
    foreach ($bytes in $images) { $writer.Write([byte[]]$bytes) }
}
finally { $writer.Dispose() }
Write-Output 'Created original Screenshot Studio icon in seven resolutions.'
