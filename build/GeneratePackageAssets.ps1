[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function New-VideoTriageLogo {
    param(
        [Parameter(Mandatory)]
        [string] $Path,

        [Parameter(Mandatory)]
        [int] $Size
    )

    $bitmap = [System.Drawing.Bitmap]::new($Size, $Size)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.Clear([System.Drawing.Color]::FromArgb(20, 28, 40))

        $accent = [System.Drawing.SolidBrush]::new(
            [System.Drawing.Color]::FromArgb(92, 200, 255))
        try {
            $points = [System.Drawing.PointF[]] @(
                [System.Drawing.PointF]::new($Size * 0.34, $Size * 0.22),
                [System.Drawing.PointF]::new($Size * 0.34, $Size * 0.78),
                [System.Drawing.PointF]::new($Size * 0.76, $Size * 0.50)
            )
            $graphics.FillPolygon($accent, $points)
        }
        finally {
            $accent.Dispose()
        }

        $directory = Split-Path -Parent $Path
        New-Item -ItemType Directory -Force -Path $directory | Out-Null
        $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

$assetDirectory = Join-Path $PSScriptRoot '..\src\VideoTriage.Package\Assets'
New-VideoTriageLogo (Join-Path $assetDirectory 'Square44x44Logo.png') 44
New-VideoTriageLogo (Join-Path $assetDirectory 'Square150x150Logo.png') 150
New-VideoTriageLogo (Join-Path $assetDirectory 'StoreLogo.png') 50

Get-ChildItem $assetDirectory -Filter '*.png' |
    Sort-Object Name |
    Select-Object Name, Length
