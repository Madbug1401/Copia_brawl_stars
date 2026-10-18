# Recria o mapa equiretangular da intro sem dependências de runtime.
# Natural Earth 1:50m, domínio público: https://www.naturalearthdata.com/about/terms-of-use/
param([string]$GeoJsonPath = '', [switch]$Regional)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$projectRoot = Split-Path -Parent $PSScriptRoot
$cacheFolder = Join-Path $projectRoot 'Temp/intro-map'
[void][System.IO.Directory]::CreateDirectory($cacheFolder)
if (!$GeoJsonPath) {
    $GeoJsonPath = Join-Path $cacheFolder 'ne_50m_land.geojson'
    if (!(Test-Path -LiteralPath $GeoJsonPath)) {
        Invoke-WebRequest 'https://raw.githubusercontent.com/nvkelso/natural-earth-vector/master/geojson/ne_50m_land.geojson' -OutFile $GeoJsonPath
    }
}
$land = Get-Content -LiteralPath $GeoJsonPath -Raw | ConvertFrom-Json
$width = 4096
$west = -180; $east = 180; $south = -90; $north = 90
$outputName = 'bg_world.png'
if ($Regional) {
    $west = -31; $east = -14; $south = 9; $north = 24
    $outputName = 'bg_cape_verde.png'
}
$height = [int]($width * ($north - $south) / ($east - $west))
$bitmap = [System.Drawing.Bitmap]::new($width, $height)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$ocean = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
    [System.Drawing.Point]::new(0, 0), [System.Drawing.Point]::new(0, $height),
    [System.Drawing.Color]::FromArgb(7, 28, 47), [System.Drawing.Color]::FromArgb(2, 12, 24))
$fill = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(36, 74, 88))
$coast = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(135, 103, 176, 173), 1.2)
$shelf = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(60, 17, 112, 123), 7)
$grid = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(28, 85, 162, 179), 1)
try {
    $graphics.FillRectangle($ocean, 0, 0, $width, $height)
    for ($longitude = -180; $longitude -le 180; $longitude += 15) {
        $x = [single](($longitude - $west) / ($east - $west) * $width)
        $graphics.DrawLine($grid, $x, 0, $x, $height)
    }
    for ($latitude = -90; $latitude -le 90; $latitude += 15) {
        $y = [single](($north - $latitude) / ($north - $south) * $height)
        $graphics.DrawLine($grid, 0, $y, $width, $y)
    }
    foreach ($feature in $land.features) {
        # Uma lista explícita preserva as três dimensões dos arrays GeoJSON no PowerShell.
        $polygons = [System.Collections.Generic.List[object]]::new()
        if ($feature.geometry.type -eq 'Polygon') {
            $polygons.Add($feature.geometry.coordinates)
        } else {
            foreach ($part in $feature.geometry.coordinates) { $polygons.Add($part) }
        }
        foreach ($polygon in $polygons) {
            $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
            try {
                foreach ($ring in $polygon) {
                    $points = [System.Drawing.PointF[]]@($ring | ForEach-Object {
                        [System.Drawing.PointF]::new([single](($_[0] - $west) / ($east - $west) * $width), [single](($north - $_[1]) / ($north - $south) * $height))
                    })
                    if ($points.Count -ge 3) { $path.AddPolygon($points) }
                }
                $graphics.DrawPath($shelf, $path)
                $graphics.FillPath($fill, $path)
                $graphics.DrawPath($coast, $path)
            } finally { $path.Dispose() }
        }
    }
    $output = Join-Path $projectRoot "Assets/_Game/Resources/UI/Menu/$outputName"
    $bitmap.Save($output, [System.Drawing.Imaging.ImageFormat]::Png)
    Write-Output "Mapa mundial criado: $output"
} finally {
    $grid.Dispose(); $shelf.Dispose(); $coast.Dispose(); $fill.Dispose(); $ocean.Dispose()
    $graphics.Dispose(); $bitmap.Dispose()
}
