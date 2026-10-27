$ErrorActionPreference = 'Stop'

# Generates the application icon.
#
# The mark is the ledger: three ruled lines, right-aligned because the product is right-to-left first,
# on the petrol ground of the theme. It is drawn rather than sourced so it stays crisp from 16 px in the
# taskbar to 256 px in the installer, and so it is exactly the brand colour.

Add-Type -AssemblyName System.Drawing

$assets = 'D:\Coding\UA Retail Store\src\StoreFlow.Desktop\Assets'
if (-not (Test-Path $assets)) { [System.IO.Directory]::CreateDirectory($assets) | Out-Null }

$Ink      = [System.Drawing.Color]::FromArgb(255, 15, 94, 99)    # petrol, the theme accent
$InkDeep  = [System.Drawing.Color]::FromArgb(255, 9, 61, 65)     # petrol deeper
$Paper    = [System.Drawing.Color]::FromArgb(255, 247, 245, 241)  # paper canvas
$Gold     = [System.Drawing.Color]::FromArgb(255, 178, 107, 0)   # the seal warning colour

function New-RoundedPath([System.Drawing.RectangleF]$rect, [single]$radius) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = [single]($radius * 2)

    $path.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
    $path.AddArc($rect.Right - $d, $rect.Y, $d, $d, 270, 90)
    $path.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90)
    $path.AddArc($rect.X, $rect.Bottom - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-IconBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    $s = [single]$size
    $margin = [single]($s * 0.06)
    $plate = New-Object System.Drawing.RectangleF $margin, $margin, ($s - 2 * $margin), ($s - 2 * $margin)
    $radius = [single]($s * 0.22)

    # The ground: petrol, with a deeper band across the foot so the mark reads as a solid object
    # rather than a flat sticker.
    $path = New-RoundedPath $plate $radius
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $plate, $Ink, $InkDeep, 90.0
    $g.FillPath($brush, $path)

    # The ledger: three ruled lines, right-aligned.
    $barHeight = [single]([Math]::Max(2.0, $s * 0.085))
    $right = [single]($plate.Right - $s * 0.16)
    $left = [single]($plate.Left + $s * 0.16)

    $lines = @(
        @{ Width = 1.00; Y = 0.30; Colour = $Paper },
        @{ Width = 0.66; Y = 0.47; Colour = $Gold },
        @{ Width = 0.40; Y = 0.64; Colour = $Paper }
    )

    foreach ($line in $lines) {
        $width = [single](($right - $left) * $line.Width)
        $y = [single]($plate.Top + ($plate.Height * $line.Y))
        $rect = New-Object System.Drawing.RectangleF ([single]($right - $width)), $y, $width, $barHeight
        $pen = New-Object System.Drawing.SolidBrush $line.Colour
        $g.FillRectangle($pen, $rect)
        $pen.Dispose()
    }

    $brush.Dispose()
    $path.Dispose()
    $g.Dispose()
    return $bmp
}

$sizes = @(16, 24, 32, 48, 64, 128, 256)

$pngs = @()
foreach ($size in $sizes) {
    $bmp = New-IconBitmap $size
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    $pngs += ,@{ Size = $size; Data = $ms.ToArray() }
    $ms.Dispose()
    Write-Output ("  rendered ${size}px (" + $pngs[-1].Data.Length + ' bytes)')
}

# Assemble the icon container: a header, one directory entry per image, then the images themselves.
$stream = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter $stream

$writer.Write([UInt16]0)      # reserved
$writer.Write([UInt16]1)      # type: icon
$writer.Write([UInt16]$sizes.Count)

$offset = 6 + (16 * $sizes.Count)

for ($i = 0; $i -lt $sizes.Count; $i++) {
    $entry = $pngs[$i]
    $dimension = if ($entry.Size -ge 256) { [byte]0 } else { [byte]$entry.Size }

    $writer.Write($dimension)
    $writer.Write($dimension)
    $writer.Write([byte]0)     # palette size
    $writer.Write([byte]0)     # reserved
    $writer.Write([UInt16]1)   # colour planes
    $writer.Write([UInt16]32)  # bits per pixel
    $writer.Write([UInt32]$entry.Data.Length)
    $writer.Write([UInt32]$offset)

    $offset += $entry.Data.Length
}

foreach ($entry in $pngs) {
    $writer.Write($entry.Data)
}

$writer.Flush()
$bytes = $stream.ToArray()
$writer.Dispose()
$stream.Dispose()

$icoPath = Join-Path $assets 'StoreFlow.ico'
[System.IO.File]::WriteAllBytes($icoPath, $bytes)
Write-Output ("wrote " + $icoPath + " (" + $bytes.Length + ' bytes)')

# A 256 px PNG is useful on its own for the installer and for documentation.
$large = New-IconBitmap 256
$pngPath = Join-Path $assets 'StoreFlow-256.png'
$large.Save($pngPath, [System.Drawing.Imaging.ImageFormat]::Png)
$large.Dispose()
Write-Output ("wrote " + $pngPath)
