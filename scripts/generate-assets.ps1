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
        [float] $Radius,
        [switch] $TopOnly
    )

    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $diameter = $Radius * 2
    $path.AddArc($X, $Y, $diameter, $diameter, 180, 90)
    $path.AddArc($X + $Width - $diameter, $Y, $diameter, $diameter, 270, 90)
    if ($TopOnly) {
        $path.AddLine($X + $Width, $Y + $Height, $X, $Y + $Height)
    }
    else {
        $path.AddArc($X + $Width - $diameter, $Y + $Height - $diameter, $diameter, $diameter, 0, 90)
        $path.AddArc($X, $Y + $Height - $diameter, $diameter, $diameter, 90, 90)
    }
    $path.CloseFigure()
    return $path
}

function Color([string] $hex) { return [System.Drawing.ColorTranslator]::FromHtml($hex) }

# Draws a clockwise circular arrow: an arc with a round tail cap and a filled
# triangular head, so it reads as "restart" instead of a clock face.
function Draw-RestartArrow {
    param(
        [System.Drawing.Graphics] $Graphics,
        [System.Drawing.Brush] $Brush,
        [float] $CenterX,
        [float] $CenterY,
        [float] $Radius,
        [float] $Stroke
    )

    $startAngle = -20.0
    $sweep = 280.0
    $endAngle = $startAngle + $sweep

    $pen = [System.Drawing.Pen]::new($Brush, $Stroke)
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Flat
    $Graphics.DrawArc($pen, $CenterX - $Radius, $CenterY - $Radius, $Radius * 2, $Radius * 2, $startAngle, $sweep)
    $pen.Dispose()

    $theta = $endAngle * [Math]::PI / 180.0
    $endX = $CenterX + $Radius * [Math]::Cos($theta)
    $endY = $CenterY + $Radius * [Math]::Sin($theta)
    $dirX = -[Math]::Sin($theta)
    $dirY = [Math]::Cos($theta)
    $normX = -$dirY
    $normY = $dirX

    $headLength = $Stroke * 1.3
    $headHalfWidth = $Stroke * 1.1
    $baseX = $endX - $dirX * ($Stroke * 0.2)
    $baseY = $endY - $dirY * ($Stroke * 0.2)

    $points = [System.Drawing.PointF[]] @(
        [System.Drawing.PointF]::new($baseX + $dirX * $headLength, $baseY + $dirY * $headLength),
        [System.Drawing.PointF]::new($baseX + $normX * $headHalfWidth, $baseY + $normY * $headHalfWidth),
        [System.Drawing.PointF]::new($baseX - $normX * $headHalfWidth, $baseY - $normY * $headHalfWidth)
    )
    $Graphics.FillPolygon($Brush, $points)
}

function New-LogoBitmap {
    param([int] $Size)

    $bitmap = [System.Drawing.Bitmap]::new($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $graphics.Clear([System.Drawing.Color]::Transparent)

    $scale = $Size / 256.0
    function S([float] $value) { return [float]($value * $scale) }

    # Detail tiers keep the mark legible in the notification area.
    $small = $Size -le 24
    $medium = $Size -gt 24 -and $Size -le 48

    $inset = if ($small) { 0 } elseif ($medium) { 6 } else { 12 }
    $radius = if ($small) { 64 } else { 56 }
    $backgroundPath = New-RoundedRectanglePath (S $inset) (S $inset) (S (256 - 2 * $inset)) (S (256 - 2 * $inset)) (S $radius)
    $backgroundBrush = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
        [System.Drawing.PointF]::new((S 0), (S 0)),
        [System.Drawing.PointF]::new((S 256), (S 256)),
        (Color '#4FB4FF'),
        (Color '#0B3C8F'))
    $blend = [System.Drawing.Drawing2D.ColorBlend]::new()
    $blend.Positions = [float[]] @(0, 0.55, 1)
    $blend.Colors = [System.Drawing.Color[]] @((Color '#4FB4FF'), (Color '#1F6FE0'), (Color '#0B3C8F'))
    $backgroundBrush.InterpolationColors = $blend
    $graphics.FillPath($backgroundBrush, $backgroundPath)

    $whiteBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::White)
    $arrowBrush = [System.Drawing.SolidBrush]::new((Color '#22C55E'))

    if ($small) {
        # Background + big arrow only. Windows and title bars turn to mush at 16px.
        Draw-RestartArrow -Graphics $graphics -Brush $whiteBrush -CenterX (S 128) -CenterY (S 128) -Radius (S 78) -Stroke (S 34)
    }
    else {
        $windowX = if ($medium) { 34 } else { 40 }
        $windowY = if ($medium) { 48 } else { 56 }
        $windowW = 256 - 2 * $windowX
        $windowH = if ($medium) { 160 } else { 144 }
        $windowR = 20
        $titleH = if ($medium) { 34 } else { 30 }

        $windowPath = New-RoundedRectanglePath (S $windowX) (S $windowY) (S $windowW) (S $windowH) (S $windowR)
        $titlePath = New-RoundedRectanglePath (S $windowX) (S $windowY) (S $windowW) (S $titleH) (S $windowR) -TopOnly

        $screenBrush = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
            [System.Drawing.PointF]::new((S $windowX), (S $windowY)),
            [System.Drawing.PointF]::new((S $windowX), (S ($windowY + $windowH))),
            [System.Drawing.Color]::White,
            (Color '#EAF3FF'))
        $graphics.FillPath($screenBrush, $windowPath)

        $titleBrush = [System.Drawing.SolidBrush]::new((Color '#C7E0FF'))
        $graphics.FillPath($titleBrush, $titlePath)

        if (-not $medium) {
            $dotBrush = [System.Drawing.SolidBrush]::new((Color '#7FB3F0'))
            $dotY = $windowY + $titleH / 2 - 5
            $graphics.FillEllipse($dotBrush, (S 58), (S $dotY), (S 10), (S 10))
            $graphics.FillEllipse($dotBrush, (S 76), (S $dotY), (S 10), (S 10))
            $dotBrush.Dispose()
        }

        $screenTop = $windowY + $titleH
        $screenCenterY = $screenTop + ($windowH - $titleH) / 2
        $arrowRadius = if ($medium) { 42 } else { 40 }
        $arrowStroke = if ($medium) { 18 } else { 16 }
        Draw-RestartArrow -Graphics $graphics -Brush $arrowBrush -CenterX (S 128) -CenterY (S $screenCenterY) -Radius (S $arrowRadius) -Stroke (S $arrowStroke)

        $titleBrush.Dispose()
        $screenBrush.Dispose()
        $titlePath.Dispose()
        $windowPath.Dispose()
    }

    $arrowBrush.Dispose()
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

Write-IconFile -Path $sourceIcon -Sizes @(256, 128, 64, 48, 40, 32, 24, 20, 16)

Write-Host "Generated $sourceIcon" -ForegroundColor Green
Write-Host "Generated $previewPng" -ForegroundColor Green
