param(
    [string]$OutputDir = ''
)
$ErrorActionPreference = 'Stop'

# Crea il runtime LibreTranslate/Argos senza scaricare i modelli.
# Usato dalla build delle edizioni portable: i modelli vengono inclusi a parte.

$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $root 'dist\runtime\LibreTranslate'
}

$python = Get-Command python -ErrorAction SilentlyContinue
if (-not $python) {
    throw "Python 3 non trovato. Installa Python 3.10 o successivo e riprova."
}

$pythonExe = Join-Path $OutputDir 'Scripts\python.exe'
$pipExe = Join-Path $OutputDir 'Scripts\pip.exe'
$ltExe = Join-Path $OutputDir 'Scripts\libretranslate.exe'

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
if (-not (Test-Path -LiteralPath $pythonExe)) {
    Write-Host "Creazione venv portable in: $OutputDir"
    & python -m venv $OutputDir
    if ($LASTEXITCODE -ne 0) { throw 'Creazione venv non riuscita.' }
}

if (-not (Test-Path -LiteralPath $ltExe)) {
    Write-Host 'Installazione di LibreTranslate nel runtime portable (senza modelli)...'
    & $pythonExe -m pip install --upgrade pip
    if ($LASTEXITCODE -ne 0) { throw 'Aggiornamento pip non riuscito.' }
    & $pipExe install libretranslate
    if ($LASTEXITCODE -ne 0) { throw 'Installazione LibreTranslate non riuscita.' }
}

if (-not (Test-Path -LiteralPath $ltExe)) {
    throw "libretranslate.exe non trovato in $OutputDir"
}
[System.IO.File]::WriteAllText(
    (Join-Path $OutputDir '.gtit-runtime-ready'),
    "LibreTranslate runtime per GameTranslator IT`n")
Write-Host "Runtime pronto: $OutputDir"
