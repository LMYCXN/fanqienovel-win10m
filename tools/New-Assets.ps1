$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assetRoot = Join-Path (Split-Path -Parent $PSScriptRoot) 'src\FanqieReader\Assets'
[void][System.IO.Directory]::CreateDirectory($assetRoot)
$definitions = @(
    @{ Name = 'StoreLogo'; Width = 50; Height = 50 },
    @{ Name = 'Square44x44Logo'; Width = 44; Height = 44 },
    @{ Name = 'Square150x150Logo'; Width = 150; Height = 150 },
    @{ Name = 'SplashScreen'; Width = 620; Height = 300 }
)
foreach ($definition in $definitions) {
    $bitmap = New-Object System.Drawing.Bitmap($definition.Width, $definition.Height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $brush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
    $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(215, 90, 58), 2)
    try {
        $graphics.Clear([System.Drawing.Color]::FromArgb(215, 90, 58))
        $size = [Math]::Min($definition.Width, $definition.Height) * 0.6
        $left = ($definition.Width - $size) / 2
        $top = ($definition.Height - $size * 0.8) / 2
        $graphics.FillRectangle($brush, [single]$left, [single]$top, [single]$size, [single]($size * 0.8))
        $graphics.DrawLine($pen, [single]($left + $size / 2), [single]$top, [single]($left + $size / 2), [single]($top + $size * 0.8))
        $bitmap.Save((Join-Path $assetRoot ($definition.Name + '.png')), [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally { $pen.Dispose(); $brush.Dispose(); $graphics.Dispose(); $bitmap.Dispose() }
}
