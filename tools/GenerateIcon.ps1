$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assetDirectory = Join-Path $PSScriptRoot '../Assets'
New-Item -ItemType Directory -Force -Path $assetDirectory | Out-Null
$pixels = @(
'................',
'.....SSSSSS.....',
'...SSWWWWWWSS...',
'..SWW......WWS..',
'..WW........WW..',
'.SW..........WS.',
'.WW..........WW.',
'.WW..........WW.',
'.WW..........WW.',
'.WWSS......SSWW.',
'.WWWW......WWWW.',
'.WGGW......WGGW.',
'.WGGW......WGRW.',
'.WWWW......WWWW.',
'..SS........SS..',
'................'
)
$colors = @{ W = [Drawing.Color]::FromArgb(244,250,255); S = [Drawing.Color]::FromArgb(85,112,126); G = [Drawing.Color]::FromArgb(141,180,193); R = [Drawing.Color]::FromArgb(240,61,76) }
$frames = [Collections.Generic.List[object]]::new()
foreach ($size in @(16,24,32,48,64,128,256)) {
    $bitmap = [Drawing.Bitmap]::new($size,$size)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $graphics.Clear([Drawing.Color]::Transparent)
    for ($y=0;$y -lt 16;$y++) { for ($x=0;$x -lt 16;$x++) {
        $key = [string]$pixels[$y][$x]
        if ($colors.ContainsKey($key)) {
            $brush = [Drawing.SolidBrush]::new($colors[$key])
            $left = [int][Math]::Round($x*$size/16.0); $top = [int][Math]::Round($y*$size/16.0)
            $right = [int][Math]::Round(($x+1)*$size/16.0); $bottom = [int][Math]::Round(($y+1)*$size/16.0)
            $graphics.FillRectangle($brush,$left,$top,$right-$left,$bottom-$top)
            $brush.Dispose()
        }
    } }
    $stream = [IO.MemoryStream]::new()
    $bitmap.Save($stream,[Drawing.Imaging.ImageFormat]::Png)
    $frames.Add(@{ Size=$size; Bytes=$stream.ToArray() })
    if ($size -eq 256) { $bitmap.Save((Join-Path $assetDirectory 'headphones.png'),[Drawing.Imaging.ImageFormat]::Png) }
    $stream.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
}
$file = [IO.File]::Create((Join-Path $assetDirectory 'headphones.ico'))
$writer = [IO.BinaryWriter]::new($file)
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$frames.Count)
$offset = 6 + 16*$frames.Count
foreach ($frame in $frames) {
    $dimension = if ($frame.Size -eq 256) { 0 } else { $frame.Size }
    $writer.Write([byte]$dimension); $writer.Write([byte]$dimension); $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$frame.Bytes.Length); $writer.Write([uint32]$offset)
    $offset += $frame.Bytes.Length
}
foreach ($frame in $frames) { $writer.Write([byte[]]$frame.Bytes) }
$writer.Dispose(); $file.Dispose()
