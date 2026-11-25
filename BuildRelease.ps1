param(
    [string]$Version = '',
    [switch]$SkipMsix,
    [string]$PfxPath = '',
    [string]$PfxPassword = ''
)
$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot

if ([string]::IsNullOrWhiteSpace($Version)) {
    $props = Get-Content -Raw -LiteralPath (Join-Path $root 'Directory.Build.props')
    $match = [regex]::Match($props, '<Version>(\d+\.\d+\.\d+)</Version>')
    if (-not $match.Success) { throw "Versione non trovata in Directory.Build.props" }
    $Version = $match.Groups[1].Value
}

if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Formato versione non valido: $Version"
}

$distDir = Join-Path $root 'dist\installer'
$publishDir = Join-Path $root 'dist\GameTranslatorIT'
$releaseDir = Join-Path $root "dist\Releases\v$Version"
New-Item -ItemType Directory -Force -Path $distDir, $releaseDir | Out-Null

Write-Host "=== Pubblicazione $Version ==="
# I file generati dal passaggio MSIX non devono finire nell'installer EXE.
foreach ($stale in @((Join-Path $publishDir 'AppxManifest.xml'), (Join-Path $publishDir 'images'))) {
    if (Test-Path -LiteralPath $stale) { Remove-Item -LiteralPath $stale -Recurse -Force }
}
& (Join-Path $root 'installer\publish.ps1') -Version $Version
if ($LASTEXITCODE -ne 0) { throw "Pubblicazione non riuscita." }

Write-Host "=== Installer EXE ==="
$iscc = (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source
if ([string]::IsNullOrWhiteSpace($iscc)) {
    $candidates = @(
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
        'C:\Users\corci\AppData\Local\Programs\Inno Setup 6\ISCC.exe'
    )
    $iscc = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if ([string]::IsNullOrWhiteSpace($iscc) -or -not (Test-Path -LiteralPath $iscc)) {
    throw "ISCC.exe non trovato. Installa Inno Setup 6 oppure aggiungi ISCC.exe al PATH."
}
& $iscc (Join-Path $root 'installer\InnoSetup\GameTranslatorIT.iss') "/DMyAppVersion=$Version"
if ($LASTEXITCODE -ne 0) { throw "Compilazione installer non riuscita." }

$setupSource = Join-Path $distDir "GameTranslatorIT-Setup-$Version.exe"
if (-not (Test-Path -LiteralPath $setupSource)) { throw "Installer non generato: $setupSource" }
Copy-Item -LiteralPath $setupSource -Destination (Join-Path $releaseDir "GameTranslatorIT-Setup-$Version.exe") -Force

Write-Host "=== MSIX ==="
if (-not $SkipMsix) {
    dotnet restore (Join-Path $root 'installer\MSIX\MsixTools.csproj')
    if ($LASTEXITCODE -ne 0) { throw "Restore strumenti MSIX non riuscito." }
    $msixParams = @{ Version = $Version }
    if (-not [string]::IsNullOrWhiteSpace($PfxPath)) { $msixParams.PfxPath = $PfxPath }
    if (-not [string]::IsNullOrWhiteSpace($PfxPassword)) { $msixParams.PfxPassword = $PfxPassword }
    & (Join-Path $root 'installer\MSIX\build-msix.ps1') @msixParams
    if ($LASTEXITCODE -ne 0) { throw "Build MSIX non riuscito." }
    Copy-Item -LiteralPath (Join-Path $distDir 'GameTranslatorIT.msix') `
        -Destination (Join-Path $releaseDir "GameTranslatorIT-$Version.msix") -Force
}

Write-Host "=== Portable ZIP ==="
$portableStage = Join-Path $releaseDir 'portable-stage'
if (Test-Path -LiteralPath $portableStage) { Remove-Item -LiteralPath $portableStage -Recurse -Force }
$portableApp = Join-Path $portableStage 'GameTranslatorIT'
New-Item -ItemType Directory -Force -Path $portableApp | Out-Null
Get-ChildItem -LiteralPath $publishDir -Force |
    Where-Object { $_.Name -notin @('AppxManifest.xml', 'images') } |
    Copy-Item -Destination $portableApp -Recurse -Force

$launcher = Join-Path $portableApp 'Avvia GameTranslator IT.cmd'
$launcherText = @"
@echo off
cd /d "%~dp0"
start "" "%~dp0GameTranslatorIT.App.exe"
"@
Set-Content -LiteralPath $launcher -Value $launcherText -Encoding ASCII

$zipPath = Join-Path $releaseDir "GameTranslatorIT-Portable-$Version.zip"
if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
Compress-Archive -Path $portableApp -DestinationPath $zipPath -CompressionLevel Optimal -Force
Remove-Item -LiteralPath $portableStage -Recurse -Force

Write-Host "=== Checksum SHA256 ==="
$assets = @(
    (Join-Path $releaseDir "GameTranslatorIT-Setup-$Version.exe"),
    $zipPath
)
if (-not $SkipMsix) { $assets += (Join-Path $releaseDir "GameTranslatorIT-$Version.msix") }

foreach ($asset in $assets) {
    if (-not (Test-Path -LiteralPath $asset)) { throw "Asset mancante: $asset" }
    $hash = (Get-FileHash -LiteralPath $asset -Algorithm SHA256).Hash
    $checksumPath = "$asset.sha256"
    [System.IO.File]::WriteAllText($checksumPath, "$hash  $([System.IO.Path]::GetFileName($asset))`n")
    Write-Host "$([System.IO.Path]::GetFileName($asset)) -> $hash"
}

$notes = Join-Path $releaseDir 'release-notes.md'
if (-not (Test-Path -LiteralPath $notes)) {
    [System.IO.File]::WriteAllText($notes, @"
# GameTranslator IT $Version

Supporto completo alle lingue: 58 lingue selezionabili, rilevamento automatico della sorgente, cambio lingua senza riavvio e fallback online esplicito per le 8 lingue senza modello Argos. Caratteri non latini e scrittura da destra a sinistra gestiti nell'overlay.

## Asset pubblicati
- GameTranslatorIT-Setup-$Version.exe (installer EXE)
- GameTranslatorIT-Portable-$Version.zip (copia portable)
- GameTranslatorIT-$Version.msix (pacchetto MSIX firmato)
- File `.sha256` corrispondenti per la verifica integrità

Le impostazioni utente in `%APPDATA%\\GameTranslatorIT` vengono conservate.
"@)
}

$pfxInRelease = Join-Path $releaseDir 'GameTranslatorIT-dev.pfx'
if (Test-Path -LiteralPath $pfxInRelease) { Remove-Item -LiteralPath $pfxInRelease -Force }

Write-Host "Rilascio pronto: $releaseDir"
Get-ChildItem -LiteralPath $releaseDir | Select-Object Name, Length | Format-Table -AutoSize | Out-Host
