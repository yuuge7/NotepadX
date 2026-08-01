# Generates Assets/app.ico from scratch so no binary asset needs to live in the repo.
# Windows PowerShell 5.1 + System.Drawing, no downloads.
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$assets = Join-Path $root 'Assets'
if (-not (Test-Path $assets)) { New-Item -ItemType Directory -Path $assets | Out-Null }

function New-RoundedPath([single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function New-IconBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.Clear([System.Drawing.Color]::Transparent)

    $s = $size / 256.0

    # Back sheet, offset up-right, suggesting the tab strip
    $backPath = New-RoundedPath (52 * $s) (26 * $s) (178 * $s) (170 * $s) (26 * $s)
    $backBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(90, 255, 255, 255))
    $g.FillPath($backBrush, $backPath)
    $backPen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(120, 0, 90, 158)), (3 * $s)
    $g.DrawPath($backPen, $backPath)

    # Front sheet
    $frontPath = New-RoundedPath (26 * $s) (56 * $s) (178 * $s) (176 * $s) (26 * $s)
    $frontBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.PointF((26 * $s), (56 * $s))),
        (New-Object System.Drawing.PointF((204 * $s), (232 * $s))),
        [System.Drawing.Color]::FromArgb(255, 42, 145, 226),
        [System.Drawing.Color]::FromArgb(255, 10, 86, 158))
    $g.FillPath($frontBrush, $frontPath)

    # Text lines
    $ink = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(240, 255, 255, 255))
    $lh = [Math]::Max(1.0, 14 * $s)
    $lx = 54 * $s
    $lw = 122 * $s
    $y0 = 96 * $s
    $gap = 34 * $s
    $widths = @(1.0, 1.0, 0.62)
    for ($i = 0; $i -lt 3; $i++) {
        $p = New-RoundedPath $lx ($y0 + $i * $gap) ($lw * $widths[$i]) $lh ([Math]::Max(1.0, $lh / 2))
        $g.FillPath($ink, $p)
        $p.Dispose()
    }

    # Caret, the one detail that says "editor" rather than "document"
    $caret = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 255, 214, 102))
    $cp = New-RoundedPath (54 * $s + $lw * 0.62 + 8 * $s) (158 * $s) (7 * $s) (22 * $s) (3 * $s)
    $g.FillPath($caret, $cp)

    $g.Dispose()
    return $bmp
}

$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$pngs = @()
foreach ($sz in $sizes) {
    $bmp = New-IconBitmap $sz
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs += , @{ Size = $sz; Bytes = $ms.ToArray() }
    $ms.Dispose()
    $bmp.Dispose()
}

$icoPath = Join-Path $assets 'app.ico'
$fs = [System.IO.File]::Create($icoPath)
$bw = New-Object System.IO.BinaryWriter($fs)

$bw.Write([UInt16]0)                 # reserved
$bw.Write([UInt16]1)                 # type: icon
$bw.Write([UInt16]$pngs.Count)

$offset = 6 + 16 * $pngs.Count
foreach ($p in $pngs) {
    $dim = if ($p.Size -ge 256) { 0 } else { $p.Size }
    $bw.Write([Byte]$dim)            # width
    $bw.Write([Byte]$dim)            # height
    $bw.Write([Byte]0)               # palette
    $bw.Write([Byte]0)               # reserved
    $bw.Write([UInt16]1)             # colour planes
    $bw.Write([UInt16]32)            # bits per pixel
    $bw.Write([UInt32]$p.Bytes.Length)
    $bw.Write([UInt32]$offset)
    $offset += $p.Bytes.Length
}
foreach ($p in $pngs) { $bw.Write($p.Bytes) }

$bw.Flush(); $bw.Dispose(); $fs.Dispose()
Write-Output "wrote $icoPath"
