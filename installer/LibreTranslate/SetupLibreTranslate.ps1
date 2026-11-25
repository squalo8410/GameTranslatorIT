$ErrorActionPreference = 'Stop'

# Installa il servizio di traduzione locale LibreTranslate (basato su Argos Translate).
# La prima esecuzione scarica tutti i modelli di traduzione disponibili: serve connessione Internet solo in questa fase.

$installRoot = Join-Path $env:LOCALAPPDATA 'GameTranslatorIT'
$venv = Join-Path $installRoot 'LibreTranslate'
$pythonExe = Join-Path $venv 'Scripts\python.exe'
$pipExe = Join-Path $venv 'Scripts\pip.exe'
$ltExe = Join-Path $venv 'Scripts\libretranslate.exe'

$python = Get-Command python -ErrorAction SilentlyContinue
if (-not $python) {
    throw "Python 3 non trovato. Installa Python 3.10 o successivo da https://www.python.org/downloads/ e riprova."
}

Write-Host "Creazione ambiente virtuale in: $venv"
New-Item -ItemType Directory -Force -Path $installRoot | Out-Null
if (-not (Test-Path $pythonExe)) {
    & python -m venv $venv
    if ($LASTEXITCODE -ne 0) { throw "Creazione venv non riuscita." }
}

Write-Host "Aggiornamento pip e installazione di LibreTranslate..."
& $pythonExe -m pip install --upgrade pip
if ($LASTEXITCODE -ne 0) { throw "Aggiornamento pip non riuscito." }
& $pipExe install libretranslate
if ($LASTEXITCODE -ne 0) { throw "Installazione LibreTranslate non riuscita." }

Write-Host "Installazione di tutti i modelli di traduzione disponibili (può richiedere diversi minuti)..."
$env:PYTHONIOENCODING = 'utf-8'
$modelScript = @'
import argostranslate.package as p
p.update_package_index()
installed = {(x.from_code, x.to_code) for x in p.get_installed_packages()}
for package in p.get_available_packages():
    pair = (package.from_code, package.to_code)
    if pair in installed:
        continue
    print(f"Installo {package.code}...", flush=True)
    try:
        p.install_from_path(package.download())
    except Exception as exc:
        print(f"Modello {package.code} non installato: {exc}", flush=True)
'@
& $pythonExe -c $modelScript
if ($LASTEXITCODE -ne 0) { throw "Installazione dei modelli di traduzione non riuscita." }

$startScript = Join-Path $installRoot 'AvviaLibreTranslate.cmd'
$content = @"
@echo off
REM Avvia il servizio di traduzione locale su loopback.
set PYTHONIOENCODING=utf-8
"$ltExe" --host 127.0.0.1 --port 5000 --disable-web-ui
"@
Set-Content -Path $startScript -Value $content -Encoding ASCII

Write-Host ""
Write-Host "Installazione completata."
Write-Host "Avvia il servizio con: $startScript"
Write-Host "Modelli di traduzione installati. Il servizio non richiede Internet."
