<#
.SYNOPSIS
    Draws the application icon (three stacked cards, as in the window header) into an .ico file.

.DESCRIPTION
    Renders the header mark of src/Gui/ShellView.xaml at 16, 20, 24, 32, 40, 48, 64 and 256 pixels and writes them as
    PNG frames of one .ico file. The executable embeds it (src/Gui/Gui.csproj), and the tray icon, the window and the
    setup program's shortcuts show it. Run it again only when the mark changes.

.PARAMETER Path
    The .ico file to write. Defaults to src/Gui/Resources/app.ico in this repository.

.EXAMPLE
    .\scripts\windows\New-AppIcon.ps1
#>
[CmdletBinding()]
param(
    [string]$Path
)

$ErrorActionPreference = 'Stop'
# Windows PowerShell does not set $PSScriptRoot while binding parameter defaults.
if (-not $Path) { $Path = Join-Path $PSScriptRoot '..\..\src\Gui\Resources\app.ico' }
Add-Type -AssemblyName System.Drawing

function New-RoundedRectangle([single]$x, [single]$y, [single]$width, [single]$height, [single]$radius) {
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $d = $radius * 2
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $width - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $width - $d, $y + $height - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $height - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-Frame([int]$size) {
    $bitmap = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.Clear([System.Drawing.Color]::Transparent)
        # The header mark is 22 x 23 units; it is centred in 24 x 24 and scaled to the frame.
        $scale = $size / 24.0
        $graphics.ScaleTransform($scale, $scale)
        $graphics.TranslateTransform(1, 0.5)
        $stroke = [Math]::Max(1.5, 1.0 / $scale)
        $hint = [System.Drawing.Color]::FromArgb(0x8A, 0x95, 0xA1)
        $surface = [System.Drawing.Color]::FromArgb(0xFC, 0xFD, 0xFD)
        $accent = [System.Drawing.Color]::FromArgb(0x2B, 0x43, 0xBF)
        $accentSoft = [System.Drawing.Color]::FromArgb(0xE5, 0xE9, 0xFA)
        $cards = @(
            @{ X = 7.0; Y = 0.0; Border = $hint; Fill = $surface },
            @{ X = 3.5; Y = 2.5; Border = $hint; Fill = $surface },
            @{ X = 0.0; Y = 5.0; Border = $accent; Fill = $accentSoft }
        )
        foreach ($card in $cards) {
            $inset = $stroke / 2
            $shape = New-RoundedRectangle ($card.X + $inset) ($card.Y + $inset) (14 - $stroke) (17 - $stroke) 2
            $fill = [System.Drawing.SolidBrush]::new($card.Fill)
            $pen = [System.Drawing.Pen]::new($card.Border, $stroke)
            $graphics.FillPath($fill, $shape)
            $graphics.DrawPath($pen, $shape)
            $fill.Dispose(); $pen.Dispose(); $shape.Dispose()
        }
        $stream = [System.IO.MemoryStream]::new()
        $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
        return , $stream.ToArray()
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 256
$frames = foreach ($size in $sizes) { , (New-Frame $size) }

$output = [System.IO.MemoryStream]::new()
$writer = [System.IO.BinaryWriter]::new($output)
# ICONDIR, then one ICONDIRENTRY per frame, then the PNG data.
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $size = $sizes[$i]
    $edge = if ($size -ge 256) { 0 } else { $size }
    $writer.Write([byte]$edge); $writer.Write([byte]$edge); $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([uint16]1); $writer.Write([uint16]32)
    $writer.Write([uint32]$frames[$i].Length); $writer.Write([uint32]$offset)
    $offset += $frames[$i].Length
}
foreach ($frame in $frames) { $writer.Write($frame) }
$writer.Flush()

$target = [System.IO.Path]::GetFullPath($Path)
[System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($target)) | Out-Null
[System.IO.File]::WriteAllBytes($target, $output.ToArray())
Write-Host "Wrote $target ($($output.Length) bytes)"
