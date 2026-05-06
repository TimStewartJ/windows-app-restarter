$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

$repoRoot = Split-Path -Parent $PSScriptRoot
$sourceIcon = Join-Path $repoRoot 'src\WindowsAppRestarter\Assets\app.ico'
$previewPng = Join-Path $repoRoot 'assets\logo-256.png'

New-Item -ItemType Directory -Path (Split-Path -Parent $sourceIcon) -Force | Out-Null
New-Item -ItemType Directory -Path (Split-Path -Parent $previewPng) -Force | Out-Null

function New-RoundedRectanglePath {
    param(
        [float] $X,
        [float] $Y,
        [float] $Width,
        [float] $Height,
        [float] $Radius
    )

    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $diameter = $Radius * 2
    $path.AddArc($X, $Y, $diameter, $diameter, 180, 90)
    $path.AddArc($X + $Width - $diameter, $Y, $diameter, $diameter, 270, 90)
    $path.AddArc($X + $Width - $diameter, $Y + $Height - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($X, $Y + $Height - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-LogoBitmap {
    param([int] $Size)

    $bitmap = [System.Drawing.Bitmap]::new($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.Clear([System.Drawing.Color]::Transparent)

    $scale = $Size / 256.0
    function S([float] $value) { return $value * $scale }

    $backgroundPath = New-RoundedRectanglePath (S 16) (S 16) (S 224) (S 224) (S 48)
    $backgroundBrush = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
        [System.Drawing.PointF]::new((S 40), (S 24)),
        [System.Drawing.PointF]::new((S 216), (S 232)),
        [System.Drawing.ColorTranslator]::FromHtml('#38bdf8'),
        [System.Drawing.ColorTranslator]::FromHtml('#1e3a8a'))
    $blend = [System.Drawing.Drawing2D.ColorBlend]::new()
    $blend.Positions = [float[]] @(0, 0.5, 1)
    $blend.Colors = [System.Drawing.Color[]] @(
        [System.Drawing.ColorTranslator]::FromHtml('#38bdf8'),
        [System.Drawing.ColorTranslator]::FromHtml('#2563eb'),
        [System.Drawing.ColorTranslator]::FromHtml('#1e3a8a'))
    $backgroundBrush.InterpolationColors = $blend
    $graphics.FillPath($backgroundBrush, $backgroundPath)

    $whiteBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::White)
    $screenBrush = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
        [System.Drawing.PointF]::new((S 78), (S 76)),
        [System.Drawing.PointF]::new((S 178), (S 164)),
        [System.Drawing.ColorTranslator]::FromHtml('#eff6ff'),
        [System.Drawing.ColorTranslator]::FromHtml('#dbeafe'))

    $monitorPath = New-RoundedRectanglePath (S 50) (S 66) (S 156) (S 104) (S 18)
    $screenPath = New-RoundedRectanglePath (S 64) (S 80) (S 128) (S 76) (S 10)
    $standPath = New-RoundedRectanglePath (S 113) (S 169) (S 30) (S 24) (S 5)
    $basePath = New-RoundedRectanglePath (S 88) (S 192) (S 80) (S 16) (S 8)
    $graphics.FillPath($whiteBrush, $monitorPath)
    $graphics.FillPath($screenBrush, $screenPath)
    $graphics.FillPath($whiteBrush, $standPath)
    $graphics.FillPath($whiteBrush, $basePath)

    $restartColor = [System.Drawing.ColorTranslator]::FromHtml('#10b981')
    $restartPen = [System.Drawing.Pen]::new($restartColor, [Math]::Max((S 16), 2))
    $restartPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $restartPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $restartPen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round

    $graphics.DrawArc($restartPen, (S 91), (S 84), (S 74), (S 74), 36, 262)
    $graphics.DrawLine($restartPen, (S 158), (S 84), (S 158), (S 117))
    $graphics.DrawLine($restartPen, (S 158), (S 117), (S 190), (S 117))

    $centerBrush = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#1e3a8a'))
    $graphics.FillEllipse($centerBrush, (S 118), (S 109), (S 20), (S 20))

    $centerBrush.Dispose()
    $restartPen.Dispose()
    $screenPath.Dispose()
    $monitorPath.Dispose()
    $standPath.Dispose()
    $basePath.Dispose()
    $screenBrush.Dispose()
    $whiteBrush.Dispose()
    $backgroundBrush.Dispose()
    $backgroundPath.Dispose()
    $graphics.Dispose()

    return $bitmap
}

function Write-IconFile {
    param(
        [string] $Path,
        [int[]] $Sizes
    )

    $pngImages = foreach ($size in $Sizes) {
        $bitmap = New-LogoBitmap -Size $size
        $stream = [System.IO.MemoryStream]::new()
        $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
        $bitmap.Dispose()

        [pscustomobject] @{
            Size = $size
            Bytes = $stream.ToArray()
        }
        $stream.Dispose()
    }

    $file = [System.IO.File]::Create($Path)
    $writer = [System.IO.BinaryWriter]::new($file)
    try {
        $writer.Write([uint16] 0)
        $writer.Write([uint16] 1)
        $writer.Write([uint16] $pngImages.Count)

        $offset = 6 + (16 * $pngImages.Count)
        foreach ($image in $pngImages) {
            $writer.Write([byte] $(if ($image.Size -eq 256) { 0 } else { $image.Size }))
            $writer.Write([byte] $(if ($image.Size -eq 256) { 0 } else { $image.Size }))
            $writer.Write([byte] 0)
            $writer.Write([byte] 0)
            $writer.Write([uint16] 1)
            $writer.Write([uint16] 32)
            $writer.Write([uint32] $image.Bytes.Length)
            $writer.Write([uint32] $offset)
            $offset += $image.Bytes.Length
        }

        foreach ($image in $pngImages) {
            $writer.Write($image.Bytes)
        }
    }
    finally {
        $writer.Dispose()
        $file.Dispose()
    }
}

$preview = New-LogoBitmap -Size 256
$preview.Save($previewPng, [System.Drawing.Imaging.ImageFormat]::Png)
$preview.Dispose()

Write-IconFile -Path $sourceIcon -Sizes @(256, 128, 64, 48, 32, 16)

Write-Host "Generated $sourceIcon" -ForegroundColor Green
Write-Host "Generated $previewPng" -ForegroundColor Green
