# 生成 LeftScreenMode 多尺寸图标 (src/app.ico)
# 用法: powershell -ExecutionPolicy Bypass -File src\MakeIcon.ps1
param(
    [string]$Out = ""
)

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrEmpty($Out)) {
    $Out = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) "app.ico"
}

Add-Type -AssemblyName System.Drawing

function New-IconBitmaps {
    $sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
    $result = @()
    foreach ($size in $sizes) {
        $bmp = New-Object System.Drawing.Bitmap($size, $size)
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        try {
            $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
            $g.Clear([System.Drawing.Color]::Transparent)

            $blue = [System.Drawing.Color]::FromArgb(64, 140, 240)
            $blueLight = [System.Drawing.Color]::FromArgb(150, 190, 245)
            $dark = [System.Drawing.Color]::FromArgb(24, 24, 28)
            $frame = [System.Drawing.Color]::FromArgb(110, 118, 132)

            $m = [Math]::Max(1, [int]($size * 0.06))
            $outer = New-Object System.Drawing.Rectangle($m, [int]($size * 0.16), ($size - $m * 2), [int]($size * 0.62))
            $pen = New-Object System.Drawing.Pen($frame, ([Math]::Max(1.0, $size / 24.0)))
            $g.DrawRectangle($pen, $outer)
            $pen.Dispose()

            $pad = [Math]::Max(1, [int]($size * 0.09))
            $mid = [int]($size / 2)
            $leftW = $mid - $outer.Left - $pad
            $rightW = $outer.Right - $pad - $mid
            $innerH = $outer.Height - $pad * 2
            if ($leftW -gt 0 -and $rightW -gt 0 -and $innerH -gt 0) {
                $left = New-Object System.Drawing.Rectangle(($outer.Left + $pad), ($outer.Top + $pad), $leftW, $innerH)
                $right = New-Object System.Drawing.Rectangle($mid, ($outer.Top + $pad), $rightW, $innerH)
                $b1 = New-Object System.Drawing.SolidBrush($blue)
                $g.FillRectangle($b1, $left)
                $b1.Dispose()
                $b2 = New-Object System.Drawing.SolidBrush($dark)
                $g.FillRectangle($b2, $right)
                $b2.Dispose()
            }
        }
        finally {
            $g.Dispose()
        }
        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose()
        $result += , @($size, $ms.ToArray())
        $ms.Dispose()
    }
    return $result
}

$frames = New-IconBitmaps
$count = $frames.Count

$fs = [System.IO.File]::Create($Out)
$bw = New-Object System.IO.BinaryWriter($fs)
try {
    $bw.Write([UInt16]0)          # reserved
    $bw.Write([UInt16]1)          # type = icon
    $bw.Write([UInt16]$count)     # count

    $offset = 6 + 16 * $count
    foreach ($f in $frames) {
        $s = [int]$f[0]
        $data = [byte[]]$f[1]
        $dim = 0
        if ($s -lt 256) { $dim = $s }
        $bw.Write([byte]$dim)
        $bw.Write([byte]$dim)
        $bw.Write([byte]0)        # color count
        $bw.Write([byte]0)        # reserved
        $bw.Write([UInt16]1)      # planes
        $bw.Write([UInt16]32)     # bpp
        $bw.Write([UInt32]$data.Length)
        $bw.Write([UInt32]$offset)
        $offset += $data.Length
    }
    foreach ($f in $frames) {
        $bw.Write([byte[]]$f[1])
    }
}
finally {
    $bw.Dispose()
    $fs.Dispose()
}

Write-Host ("图标已生成: " + $Out)
