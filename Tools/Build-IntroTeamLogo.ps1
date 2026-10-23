# Instala um PNG final de equipa no espaço reservado da introdução.
param(
    [Parameter(Mandatory = $true)]
    [string] $SourcePath
)

$ErrorActionPreference = 'Stop'
$source = (Resolve-Path -LiteralPath $SourcePath).Path
if ([System.IO.Path]::GetExtension($source) -ine '.png') {
    throw 'O logo deve ser um ficheiro PNG.'
}
$projectRoot = Split-Path -Parent $PSScriptRoot
$output = Join-Path $projectRoot 'Assets/_Game/Resources/UI/Menu/team_logo.png'
if ($source -ine $output) {
    Copy-Item -LiteralPath $source -Destination $output -Force
}
Write-Output "Logo de equipa instalado: $output"
