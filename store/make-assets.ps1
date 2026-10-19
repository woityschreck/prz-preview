# Erzeugt App-Symbole für MSIX-Paket und Store-Eintrag (PNG) per System.Drawing.
Add-Type -AssemblyName System.Drawing
$out = Join-Path $PSScriptRoot 'assets'
New-Item -ItemType Directory -Force $out | Out-Null

function New-RoundRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90); $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90); $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure(); return $p
}

# Zeichnet das Symbol (isometrischer Quader mit Uhr-Akzent) in ein Quadrat der Kantenlänge $s bei ($ox,$oy)
function Draw-Icon($g, [float]$ox, [float]$oy, [float]$s, [bool]$background) {
    if ($background) {
        $rect = New-Object System.Drawing.RectangleF $ox, $oy, $s, $s
        $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.Color]::FromArgb(255, 0x3A, 0x7B, 0xEB)), ([System.Drawing.Color]::FromArgb(255, 0x1F, 0x4F, 0xB0)), 90
        $g.FillPath($bg, (New-RoundRect $ox $oy $s $s ($s * 0.22)))
    }
    $cx = $ox + $s * 0.47; $cy = $oy + $s * 0.52; $a = $s * 0.27   # Quader-Mittelpunkt und Kantenmaß
    $top   = [System.Drawing.PointF[]]@((New-Object System.Drawing.PointF $cx, ($cy - $a)), (New-Object System.Drawing.PointF ($cx + $a * 0.95), ($cy - $a * 0.5)), (New-Object System.Drawing.PointF $cx, $cy), (New-Object System.Drawing.PointF ($cx - $a * 0.95), ($cy - $a * 0.5)))
    $left  = [System.Drawing.PointF[]]@((New-Object System.Drawing.PointF ($cx - $a * 0.95), ($cy - $a * 0.5)), (New-Object System.Drawing.PointF $cx, $cy), (New-Object System.Drawing.PointF $cx, ($cy + $a)), (New-Object System.Drawing.PointF ($cx - $a * 0.95), ($cy + $a * 0.5)))
    $right = [System.Drawing.PointF[]]@((New-Object System.Drawing.PointF $cx, $cy), (New-Object System.Drawing.PointF ($cx + $a * 0.95), ($cy - $a * 0.5)), (New-Object System.Drawing.PointF ($cx + $a * 0.95), ($cy + $a * 0.5)), (New-Object System.Drawing.PointF $cx, ($cy + $a)))
    $g.FillPolygon((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 0xF4, 0xF7, 0xFC))), $top)
    $g.FillPolygon((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 0xC9, 0xD6, 0xEA))), $left)
    $g.FillPolygon((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 0xA3, 0xB7, 0xD6))), $right)
    # Uhr-Akzent (Bearbeitungszeit) unten rechts
    $r = $s * 0.17; $ux = $ox + $s * 0.72; $uy = $oy + $s * 0.72
    $g.FillEllipse((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 0xF0, 0xA0, 0x30))), $ux - $r, $uy - $r, 2 * $r, 2 * $r)
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), ([Math]::Max(1.2, $s * 0.035))
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'
    $g.DrawLine($pen, $ux, $uy, $ux, $uy - $r * 0.6)
    $g.DrawLine($pen, $ux, $uy, $ux + $r * 0.45, $uy)
}

function Save-Png([int]$w, [int]$h, [string]$name, [float]$scale = 1.0, [bool]$transparentBg = $false) {
    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.InterpolationMode = 'HighQualityBicubic'
    $g.Clear([System.Drawing.Color]::Transparent)
    $s = [Math]::Min($w, $h) * $scale
    Draw-Icon $g (($w - $s) / 2) (($h - $s) / 2) $s (-not $transparentBg)
    $bmp.Save((Join-Path $out $name), [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
    Write-Host "  $name  ${w}x$h"
}

Save-Png 44 44 'Square44x44Logo.png'
Save-Png 150 150 'Square150x150Logo.png'
Save-Png 310 150 'Wide310x150Logo.png' 0.9
Save-Png 50 50 'StoreLogo.png'
Save-Png 300 300 'Store-Logo-300x300.png'
Save-Png 1080 1080 'Store-Logo-1080x1080.png'
