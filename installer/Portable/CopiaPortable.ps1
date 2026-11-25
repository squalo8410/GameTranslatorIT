param(
    [string]$Destination = ''
)
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$publishDir = Join-Path $root 'dist\GameTranslatorIT'

if (-not (Test-Path (Join-Path $publishDir 'GameTranslatorIT.App.exe'))) {
    Write-Host "Eseguibile non trovato: pubblicazione dell'app..."
    & (Join-Path $root 'installer\publish.ps1')
}

if ($Destination -eq '') {
    Add-Type -AssemblyName System.Windows.Forms
    $dialog = New-Object System.Windows.Forms.FolderBrowserDialog
    $dialog.Description = 'Scegli la cartella dove copiare GameTranslator IT'
    $dialog.ShowNewFolderButton = $true
    if ($dialog.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) {
        Write-Host 'Operazione annullata.'
        exit 0
    }
    $Destination = $dialog.SelectedPath
}

$target = Join-Path $Destination 'GameTranslatorIT'

if (Test-Path -LiteralPath $target) {
    $existing = (Get-ChildItem -LiteralPath $target -Force -ErrorAction SilentlyContinue | Measure-Object).Count
    if ($existing -gt 0) {
        $answer = Read-Host "La cartella $target contiene gia' dei file. Sovrascrivere? (s/N)"
        if ($answer -notmatch '^[sS]') {
            Write-Host 'Operazione annullata.'
            exit 0
        }
    }
}

New-Item -ItemType Directory -Force -Path $target | Out-Null
Copy-Item -Path (Join-Path $publishDir '*') -Destination $target -Recurse -Force

$launcher = Join-Path $target 'Avvia GameTranslator IT.cmd'
$launcherText = @"
@echo off
cd /d "%~dp0"
start "" "%~dp0GameTranslatorIT.App.exe"
"@
Set-Content -LiteralPath $launcher -Value $launcherText -Encoding ASCII

Write-Host "Copia completata in: $target"
Write-Host "Avvio: $launcher"
