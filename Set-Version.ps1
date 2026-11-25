param(
    [Parameter(Mandatory = $true)]
    [string]$Version
)
$ErrorActionPreference = 'Stop'

if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "La versione deve essere nel formato X.Y.Z (es. 1.0.1). Valore ricevuto: $Version"
}

$root = $PSScriptRoot
$propsPath = Join-Path $root 'Directory.Build.props'
$issPath = Join-Path $root 'installer\InnoSetup\GameTranslatorIT.iss'
$manifestPath = Join-Path $root 'installer\MSIX\Package.appxmanifest'
$readmePath = Join-Path $root 'README.md'

$utf8NoBom = New-Object System.Text.UTF8Encoding($false)

function Replace-FileText([string]$path, [string]$pattern, [string]$replacement) {
    $text = [System.IO.File]::ReadAllText($path, [System.Text.Encoding]::UTF8)
    if ($text -notmatch $pattern) { throw "Modello non trovato in $path : $pattern" }
    [System.IO.File]::WriteAllText($path, [regex]::Replace($text, $pattern, $replacement), $utf8NoBom)
}

Replace-FileText $propsPath '<Version>\d+\.\d+\.\d+</Version>' "<Version>$Version</Version>"
Replace-FileText $issPath '#define MyAppVersion "\d+\.\d+\.\d+"' "#define MyAppVersion `"$Version`""

$msixVersion = $Version
if (($msixVersion -split '\.').Count -eq 3) { $msixVersion += '.0' }
Replace-FileText $manifestPath '(<Identity\b[^>]*\bVersion=")\d+\.\d+\.\d+\.\d+(")' ('${1}' + $msixVersion + '${2}')

Replace-FileText $readmePath 'GameTranslatorIT-Setup-\d+\.\d+\.\d+\.exe' "GameTranslatorIT-Setup-$Version.exe"

Write-Host "Versione impostata: $Version (MSIX: $msixVersion)"
Write-Host "Ora esegui: .\BuildRelease.ps1 -Version $Version"
