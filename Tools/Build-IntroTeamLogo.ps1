# Identidade provisória desenhada com formas vetoriais, substituível por team_logo.png.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$projectRoot = Split-Path -Parent $PSScriptRoot
$bitmap = [System.Drawing.Bitmap]::new(1024, 768)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
$cyan = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(64, 224, 209))
$white = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(240, 247, 255))
$muted = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(142, 175, 192))
$pen = [System.Drawing.Pen]::new($cyan, 3)
$title = [System.Drawing.Font]::new('Bahnschrift', 70, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
$subtitle = [System.Drawing.Font]::new('Bahnschrift', 24, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
$format = [System.Drawing.StringFormat]::new()
$format.Alignment = [System.Drawing.StringAlignment]::Center
try {
    $graphics.Clear([System.Drawing.Color]::Transparent)
    $graphics.DrawArc($pen, 352, 90, 320, 320, 205, 135)
    $graphics.DrawArc($pen, 352, 90, 320, 320, 25, 135)
    $left = [System.Drawing.PointF[]]@(
        [System.Drawing.PointF]::new(512, 132), [System.Drawing.PointF]::new(392, 340),
        [System.Drawing.PointF]::new(440, 340), [System.Drawing.PointF]::new(536, 174))
    $right = [System.Drawing.PointF[]]@(
        [System.Drawing.PointF]::new(546, 190), [System.Drawing.PointF]::new(632, 340),
        [System.Drawing.PointF]::new(584, 340), [System.Drawing.PointF]::new(522, 232))
    $graphics.FillPolygon($cyan, $left)
    $graphics.FillPolygon($white, $right)
    $graphics.FillRectangle($cyan, 474, 301, 76, 12)
    $graphics.FillRectangle($white, 478, 368, 68, 3)
    $graphics.DrawString('ATLANTIC', $title, $white, [System.Drawing.RectangleF]::new(0, 458, 1024, 100), $format)
    $graphics.DrawString('S T U D I O', $subtitle, $muted, [System.Drawing.RectangleF]::new(0, 563, 1024, 50), $format)
    $output = Join-Path $projectRoot 'Assets/_Game/Resources/UI/Menu/team_logo.png'
    $bitmap.Save($output, [System.Drawing.Imaging.ImageFormat]::Png)
    Write-Output "Logo provisório criado: $output"
} finally {
    $format.Dispose(); $subtitle.Dispose(); $title.Dispose(); $pen.Dispose()
    $muted.Dispose(); $white.Dispose(); $cyan.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
}
