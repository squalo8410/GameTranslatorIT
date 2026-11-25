param()
$ErrorActionPreference = 'Stop'

$out = Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'dist\installer'
$cer = Join-Path $out 'GameTranslatorIT-dev.cer'
$msix = Join-Path $out 'GameTranslatorIT.msix'

$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Start-Process powershell -ArgumentList '-NoProfile -ExecutionPolicy Bypass -File', "`"$PSCommandPath`"" -Verb RunAs
    exit
}

if (-not (Test-Path -LiteralPath $cer)) { throw "Certificato non trovato: $cer" }
if (-not (Test-Path -LiteralPath $msix)) { throw "Pacchetto non trovato: $msix" }

Import-Certificate -FilePath $cer -CertStoreLocation 'Cert:\LocalMachine\TrustedPeople' -Confirm:$false | Out-Null
Import-Certificate -FilePath $cer -CertStoreLocation 'Cert:\LocalMachine\Root' -Confirm:$false | Out-Null

Add-AppxPackage -Path $msix -ForceUpdateFromAnyVersion
Write-Host 'MSIX installato.'
