Add-Type -AssemblyName System.Drawing

$pngPath = 'F:\Github Projects\RansomGuard\Assets\Icons\RansomGuard.png'
$icoPath = 'F:\Github Projects\RansomGuard\Assets\Icons\RansomGuard.ico'

$png = [System.Drawing.Image]::FromFile($pngPath)
$sizes = @(16, 24, 32, 48, 64, 128, 256)

$ms = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($ms)

# ICO header
$bw.Write([uint16]0)
$bw.Write([uint16]1)
$bw.Write([uint16]$sizes.Count)

$imageDataList = @()
foreach ($size in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.DrawImage($png, 0, 0, $size, $size)
    $g.Dispose()

    $imgMs = New-Object System.IO.MemoryStream
    $bmp.Save($imgMs, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    $imageDataList += @{ Size = $size; Data = $imgMs.ToArray() }
    $imgMs.Dispose()
}

# Directory entries (each 16 bytes)
$offset = 6 + ($sizes.Count * 16)
foreach ($img in $imageDataList) {
    $s = if ($img.Size -eq 256) { [byte]0 } else { [byte]$img.Size }
    $bw.Write($s)
    $bw.Write($s)
    $bw.Write([byte]0)
    $bw.Write([byte]0)
    $bw.Write([uint16]1)
    $bw.Write([uint16]32)
    $bw.Write([uint32]$img.Data.Length)
    $bw.Write([uint32]$offset)
    $offset += $img.Data.Length
}

# Image data
foreach ($img in $imageDataList) {
    $bw.Write($img.Data)
}

$bw.Flush()
[System.IO.File]::WriteAllBytes($icoPath, $ms.ToArray())
$png.Dispose()

Write-Host "ICO created successfully at $icoPath" -ForegroundColor Green
Write-Host "File size: $((Get-Item $icoPath).Length) bytes"
