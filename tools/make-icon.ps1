param(
    [string]$OutPath
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

if (-not $OutPath) {
    $OutPath = Join-Path (Split-Path -Parent $PSScriptRoot) 'src\OpenWindows.App\Assets\app.ico'
}

$outDir = Split-Path -Parent $OutPath
if (-not (Test-Path -LiteralPath $outDir)) {
    New-Item -ItemType Directory -Force -Path $outDir | Out-Null
}

function New-RoundedRectPath {
    param(
        [float]$X,
        [float]$Y,
        [float]$W,
        [float]$H,
        [float]$R
    )
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $d = [float]($R * 2.0)
    $path.AddArc($X, $Y, $d, $d, [float]180.0, [float]90.0)
    $path.AddArc([float]($X + $W - $d), $Y, $d, $d, [float]270.0, [float]90.0)
    $path.AddArc([float]($X + $W - $d), [float]($Y + $H - $d), $d, $d, [float]0.0, [float]90.0)
    $path.AddArc($X, [float]($Y + $H - $d), $d, $d, [float]90.0, [float]90.0)
    $path.CloseFigure()
    Write-Output $path
}

function New-IconBitmap {
    param(
        [int]$Size,
        [double]$Stroke,
        [double]$OuterRatio,
        [double]$DotFactor,
        [bool]$Needle,
        [double]$NeedleLenFactor,
        [double]$NeedleWidthFactor
    )
    $bmp = [System.Drawing.Bitmap]::new($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $path = $null
    $gradient = $null
    $pen = $null
    $white = $null
    $needlePen = $null
    try {
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $g.Clear([System.Drawing.Color]::Transparent)

        $s = [float]$Size
        $margin = [float]($s * 0.04)
        $side = [float]($s - 2.0 * $margin)
        $radius = [float]($s * 0.22)
        $path = New-RoundedRectPath -X $margin -Y $margin -W $side -H $side -R $radius

        $gradRect = [System.Drawing.RectangleF]::new($margin, $margin, $side, $side)
        $gradient = [System.Drawing.Drawing2D.LinearGradientBrush]::new($gradRect, [System.Drawing.Color]::FromArgb(255, 79, 107, 255), [System.Drawing.Color]::FromArgb(255, 124, 58, 237), [float]90.0)
        $g.FillPath($gradient, $path)

        $cx = [float]($s / 2.0)
        $cy = [float]($s / 2.0)
        $strokeW = [float]$Stroke
        $arcR = [float]($s * $OuterRatio - $strokeW / 2.0)

        $pen = [System.Drawing.Pen]::new([System.Drawing.Color]::White, $strokeW)
        $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
        $g.DrawArc($pen, [float]($cx - $arcR), [float]($cy - $arcR), [float]($arcR * 2.0), [float]($arcR * 2.0), [float]135.0, [float]270.0)

        if ($Needle) {
            $theta = -45.0 * [math]::PI / 180.0
            $nx = [float]($cx + [math]::Cos($theta) * $arcR * $NeedleLenFactor)
            $ny = [float]($cy + [math]::Sin($theta) * $arcR * $NeedleLenFactor)
            $needlePen = [System.Drawing.Pen]::new([System.Drawing.Color]::White, [float]($strokeW * $NeedleWidthFactor))
            $needlePen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
            $needlePen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
            $g.DrawLine($needlePen, $cx, $cy, $nx, $ny)
        }

        $dotR = [float]($strokeW * $DotFactor)
        if ($dotR -gt 0.0) {
            $white = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::White)
            $g.FillEllipse($white, [float]($cx - $dotR), [float]($cy - $dotR), [float]($dotR * 2.0), [float]($dotR * 2.0))
        }
    }
    finally {
        if ($needlePen) { $needlePen.Dispose() }
        if ($white) { $white.Dispose() }
        if ($pen) { $pen.Dispose() }
        if ($gradient) { $gradient.Dispose() }
        if ($path) { $path.Dispose() }
        $g.Dispose()
    }
    Write-Output $bmp
}

function ConvertTo-DibBytes {
    param([System.Drawing.Bitmap]$Bitmap)
    $w = $Bitmap.Width
    $h = $Bitmap.Height
    $rect = [System.Drawing.Rectangle]::new(0, 0, $w, $h)
    $data = $Bitmap.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        $stride = $data.Stride
        $flip = $stride -gt 0
        if ($stride -lt 0) { $stride = -$stride }
        $pixels = [byte[]]::new($stride * $h)
        [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $pixels, 0, $pixels.Length)
    }
    finally {
        $Bitmap.UnlockBits($data)
    }
    $maskStride = [int]([math]::Ceiling($w / 32.0) * 4)
    $mask = [byte[]]::new($maskStride * $h)

    $ms = [System.IO.MemoryStream]::new()
    $bw = [System.IO.BinaryWriter]::new($ms)
    try {
        $bw.Write([uint32]40)
        $bw.Write([int32]$w)
        $bw.Write([int32]($h * 2))
        $bw.Write([uint16]1)
        $bw.Write([uint16]32)
        $bw.Write([uint32]0)
        $bw.Write([uint32]($stride * $h))
        $bw.Write([int32]0)
        $bw.Write([int32]0)
        $bw.Write([uint32]0)
        $bw.Write([uint32]0)
        if ($flip) {
            for ($y = $h - 1; $y -ge 0; $y--) {
                $bw.Write($pixels, $y * $stride, $stride)
            }
        }
        else {
            for ($y = 0; $y -lt $h; $y++) {
                $bw.Write($pixels, $y * $stride, $stride)
            }
        }
        $bw.Write($mask, 0, $mask.Length)
        $bw.Flush()
        $bytes = $ms.ToArray()
    }
    finally {
        $bw.Dispose()
        $ms.Dispose()
    }
    Write-Output -NoEnumerate $bytes
}

