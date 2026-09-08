$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = Split-Path $PSScriptRoot -Parent
$sheet = [Drawing.Bitmap]::FromFile((Join-Path $root 'assets\sprites\walk_repaired_sheet.png'))
$idle = [Drawing.Bitmap]::FromFile((Join-Path $root 'assets\sprites\girlfriend_cherry_v2\idle.png'))
try {
    $cellWidth = [int]($sheet.Width / 4)
    $left = $cellWidth; $top = $sheet.Height; $right = 0; $bottom = 0
    for ($y = 0; $y -lt $sheet.Height; $y++) {
        for ($x = 0; $x -lt $sheet.Width; $x++) {
            if ($sheet.GetPixel($x, $y).A -ge 128) {
                $cx = $x % $cellWidth
                $left = [Math]::Min($left, $cx); $right = [Math]::Max($right, $cx)
                $top = [Math]::Min($top, $y); $bottom = [Math]::Max($bottom, $y)
            }
        }
    }
    for ($i = 0; $i -lt 4; $i++) {
        $frame = New-Object Drawing.Bitmap $idle.Width, $idle.Height
        $graphics = [Drawing.Graphics]::FromImage($frame)
        try {
            $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
            $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::Half
            $src = New-Object Drawing.Rectangle ($i * $cellWidth + $left), $top, ($right - $left + 1), ($bottom - $top + 1)
            $dst = New-Object Drawing.Rectangle 4, 5, ($idle.Width - 8), ($idle.Height - 12)
            $graphics.DrawImage($sheet, $dst, $src, [Drawing.GraphicsUnit]::Pixel)
            # Color-key windows cannot display partial alpha without colored fringes.
            for ($y = 0; $y -lt $frame.Height; $y++) {
                for ($x = 0; $x -lt $frame.Width; $x++) {
                    $c = $frame.GetPixel($x, $y)
                    if ($c.A -lt 128) { $frame.SetPixel($x, $y, [Drawing.Color]::Transparent) }
                    else { $frame.SetPixel($x, $y, [Drawing.Color]::FromArgb(255, $c.R, $c.G, $c.B)) }
                }
            }
            $frame.Save((Join-Path $root ('assets\sprites\girlfriend_cherry_v2\walk_clean_{0}.png' -f ($i + 1))), [Drawing.Imaging.ImageFormat]::Png)
        } finally { $graphics.Dispose(); $frame.Dispose() }
    }
    Write-Output 'Prepared four aligned walking frames.'
} finally { $sheet.Dispose(); $idle.Dispose() }
