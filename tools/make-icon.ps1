<#
  Maakt src\app.ico (het icoon van WeekKalender.exe in Verkenner en Taakbeheer): een kalenderblaadje met "wk".
  Hoeft alleen opnieuw als je het ontwerp wilt veranderen.
#>
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = Split-Path $PSScriptRoot -Parent
$target = Join-Path $root 'src\app.ico'

function New-RoundedPath([single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function New-IconPng([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.TextRenderingHint = 'AntiAliasGridFit'
    $g.Clear([System.Drawing.Color]::Transparent)

    $m = [single]($size * 0.06)
    $w = [single]($size - 2 * $m)
    $r = [single]([Math]::Max(1.5, $size * 0.14))
    $sheet = New-RoundedPath $m $m $w $w $r
    $g.FillPath([System.Drawing.Brushes]::White, $sheet)

    $blue = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 0, 103, 192))
    $headerHeight = [single]($w * 0.30)
    $g.SetClip($sheet)
    $g.FillRectangle($blue, $m, $m, $w, $headerHeight)
    $g.ResetClip()

    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(70, 0, 0, 0)), ([single]([Math]::Max(1, $size / 32)))
    $g.DrawPath($pen, $sheet)

    $family = New-Object System.Drawing.FontFamily 'Segoe UI'
    $format = [System.Drawing.StringFormat]::GenericTypographic
    $text = 'wk'
    $probe = New-Object System.Drawing.Drawing2D.GraphicsPath
    $probe.AddString($text, $family, [int][System.Drawing.FontStyle]::Bold, 100, (New-Object System.Drawing.PointF 0, 0), $format)
    $b = $probe.GetBounds()
    $areaTop = $m + $headerHeight
    $areaHeight = $w - $headerHeight
    $scale = [Math]::Min(($w * 0.72) / $b.Width, ($areaHeight * 0.62) / $b.Height)
    $matrix = New-Object System.Drawing.Drawing2D.Matrix
    $matrix.Scale($scale, $scale)
    $probe.Transform($matrix)
    $b = $probe.GetBounds()
    $dx = [single]($m + ($w - $b.Width) / 2 - $b.X)
    $dy = [single]($areaTop + ($areaHeight - $b.Height) / 2 - $b.Y)
    $move = New-Object System.Drawing.Drawing2D.Matrix
    $move.Translate($dx, $dy)
    $probe.Transform($move)
    $dark = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 32, 32, 32))
    $g.FillPath($dark, $probe)

    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return , $ms.ToArray()
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 256
$images = @()
foreach ($s in $sizes) { $images += , (New-IconPng $s) }

# ICO-bestand met PNG-afbeeldingen (ondersteund sinds Windows Vista).
$out = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter $out
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$images[$i].Length); $bw.Write([uint32]$offset)
    $offset += $images[$i].Length
}
foreach ($img in $images) { $bw.Write($img) }
$bw.Flush()
[System.IO.File]::WriteAllBytes($target, $out.ToArray())
Write-Host "Gemaakt: $target"