$sizeSpecs = @(
    [pscustomobject]@{ Size = 16;  Stroke = 2.2;  Outer = 0.34;  Dot = 0.60; Needle = $false; NeedleLen = 0.0;  NeedleWidth = 0.0 },
    [pscustomobject]@{ Size = 24;  Stroke = 3.4;  Outer = 0.32;  Dot = 0.50; Needle = $false; NeedleLen = 0.0;  NeedleWidth = 0.0 },
    [pscustomobject]@{ Size = 32;  Stroke = 4.6;  Outer = 0.305; Dot = 0.48; Needle = $true;  NeedleLen = 0.78; NeedleWidth = 0.45 },
    [pscustomobject]@{ Size = 48;  Stroke = 6.5;  Outer = 0.30;  Dot = 0.52; Needle = $true;  NeedleLen = 0.76; NeedleWidth = 0.46 },
    [pscustomobject]@{ Size = 64;  Stroke = 8.5;  Outer = 0.30;  Dot = 0.55; Needle = $true;  NeedleLen = 0.75; NeedleWidth = 0.46 },
    [pscustomobject]@{ Size = 128; Stroke = 15.0; Outer = 0.30;  Dot = 0.60; Needle = $true;  NeedleLen = 0.74; NeedleWidth = 0.47 },
    [pscustomobject]@{ Size = 256; Stroke = 24.0; Outer = 0.30;  Dot = 0.60; Needle = $true;  NeedleLen = 0.73; NeedleWidth = 0.46 }
)

$images = @()
foreach ($spec in $sizeSpecs) {
    $bmp = New-IconBitmap -Size $spec.Size -Stroke $spec.Stroke -OuterRatio $spec.Outer -DotFactor $spec.Dot -Needle $spec.Needle -NeedleLenFactor $spec.NeedleLen -NeedleWidthFactor $spec.NeedleWidth
    try {
        if ($spec.Size -ge 256) {
            $pngStream = [System.IO.MemoryStream]::new()
            $bmp.Save($pngStream, [System.Drawing.Imaging.ImageFormat]::Png)
            $dib = $pngStream.ToArray()
            $pngStream.Dispose()
        }
        else {
            $dib = ConvertTo-DibBytes -Bitmap $bmp
        }
    }
    finally {
        $bmp.Dispose()
    }
    $images += [pscustomobject]@{ Size = $spec.Size; Bytes = $dib }
}

$stream = [System.IO.MemoryStream]::new()
$writer = [System.IO.BinaryWriter]::new($stream)
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$images.Count)
    $offset = 6 + 16 * $images.Count
    foreach ($img in $images) {
        $dim = [byte]($img.Size % 256)
        $writer.Write($dim)
        $writer.Write($dim)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$img.Bytes.Length)
        $writer.Write([uint32]$offset)
        $offset += $img.Bytes.Length
    }
    foreach ($img in $images) {
        $writer.Write($img.Bytes, 0, $img.Bytes.Length)
    }
    $writer.Flush()
    $bytes = $stream.ToArray()
}
finally {
    $writer.Dispose()
    $stream.Dispose()
}

[System.IO.File]::WriteAllBytes($OutPath, $bytes)
Write-Output ('Created {0} ({1} bytes, {2} entries)' -f $OutPath, $bytes.Length, $images.Count)
