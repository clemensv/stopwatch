Add-Type -AssemblyName System.Drawing

$logoScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$logoProjectRoot = (Resolve-Path -LiteralPath (Join-Path $logoScriptRoot '..\..')).Path
$logoSourcePath = Join-Path $logoScriptRoot 'project-logo-source.png'
$logoPngRoot = Join-Path $logoScriptRoot 'png'
$logoHeaderPath = Join-Path $logoProjectRoot 'StopwatchOverlay\project-logo-24.png'
$logoIconPath = Join-Path $logoProjectRoot 'StopwatchOverlay\project-logo.ico'
New-Item -ItemType Directory -Path $logoPngRoot -Force | Out-Null

function New-ProjectLogoPngBytes(
    [System.Drawing.Bitmap] $source,
    [int] $size) {
    if ($size -eq $source.Width -and $size -eq $source.Height) {
        return [System.IO.File]::ReadAllBytes($logoSourcePath)
    }

    $bitmap = [System.Drawing.Bitmap]::new(
        $size,
        $size,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.CompositingMode =
                [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
            $graphics.CompositingQuality =
                [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
            $graphics.InterpolationMode =
                [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
            $graphics.PixelOffsetMode =
                [System.Drawing.Drawing2D.PixelOffsetMode]::Half
            $graphics.SmoothingMode =
                [System.Drawing.Drawing2D.SmoothingMode]::None
            $graphics.Clear([System.Drawing.Color]::Transparent)

            $imageAttributes = [System.Drawing.Imaging.ImageAttributes]::new()
            try {
                $imageAttributes.SetWrapMode(
                    [System.Drawing.Drawing2D.WrapMode]::TileFlipXY)
                $graphics.DrawImage(
                    $source,
                    [System.Drawing.Rectangle]::new(0, 0, $size, $size),
                    0,
                    0,
                    $source.Width,
                    $source.Height,
                    [System.Drawing.GraphicsUnit]::Pixel,
                    $imageAttributes)
            }
            finally {
                $imageAttributes.Dispose()
            }
        }
        finally {
            $graphics.Dispose()
        }

        $stream = [System.IO.MemoryStream]::new()
        try {
            $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
            return $stream.ToArray()
        }
        finally {
            $stream.Dispose()
        }
    }
    finally {
        $bitmap.Dispose()
    }
}

$sourceLogo = [System.Drawing.Bitmap]::new($logoSourcePath)
try {
    if ($sourceLogo.Width -ne $sourceLogo.Height) {
        throw 'The canonical project logo must use a square canvas.'
    }

    $logoSizes = @(16, 24, 32, 48, 64, 128, 256, $sourceLogo.Width) |
        Select-Object -Unique
    $logoFrames = foreach ($logoSize in $logoSizes) {
        $pngBytes = New-ProjectLogoPngBytes $sourceLogo $logoSize
        $pngPath = Join-Path $logoPngRoot "project-logo-$logoSize.png"
        [System.IO.File]::WriteAllBytes($pngPath, $pngBytes)
        if ($logoSize -le 256) {
            [PSCustomObject]@{ Size = $logoSize; Bytes = $pngBytes }
        }
    }
}
finally {
    $sourceLogo.Dispose()
}

$logoHeaderFrame = $logoFrames | Where-Object Size -eq 24 | Select-Object -First 1
[System.IO.File]::WriteAllBytes($logoHeaderPath, [byte[]]$logoHeaderFrame.Bytes)

$iconStream = [System.IO.File]::Open(
    $logoIconPath,
    [System.IO.FileMode]::Create,
    [System.IO.FileAccess]::Write,
    [System.IO.FileShare]::None)
try {
    $writer = [System.IO.BinaryWriter]::new($iconStream)
    try {
        $writer.Write([uint16]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]$logoFrames.Count)

        $imageOffset = 6 + (16 * $logoFrames.Count)
        foreach ($frame in $logoFrames) {
            $dimension = if ($frame.Size -eq 256) { 0 } else { $frame.Size }
            $writer.Write([byte]$dimension)
            $writer.Write([byte]$dimension)
            $writer.Write([byte]0)
            $writer.Write([byte]0)
            $writer.Write([uint16]1)
            $writer.Write([uint16]32)
            $writer.Write([uint32]$frame.Bytes.Length)
            $writer.Write([uint32]$imageOffset)
            $imageOffset += $frame.Bytes.Length
        }

        foreach ($frame in $logoFrames) {
            $writer.Write([byte[]]$frame.Bytes)
        }
    }
    finally {
        $writer.Dispose()
    }
}
finally {
    $iconStream.Dispose()
}

Write-Output $logoSourcePath
Write-Output $logoHeaderPath
Write-Output $logoIconPath
