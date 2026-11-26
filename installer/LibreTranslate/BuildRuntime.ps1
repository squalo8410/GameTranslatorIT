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

# I wrapper console generati da pip contengono il percorso assoluto della venv
# usata in build: copiarli in un'altra macchina non funzionerebbe. La portable
# avvia quindi direttamente python.exe con questo script e con PYTHONHOME
# puntato a una copia del runtime Python inclusa nella cartella Runtime.
$cfgPath = Join-Path $OutputDir 'pyvenv.cfg'
$homeMatch = [regex]::Match((Get-Content -LiteralPath $cfgPath -Raw), '(?m)^home\s*=\s*(.+?)\s*$')
if (-not $homeMatch.Success) {
    throw "Home Python non trovata in $cfgPath"
}
$pythonHomeSource = $homeMatch.Groups[1].Value.Trim()
if (-not (Test-Path -LiteralPath (Join-Path $pythonHomeSource 'python.exe'))) {
    throw "Runtime Python di base non trovato: $pythonHomeSource"
}
$pythonHomeTarget = Join-Path $OutputDir 'Python'
Write-Host "Copia runtime Python in $pythonHomeTarget"
if (-not (Test-Path -LiteralPath (Join-Path $pythonHomeTarget 'python.exe'))) {
    & robocopy.exe $pythonHomeSource $pythonHomeTarget /E /XD Doc include tcl Tools Scripts /NFL /NDL /NJH /NJS /NC /NS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Copia runtime Python non riuscita: $LASTEXITCODE" }
}

$launcherScript = Join-Path $OutputDir 'avvia-libretranslate.py'
[System.IO.File]::WriteAllText(
    $launcherScript,
    "from libretranslate.main import main`r`nif __name__ == '__main__':`r`n    main()`r`n")

[System.IO.File]::WriteAllText(
    (Join-Path $OutputDir '.gtit-runtime-ready'),
    "LibreTranslate runtime per GameTranslator IT`n")
Write-Host "Runtime pronto: $OutputDir"

# robocopy restituisce 1 quando la copia riesce: senza un exit esplicito il
# chiamante leggerebbe quel valore come errore.
exit 0
