# Genere assets\NHMonitor.ico (16 a 256 px) : trois barres CPU / GPU / RAM sur fond sombre arrondi.
# Usage : powershell -ExecutionPolicy Bypass -File build\make-icon.ps1
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root 'assets\NHMonitor.ico'
New-Item -ItemType Directory -Force (Split-Path $out) | Out-Null

$sizes = 16, 24, 32, 48, 64, 128, 256
$colors = @(
    [System.Drawing.Color]::FromArgb(255, 80, 170, 255),
    [System.Drawing.Color]::FromArgb(255, 90, 210, 110),
    [System.Drawing.Color]::FromArgb(255, 200, 130, 255)
)
$levels = 0.55, 0.85, 0.40

$pngs = foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)

    $r = [Math]::Max(2, [int]($s * 0.18))
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc(0, 0, 2 * $r, 2 * $r, 180, 90)
    $path.AddArc($s - 2 * $r - 1, 0, 2 * $r, 2 * $r, 270, 90)
    $path.AddArc($s - 2 * $r - 1, $s - 2 * $r - 1, 2 * $r, 2 * $r, 0, 90)
    $path.AddArc(0, $s - 2 * $r - 1, 2 * $r, 2 * $r, 90, 90)
    $path.CloseFigure()
    $bg = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 24, 32, 52))
    $g.FillPath($bg, $path)

    $margin = $s * 0.18
    $inner = $s - 2 * $margin
    $barW = $inner / 3
    for ($i = 0; $i -lt 3; $i++) {
        $h = [Math]::Max(1, $inner * $levels[$i])
        $x = $margin + $i * $barW + $barW * 0.12
        $w = $barW * 0.76
        $brush = New-Object System.Drawing.SolidBrush $colors[$i]
        $g.FillRectangle($brush, [float]$x, [float]($margin + $inner - $h), [float]$w, [float]$h)
        $brush.Dispose()
    }
    $g.Dispose()

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    , $ms.ToArray()
}

# Format ICO : en-tete + repertoire + images PNG.
$fs = [System.IO.File]::Create($out)
$w = New-Object System.IO.BinaryWriter $fs
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $dim = if ($s -ge 256) { 0 } else { $s }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$pngs[$i].Length); $w.Write([uint32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $w.Write($p) }
$w.Close()
Write-Output "Icon written: $out"
