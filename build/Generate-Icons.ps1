#Requires -Version 5.1
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function Draw-Icon {
    param([int] $Size)

    $bmp = [System.Drawing.Bitmap]::new(
        $Size, $Size,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        try {
            $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
            $g.Clear([System.Drawing.Color]::Transparent)

            # Rounded-rect background (#141C28)
            $radius = [Math]::Max(2, [int]($Size * 0.18))
            $r2     = $radius * 2
            $bgBrush = [System.Drawing.SolidBrush]::new(
                [System.Drawing.Color]::FromArgb(255, 0x14, 0x1C, 0x28))
            $bgPath = [System.Drawing.Drawing2D.GraphicsPath]::new()
            $bgPath.AddArc(0,            0,            $r2, $r2, 180, 90)
            $bgPath.AddArc($Size - $r2,  0,            $r2, $r2, 270, 90)
            $bgPath.AddArc($Size - $r2,  $Size - $r2,  $r2, $r2,   0, 90)
            $bgPath.AddArc(0,            $Size - $r2,  $r2, $r2,  90, 90)
            $bgPath.CloseFigure()
            $g.FillPath($bgBrush, $bgPath)
            $bgBrush.Dispose()
            $bgPath.Dispose()

            # Size-adaptive rules
            $drawChevrons  = $Size -ge 32

            # Play triangle dimensions
            $triH    = [int]($Size * 0.55)
            $triW    = [int]($triH * 0.866)
            $centerY = if ($drawChevrons) { [int]($Size * 0.38) } else { [int]($Size * 0.50) }
            $x0      = [int](($Size - $triW) / 2)
            $y0      = [int]($centerY - $triH / 2)

            $tealBrush = [System.Drawing.SolidBrush]::new(
                [System.Drawing.Color]::FromArgb(255, 0x4F, 0xC3, 0xF7))
            $triPts = [System.Drawing.PointF[]] @(
                [System.Drawing.PointF]::new($x0,          $y0),
                [System.Drawing.PointF]::new($x0,          $y0 + $triH),
                [System.Drawing.PointF]::new($x0 + $triW,  $centerY)
            )
            $g.FillPolygon($tealBrush, $triPts)
            $tealBrush.Dispose()

            if ($drawChevrons) {
                $chevronCount = if ($Size -ge 50) { 2 } else { 1 }
                $sw     = [float][Math]::Max(1.0, $Size * 0.07)
                $cw     = [float]($triW * 0.75)
                $ch     = [float]($Size * 0.10)
                $cx     = [float]($Size / 2)
                $baseY  = [float]($y0 + $triH + $Size * 0.06)
                $gap    = [float]($Size * 0.14)

                for ($i = 0; $i -lt $chevronCount; $i++) {
                    $cy    = $baseY + $i * $gap
                    $alpha = if ($i -eq 0) { 255 } else { 128 }
                    $pen   = [System.Drawing.Pen]::new(
                        [System.Drawing.Color]::FromArgb($alpha, 0x4F, 0xC3, 0xF7), $sw)
                    $pen.StartCap  = [System.Drawing.Drawing2D.LineCap]::Round
                    $pen.EndCap    = [System.Drawing.Drawing2D.LineCap]::Round
                    $pen.LineJoin  = [System.Drawing.Drawing2D.LineJoin]::Round
                    $chevPts = [System.Drawing.PointF[]] @(
                        [System.Drawing.PointF]::new($cx - $cw / 2, $cy),
                        [System.Drawing.PointF]::new($cx,            $cy + $ch),
                        [System.Drawing.PointF]::new($cx + $cw / 2, $cy)
                    )
                    $g.DrawLines($pen, $chevPts)
                    $pen.Dispose()
                }
            }
        }
        finally {
            $g.Dispose()
        }
    }
    catch {
        $bmp.Dispose()
        throw
    }
    return $bmp
}

function Get-PngBytes {
    param([System.Drawing.Bitmap] $Bitmap)
    $ms = [System.IO.MemoryStream]::new()
    try {
        $Bitmap.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        return , $ms.ToArray()
    }
    finally { $ms.Dispose() }
}

function New-IcoBytes {
    param([byte[][]] $PngArrays, [int[]] $Sizes)
    $count     = $PngArrays.Count
    $dirOffset = 6 + $count * 16
    $offsets   = [int[]]::new($count)
    $running   = $dirOffset
    for ($i = 0; $i -lt $count; $i++) {
        $offsets[$i] = $running
        $running    += $PngArrays[$i].Length
    }
    $ms = [System.IO.MemoryStream]::new()
    $bw = [System.IO.BinaryWriter]::new($ms)
    # ICONDIR
    $bw.Write([uint16] 0)        # reserved
    $bw.Write([uint16] 1)        # type = icon
    $bw.Write([uint16] $count)   # count
    # ICONDIRENTRY × N
    for ($i = 0; $i -lt $count; $i++) {
        $dim = if ($Sizes[$i] -ge 256) { 0 } else { $Sizes[$i] }
        $bw.Write([byte]   $dim)
        $bw.Write([byte]   $dim)
        $bw.Write([byte]   0)    # color count
        $bw.Write([byte]   0)    # reserved
        $bw.Write([uint16] 0)    # planes (0 for PNG-in-ICO per Vista+ spec)
        $bw.Write([uint16] 0)    # bit count (0 for PNG-in-ICO per Vista+ spec)
        $bw.Write([uint32] $PngArrays[$i].Length)
        $bw.Write([uint32] $offsets[$i])
    }
    # PNG data
    foreach ($png in $PngArrays) { $bw.Write($png) }
    $bw.Flush()
    $bytes = $ms.ToArray()
    $bw.Dispose()
    $ms.Dispose()
    return , $bytes
}

# ── Resolve paths ──────────────────────────────────────────────────────────────
$repoRoot  = Split-Path $PSScriptRoot -Parent
$appAssets = Join-Path $repoRoot 'src\VideoTriage.App\Assets'
$pkgAssets = Join-Path $repoRoot 'src\VideoTriage.Package\Assets'
New-Item -ItemType Directory -Force -Path $appAssets | Out-Null
New-Item -ItemType Directory -Force -Path $pkgAssets | Out-Null

# ── Build ICO ──────────────────────────────────────────────────────────────────
$icoSizes  = @(16, 24, 32, 48, 256)
$pngList   = [System.Collections.Generic.List[byte[]]]::new()
foreach ($sz in $icoSizes) {
    $bmp = Draw-Icon -Size $sz
    try   { $pngList.Add((Get-PngBytes -Bitmap $bmp)) }
    finally { $bmp.Dispose() }
}
$icoPath = Join-Path $appAssets 'app.ico'
[System.IO.File]::WriteAllBytes($icoPath, (New-IcoBytes -PngArrays $pngList.ToArray() -Sizes $icoSizes))
Write-Output "Written: $icoPath ($($icoSizes -join ',') px)"

# ── MSIX PNGs ─────────────────────────────────────────────────────────────────
$msixAssets = [ordered]@{
    'Square44x44Logo.png'   = 44
    'StoreLogo.png'         = 50
    'Square150x150Logo.png' = 150
}
foreach ($name in $msixAssets.Keys) {
    $sz  = $msixAssets[$name]
    $bmp = Draw-Icon -Size $sz
    try {
        $out = Join-Path $pkgAssets $name
        $bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
        Write-Output "Written: $out (${sz}x${sz})"
    }
    finally { $bmp.Dispose() }
}

Write-Output 'Icon generation complete.'
