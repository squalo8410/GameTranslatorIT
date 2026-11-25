param(
    [string]$PfxPath = '',
    [string]$PfxPassword = '',
    [string]$Version = ''
)
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$publishDir = Join-Path $root 'dist\GameTranslatorIT'
$manifestSource = Join-Path $PSScriptRoot 'Package.appxmanifest'
$manifestTarget = Join-Path $publishDir 'AppxManifest.xml'
$outputDir = Join-Path $root 'dist\installer'
$msixPath = Join-Path $outputDir 'GameTranslatorIT.msix'

if (-not (Test-Path (Join-Path $publishDir 'GameTranslatorIT.App.exe'))) {
    Write-Host "Eseguibile non trovato: pubblicazione dell'app..."
    & (Join-Path $root 'installer\publish.ps1')
}

New-Item -ItemType Directory -Force -Path $outputDir | Out-Null

# Immagini minime richieste dal manifest (PNG 1x1 trasparente).
$imagesDir = Join-Path $publishDir 'images'
New-Item -ItemType Directory -Force -Path $imagesDir | Out-Null
$pngBase64 = 'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg=='
foreach ($name in @('storelogo.png','square150x150logo.png','square44x44logo.png','wide310x150logo.png','splashscreen.png')) {
    $target = Join-Path $imagesDir $name
    if (-not (Test-Path $target)) {
        [System.IO.File]::WriteAllBytes($target, [Convert]::FromBase64String($pngBase64))
    }
}

Copy-Item -Path $manifestSource -Destination $manifestTarget -Force

# MSIX richiede sempre quattro componenti numerici nella versione.
if (-not [string]::IsNullOrWhiteSpace($Version)) {
    if ($Version -notmatch '^\d+\.\d+\.\d+(\.\d+)?$') { throw "Versione MSIX non valida: $Version" }
    $msixVersion = $Version
    if (($msixVersion -split '\.').Count -eq 3) { $msixVersion += '.0' }
    $manifestXml = [xml](Get-Content -Raw -LiteralPath $manifestTarget)
    $manifestXml.Package.Identity.Version = $msixVersion
    $manifestXml.Save($manifestTarget)
    Write-Host "Versione MSIX impostata: $msixVersion"
}

# Cerca makeappx/signtool prima nel Windows SDK, poi nel pacchetto NuGet BuildTools.
$toolDirs = @(
    (Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'),
    (Join-Path $env:ProgramFiles 'Windows Kits\10\bin'),
    (Join-Path $env:USERPROFILE '.nuget\packages\microsoft.windows.sdk.buildtools')
)

function Find-Tool([string]$name) {
    foreach ($dir in $toolDirs) {
        if (-not (Test-Path -LiteralPath $dir)) { continue }
        $candidates = Get-ChildItem -LiteralPath $dir -Filter $name -Recurse -ErrorAction SilentlyContinue
        $x64 = $candidates | Where-Object { $_.FullName -match '\\x64\\' } | Select-Object -First 1
        if ($x64) { return $x64.FullName }
        $first = $candidates | Select-Object -First 1
        if ($first) { return $first.FullName }
    }
    return $null
}

$makeappx = Find-Tool 'makeappx.exe'
if (-not $makeappx) {
    throw "makeappx.exe non trovato. Eseguire: dotnet restore installer\MSIX\MsixTools.csproj"
}
$signtool = Find-Tool 'signtool.exe'
if (-not $signtool) {
    throw "signtool.exe non trovato. Eseguire: dotnet restore installer\MSIX\MsixTools.csproj"
}

Write-Host "Creazione pacchetto MSIX: $msixPath"
& $makeappx pack /d $publishDir /p $msixPath /o
if ($LASTEXITCODE -ne 0) { throw "makeappx non riuscito (codice $LASTEXITCODE)." }

# Se non viene passato un certificato, crea uno self-signed locale per il sideloading.
if ($PfxPath -eq '') {
    $subject = 'CN=GameTranslatorIT'
    $existing = Get-ChildItem 'Cert:\CurrentUser\My' -ErrorAction SilentlyContinue |
        Where-Object { $_.Subject -eq $subject -and $_.NotAfter -gt (Get-Date) } |
        Select-Object -First 1

    if ($existing) {
        $cert = $existing
        Write-Host "Certificato di sviluppo esistente: $($cert.Thumbprint)"
    }
    else {
        $cert = New-SelfSignedCertificate -Type CodeSigningCert `
            -Subject $subject `
            -KeyUsage DigitalSignature `
            -FriendlyName 'GameTranslator IT (sviluppo)' `
            -NotAfter (Get-Date).AddYears(5) `
            -CertStoreLocation 'Cert:\CurrentUser\My'
        Write-Host "Creato certificato di sviluppo: $($cert.Thumbprint)"
    }

    $PfxPath = Join-Path $outputDir 'GameTranslatorIT-dev.pfx'
    $securePwd = ConvertTo-SecureString -String 'gtit-local' -Force -AsPlainText
    Export-PfxCertificate -Cert $cert -FilePath $PfxPath -Password $securePwd -Force | Out-Null
    Export-Certificate -Cert $cert -FilePath (Join-Path $outputDir 'GameTranslatorIT-dev.cer') -Force | Out-Null
    $PfxPassword = 'gtit-local'

    # Fiducia locale per poter installare il pacchetto su questo PC.
    Import-Certificate -FilePath (Join-Path $outputDir 'GameTranslatorIT-dev.cer') `
        -CertStoreLocation 'Cert:\CurrentUser\TrustedPeople' -Confirm:$false | Out-Null
    Import-Certificate -FilePath (Join-Path $outputDir 'GameTranslatorIT-dev.cer') `
        -CertStoreLocation 'Cert:\CurrentUser\Root' -Confirm:$false | Out-Null
}
elseif (-not (Test-Path -LiteralPath $PfxPath)) {
    throw "File PFX non trovato: $PfxPath"
}

$secure = ConvertTo-SecureString -String $PfxPassword -Force -AsPlainText
& $signtool sign /fd SHA256 /a /f $PfxPath /p $PfxPassword $msixPath
if ($LASTEXITCODE -ne 0) { throw "Firma del pacchetto non riuscita (codice $LASTEXITCODE)." }

Write-Host "MSIX firmato e pronto: $msixPath"
Write-Host "Certificato pubblico: $(Join-Path $outputDir 'GameTranslatorIT-dev.cer')"
