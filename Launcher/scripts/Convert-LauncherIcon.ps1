param([string]$Source = "$PSScriptRoot/../src/Shinshi.Launcher.UI/Assets/launcher.png",
      [string]$Destination = "$PSScriptRoot/../src/Shinshi.Launcher.UI/Assets/launcher.ico")
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$sourceImage = [Drawing.Image]::FromFile([IO.Path]::GetFullPath($Source))
$images = @()
try {
    foreach ($size in @(16, 24, 32, 48, 64, 128, 256)) {
        $bitmap = [Drawing.Bitmap]::new($size, $size)
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        $memory = [IO.MemoryStream]::new()
        try {
            $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.DrawImage($sourceImage, 0, 0, $size, $size)
            $bitmap.Save($memory, [Drawing.Imaging.ImageFormat]::Png)
            $images += @{ Size = $size; Bytes = $memory.ToArray() }
        } finally { $graphics.Dispose(); $bitmap.Dispose(); $memory.Dispose() }
    }
    $stream = [IO.File]::Create([IO.Path]::GetFullPath($Destination))
    $writer = [IO.BinaryWriter]::new($stream)
    try {
        $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$images.Count)
        $offset = 6 + 16 * $images.Count
        foreach ($item in $images) {
            $dimension = $item.Size % 256
            $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
            $writer.Write([byte]0); $writer.Write([byte]0)
            $writer.Write([uint16]1); $writer.Write([uint16]32)
            $writer.Write([uint32]$item.Bytes.Length); $writer.Write([uint32]$offset)
            $offset += $item.Bytes.Length
        }
        foreach ($item in $images) { $writer.Write([byte[]]$item.Bytes) }
    } finally { $writer.Dispose(); $stream.Dispose() }
} finally { $sourceImage.Dispose() }
